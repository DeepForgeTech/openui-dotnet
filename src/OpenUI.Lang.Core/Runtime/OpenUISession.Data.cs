using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Semantics;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Runtime;

/// <summary>Queries and mutations (language.md, sections 6.1, 6.2 and 6.4).</summary>
public sealed partial class OpenUISession
{
    // The shortest refresh, so a program cannot turn a timer into a busy loop against a host's tools.
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(1);

    // The longest period a timer takes: 2^32 - 2 milliseconds.
    private const double MaxRefreshSeconds = 4294967;

    // A tool name is copied into each error about it, so one long name cannot fill many messages.
    private const int MaxToolNameLength = 256;

    private static readonly TimeSpan RateWindow = TimeSpan.FromSeconds(1);

    private readonly Dictionary<string, QueryCell> _queries = new(StringComparer.Ordinal);
    private readonly Queue<string> _heldQueries = new();
    private ITimer? _heldTimer;
    private DateTimeOffset _windowStart;
    private long _window;
    private int _windowFetches;
    private int _refreshingQueries;
    private readonly Dictionary<string, MutationCell> _mutations = new(StringComparer.Ordinal);

    /// <summary>A query's latest result and fetch state; all fields null or false for an unknown name (section 6.1).</summary>
    public QueryStatus GetQueryStatus(string queryId)
    {
        ArgumentNullException.ThrowIfNull(queryId);
        lock (_gate)
        {
            return _queries.TryGetValue(queryId, out var cell) ? cell.ToStatus() : QueryStatus.Unknown;
        }
    }

    /// <summary>A mutation's status; idle when it has not run (section 6.2).</summary>
    public MutationStatus GetMutationStatus(string mutationId)
    {
        ArgumentNullException.ThrowIfNull(mutationId);
        lock (_gate)
        {
            return _mutations.TryGetValue(mutationId, out var cell) ? cell.Status : MutationStatus.Idle;
        }
    }

    private Dictionary<string, OuiValue> QueryDataLocked()
    {
        var data = new Dictionary<string, OuiValue>(StringComparer.Ordinal);
        foreach (var (name, cell) in _queries)
        {
            if (cell.HasData)
            {
                data[name] = cell.Data;
            }
        }

        foreach (var (name, cell) in _mutations)
        {
            data[name] = cell.Status.ToObject();
        }

        return data;
    }

    private ImmutableDictionary<string, QueryStatus> QueryStatusesLocked() =>
        _queries.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.ToStatus(), StringComparer.Ordinal);

    private IEnumerable<OpenUIError> DataErrorsLocked() =>
        [.. _queries.Values.SelectMany(cell => cell.Errors), .. _mutations.Values.Where(cell => cell.Error is not null).Select(cell => cell.Error!)];

    private void ClearDataErrorsLocked()
    {
        foreach (var cell in _queries.Values)
        {
            cell.ClearErrors();
        }

        foreach (var cell in _mutations.Values)
        {
            cell.Error = null;
        }
    }

    // At stream end: forget queries the program lost, and start the new or changed ones.
    private void SyncDataLocked()
    {
        foreach (var name in _queries.Keys.Where(name => !IsStatement(name, StatementKind.Query)).ToList())
        {
            _queries.Remove(name, out var cell);
            CancelFetchLocked(cell!);
            StopRefreshLocked(cell!);
        }

        foreach (var name in _mutations.Keys.Where(name => !IsStatement(name, StatementKind.Mutation)).ToList())
        {
            _mutations.Remove(name);
        }

        var evaluator = NewEvaluatorLocked();
        foreach (var statement in _document.Statements.Where(s => s.Kind == StatementKind.Query))
        {
            StartFetchLocked(statement, force: false, evaluator);
        }
    }

    private bool IsStatement(string name, StatementKind kind) => _document.TryGetStatement(name, out var statement) && statement.Kind == kind;

    private void StopDataLocked()
    {
        _heldTimer?.Dispose();
        _heldTimer = null;
        _heldQueries.Clear();
        foreach (var cell in _queries.Values)
        {
            cell.Unqueue();
            CancelFetchLocked(cell);
            StopRefreshLocked(cell);
        }
    }

    // Only $vars written literally in the arguments retrigger a fetch (section 6.1).
    private void RefetchDependentsLocked(HashSet<string> keys)
    {
        if (_streaming || keys.Count == 0)
        {
            return;
        }

        var dependents = _document.Statements.Where(s => s.Kind == StatementKind.Query && s.Bound.Tool!.LiteralStateDeps.Any(keys.Contains)).ToList();
        if (dependents.Count == 0)
        {
            return;
        }

        var evaluator = NewEvaluatorLocked();
        foreach (var statement in dependents)
        {
            StartFetchLocked(statement, force: true, evaluator);
        }
    }

    private void StartFetchLocked(Statement statement, bool force, Evaluator evaluator)
    {
        if (!_queries.TryGetValue(statement.Name, out var cell))
        {
            cell = new QueryCell();
            _queries[statement.Name] = cell;
        }

        var call = statement.Bound.Tool!;
        OuiValue toolValue = default;
        OuiValue args = default;
        var evaluated = Guarded(() => evaluator.TryEvaluateIn(statement.Name, call.Tool, out toolValue)
            && evaluator.TryEvaluateIn(statement.Name, call.Arguments, out args)
            && evaluator.TryChargeJson(args));
        ScheduleRefreshLocked(statement, cell, evaluator);
        if (!evaluated)
        {
            // A limit stopped the arguments and reported it; a tool never sees half-built arguments.
            CancelFetchLocked(cell);
            cell.ForgetArguments();
            return;
        }

        var tool = ToolName(toolValue);
        string key;
        try
        {
            key = ArgumentsKey(args);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            CancelFetchLocked(cell);
            cell.Fail(ToolFailure(OpenUIErrorSource.Query, statement.Name, tool, ex));
            return;
        }

        if (!force && cell.Asked(tool, key))
        {
            return;
        }

        cell.Ask(tool, key);
        CancelFetchLocked(cell);
        cell.ClearErrors();
        if (ToolProblem(OpenUIErrorSource.Query, statement.Name, tool) is { } problem)
        {
            cell.Fail(problem);
            return;
        }

        // Over a rate a query waits its turn and then fetches once, with the arguments of that moment,
        // so a program cannot hammer a tool and the last state written still gets its answer.
        if (!TakeFetchLocked(cell))
        {
            if (cell.Hold())
            {
                _heldQueries.Enqueue(statement.Name);
            }

            StartHeldTimerLocked();
            return;
        }

        var cancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var fetch = cell.BeginFetch(cancel);

        // Read here: by the time the task runs, a later commit may have disposed the source.
        var token = cancel.Token;
        Track(Task.Run(() => FetchAsync(statement.Name, cell, fetch, tool, args, token)));
    }

    private bool HasFetchLeftLocked()
    {
        var now = _options.Time.GetUtcNow();
        if (now - _windowStart >= RateWindow)
        {
            _windowStart = now;
            _window++;
            _windowFetches = 0;
        }

        return _windowFetches < Math.Max(_limits.MaxSessionFetchesPerSecond, 1);
    }

    private bool TakeFetchLocked(QueryCell cell)
    {
        if (!HasFetchLeftLocked() || !cell.TakeFetch(_window, Math.Max(_limits.MaxFetchesPerSecond, 1)))
        {
            return false;
        }

        _windowFetches++;
        return true;
    }

    // A stall past the window's end leaves a negative wait, which a timer refuses.
    private void StartHeldTimerLocked()
    {
        var wait = _windowStart + RateWindow - _options.Time.GetUtcNow();
        _heldTimer ??= _options.Time.CreateTimer(_ => OnHeldQueries(), null, wait > TimeSpan.Zero ? wait : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    // One timer for the whole queue, and only what this window can start leaves it, so the cost of a
    // tick does not grow with how many queries are waiting.
    private void OnHeldQueries() => OnTimer("a held fetch", () =>
    {
        _heldTimer?.Dispose();
        _heldTimer = null;
        var evaluator = NewEvaluatorLocked();
        while ((_streaming || HasFetchLeftLocked()) && _heldQueries.TryDequeue(out var name))
        {
            if (!_queries.TryGetValue(name, out var cell) || !cell.Unqueue())
            {
                continue;
            }

            if (_streaming)
            {
                // The end of the stream fetches it.
                cell.ForgetArguments();
            }
            else if (TryGetQueryLocked(name, out var statement))
            {
                StartFetchLocked(statement, force: true, evaluator);
            }
        }

        if (_heldQueries.Count > 0)
        {
            StartHeldTimerLocked();
        }

        return true;
    });

    // Nothing above a timer callback catches: an exception here would end the process.
    private void OnTimer(string what, Func<bool> commit)
    {
        try
        {
            CommitIfAlive(commit);
        }
        catch (Exception ex)
        {
            Trace.TraceError("OpenUISession: {0} failed: {1}", what, ex);
        }
    }

    private bool TryGetQueryLocked(string name, [NotNullWhen(true)] out Statement? statement) =>
        _document.TryGetStatement(name, out statement) && statement.Kind == StatementKind.Query;

    // Tool code can run inside Cancel, so it is detached here and cancelled after the lock is released.
    private void CancelFetchLocked(QueryCell cell)
    {
        if (cell.Stop() is { } cancellation)
        {
            _cancellations.Add(cancellation);
        }
    }

    private void RunPendingCancellations()
    {
        CancellationTokenSource[] pending;
        lock (_gate)
        {
            if (_cancellations.Count == 0)
            {
                return;
            }

            pending = [.. _cancellations];
            _cancellations.Clear();
        }

        foreach (var cancellation in pending)
        {
            CancelQuietly(cancellation);
            cancellation.Dispose();
        }
    }

    // A tool's cancellation callback is host code; what it throws must not reach the commit or a timer.
    private static void CancelQuietly(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (AggregateException ex)
        {
            Trace.TraceError("OpenUISession: a tool's cancellation callback threw: {0}", ex);
        }
    }

    private async Task FetchAsync(string name, QueryCell cell, QueryCell.Fetching fetch, string tool, OuiValue args, CancellationToken ct)
    {
        OuiValue result = default;
        OpenUIError? failure = null;
        try
        {
            result = Admitted(await InvokeToolAsync(tool, args, ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            failure = ToolFailure(OpenUIErrorSource.Query, name, tool, ex);
        }

        CommitIfAlive(() =>
        {
            // A result for stale arguments is discarded (section 6.1).
            if (!cell.Finish(fetch))
            {
                return false;
            }

            if (failure is null)
            {
                cell.Succeed(result);
            }
            else
            {
                cell.Fail(failure);
            }

            return true;
        });
    }

    private void ScheduleRefreshLocked(Statement statement, QueryCell cell, Evaluator evaluator)
    {
        var name = statement.Name;
        var seconds = 0.0;
        if (statement.Bound.Tool!.RefreshSeconds is { } refresh)
        {
            Guarded(() => evaluator.EvaluateIn(name, refresh)).TryGetNumber(out seconds);
        }

        var interval = seconds > 0 && double.IsFinite(seconds)
            ? TimeSpan.FromSeconds(Math.Clamp(seconds, MinRefreshInterval.TotalSeconds, MaxRefreshSeconds))
            : TimeSpan.Zero;
        if (interval > TimeSpan.Zero && !cell.IsRefreshing && _refreshingQueries >= _limits.MaxRefreshingQueries)
        {
            _otherErrors.Add(new OpenUIError
            {
                Source = OpenUIErrorSource.Runtime,
                Code = OpenUIErrorCode.LimitExceeded,
                Message = $"More than {_limits.MaxRefreshingQueries} queries refresh on a timer; the rest fetch once.",
            });
            return;
        }

        var wasRefreshing = cell.IsRefreshing;
        cell.SetRefresh(interval, every => _options.Time.CreateTimer(_ => OnRefreshTimer(name), null, every, Timeout.InfiniteTimeSpan));
        _refreshingQueries += (cell.IsRefreshing ? 1 : 0) - (wasRefreshing ? 1 : 0);
    }

    private void StopRefreshLocked(QueryCell cell)
    {
        _refreshingQueries -= cell.IsRefreshing ? 1 : 0;
        cell.StopRefresh();
    }

    private void OnRefreshTimer(string name) => OnTimer("a refresh tick", () =>
    {
        _queries.TryGetValue(name, out var cell);
        cell?.RearmRefresh();

        // A tick leaves a running fetch alone, or a slow tool would never finish (section 6.1).
        if (_streaming || !TryGetQueryLocked(name, out var statement) || cell is { IsBusy: true })
        {
            return false;
        }

        StartFetchLocked(statement, force: true, NewEvaluatorLocked());
        return true;
    });

    private async Task<ActionOutcome?> RunMutationAsync(Statement statement, int stepsRun, EvaluationBudget budget, CancellationToken ct)
    {
        var tool = "";
        OuiValue args = default;
        MutationCell? cell = null;
        ActionOutcome? early = null;
        var lifetime = CancellationToken.None;
        Commit(() =>
        {
            lifetime = _lifetime.Token;
            if (_streaming)
            {
                early = new ActionOutcome(ActionOutcomeKind.Rejected, stepsRun, null);
                return false;
            }

            if (!_mutations.TryGetValue(statement.Name, out cell))
            {
                cell = new MutationCell();
                _mutations[statement.Name] = cell;
            }

            if (cell.Status.State == MutationState.Loading)
            {
                early = new ActionOutcome(ActionOutcomeKind.Rejected, stepsRun, null);
                return false;
            }

            var call = statement.Bound.Tool!;
            var evaluator = NewEvaluatorLocked(budget);
            OuiValue toolValue = default;
            OuiValue argsValue = default;
            if (!Guarded(() => evaluator.TryEvaluateIn(statement.Name, call.Tool, out toolValue)
                && evaluator.TryEvaluateIn(statement.Name, call.Arguments, out argsValue)
                && evaluator.TryChargeJson(argsValue)))
            {
                // A limit stopped the arguments and reported it; a tool never sees half-built arguments.
                early = new ActionOutcome(ActionOutcomeKind.Stopped, stepsRun, _otherErrors.LastAdded);
                return true;
            }

            tool = ToolName(toolValue);
            args = argsValue;
            cell.Error = null;
            if (ToolProblem(OpenUIErrorSource.Mutation, statement.Name, tool) is { } problem)
            {
                early = FinishMutationLocked(cell, problem, stepsRun);
                return true;
            }

            cell.Status = new MutationStatus(MutationState.Loading, OuiValue.Null, null);
            return true;
        });
        if (early is not null)
        {
            return early;
        }

        ActionOutcome? outcome = null;
        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime);
        try
        {
            var result = Admitted(await InvokeToolAsync(tool, args, linked.Token).ConfigureAwait(false));
            CommitIfAlive(() =>
            {
                cell!.Status = new MutationStatus(MutationState.Success, result, null);
                return true;
            });
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            CommitIfAlive(() =>
            {
                cell!.Status = MutationStatus.Idle;
                return true;
            });
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            CommitIfAlive(() =>
            {
                outcome = FinishMutationLocked(cell!, ToolFailure(OpenUIErrorSource.Mutation, statement.Name, tool, ex), stepsRun);
                return true;
            });
        }
        finally
        {
            linked.Dispose();
        }

        return outcome;
    }

    // Calls past the limit wait their turn. WaitAsync so that cancellation and disposal finish even when
    // a host tool ignores its token; such a tool keeps its slot until it really ends, since it is still
    // running against the host.
    private async Task<OuiValue> InvokeToolAsync(string tool, OuiValue args, CancellationToken ct)
    {
        await _toolSlots.WaitAsync(ct).ConfigureAwait(false);
        Task<OuiValue> call;
        try
        {
            call = _options.Tools!.InvokeAsync(tool, args, ct).AsTask();
        }
        catch
        {
            _toolSlots.Release();
            throw;
        }

        _ = call.ContinueWith(_ => _toolSlots.Release(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return await call.WaitAsync(ct).ConfigureAwait(false);
    }

    // A result too deep to walk safely fails its tool call and never reaches the state or a walk.
    private OuiValue Admitted(OuiValue result) => IsTooDeep(result)
        ? throw new OpenUIToolException($"The tool's result nests deeper than {_limits.MaxValueDepth} levels.", OpenUIErrorCode.ToolError)
        : result;

    private ActionOutcome FinishMutationLocked(MutationCell cell, OpenUIError error, int stepsRun)
    {
        cell.Error = error;
        cell.Status = new MutationStatus(MutationState.Error, OuiValue.Null, error.Message);
        return new ActionOutcome(ActionOutcomeKind.Stopped, stepsRun, error);
    }

    // The provider is host code running under the lock; what it throws becomes this tool's error.
    private OpenUIError? ToolProblem(OpenUIErrorSource source, string statementId, string tool)
    {
        try
        {
            if (_options.Tools is not { } tools || !tools.Contains(tool))
            {
                return ToolNotFound(source, statementId, tool);
            }

            // A query runs on load with no click, so it cannot be how a model's text changes anything (section 6.2).
            return source == OpenUIErrorSource.Query && tools.GetEffect(tool) != OpenUIToolEffect.Read
                ? new OpenUIError
                {
                    Source = source,
                    Code = OpenUIErrorCode.ToolError,
                    Message = $"\"{tool}\" is not a read-only tool, so a Query cannot call it.",
                    StatementId = statementId,
                    ToolName = tool,
                    Hint = "Use a Mutation and @Run.",
                }
                : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return ToolFailure(source, statementId, tool, ex);
        }
    }

    private OpenUIError ToolNotFound(OpenUIErrorSource source, string statementId, string tool)
    {
        var names = _options.Tools?.ToolNames ?? [];
        return new OpenUIError
        {
            Source = source,
            Code = OpenUIErrorCode.ToolNotFound,
            Message = $"Unknown tool \"{tool}\".",
            StatementId = statementId,
            ToolName = tool,
            Hint = names.Count == 0 ? "No tools are available." : "Available tools: " + string.Join(", ", names.Order(StringComparer.Ordinal)),
        };
    }

    // This goes to the model and can reach the page, where an ordinary exception's text could hold a
    // connection string. A tool throws OpenUIToolException for a message it means to be read.
    private static OpenUIError ToolFailure(OpenUIErrorSource source, string statementId, string tool, Exception exception)
    {
        if (exception is not OpenUIToolException)
        {
            Trace.TraceError("OpenUISession: the tool {0} threw: {1}", tool, exception);
        }

        return new OpenUIError
        {
            Source = source,
            Code = exception is OpenUIToolException known ? known.Code : OpenUIErrorCode.ToolError,
            Message = exception is OpenUIToolException ? exception.Message : $"The tool \"{tool}\" failed.",
            StatementId = statementId,
            ToolName = tool,
        };
    }

    private static string ToolName(OuiValue value)
    {
        if (value.TryGetString(out var name))
        {
            return name.Length <= MaxToolNameLength ? name : name[..MaxToolNameLength] + "...";
        }

        var measure = value.Measure;
        return measure.Size + measure.Chars > MaxToolNameLength ? "" : Ecma.ToString(value);
    }

    // A hash, so what a session keeps per query is small however large its arguments are.
    internal static string ArgumentsKey(OuiValue value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            value.WriteTo(writer);
        }

        return Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan));
    }

    private sealed class MutationCell
    {
        public MutationStatus Status { get; set; } = MutationStatus.Idle;

        public OpenUIError? Error { get; set; }
    }
}
