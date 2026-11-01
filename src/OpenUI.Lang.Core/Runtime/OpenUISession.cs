using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Editing;
using OpenUI.Lang.Library;
using OpenUI.Lang.Protocol;
using OpenUI.Lang.Semantics;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Runtime;

/// <summary>
/// One rendered program and its runtime: streaming, state, forms, queries, mutations and actions
/// (language.md, sections 4 to 6). Thread-safe. Every change is one commit: it runs under one lock
/// held only for synchronous work, gets one revision, and notifies after the lock is released, on
/// the thread that committed or, if another thread is already raising events, on that thread. Tools
/// run outside the lock. <see cref="Current"/> evaluates on demand, at most once per revision, and
/// never between the start and end of a commit.
/// </summary>
public sealed partial class OpenUISession : IAsyncDisposable
{
    // What one write to the persistent state dictionary costs, in evaluation steps.
    private const int StateWriteSteps = 8;

    private readonly Lock _gate = new();
    private readonly OpenUIParser _parser;
    private readonly OpenUISessionOptions _options;
    private readonly OpenUILimits _limits;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly StringBuilder _text = new();
    private readonly HashSet<Task> _inFlight = [];
    private readonly HashSet<ChannelWriter<bool>> _watchers = [];
    private readonly List<CancellationTokenSource> _cancellations = [];

    // Never disposed: a tool that ignores its token releases it after the session is gone.
    private readonly SemaphoreSlim _toolSlots;
    private DiagnosticBag _otherErrors;
    private OpenUIStreamParser _stream;
    private OpenUIDocument _document;
    private StateDefaultValues? _defaults;
    private ImmutableDictionary<string, OuiValue> _state;
    private FormStateSnapshot _forms;
    private OpenUIErrorList _lastEmitted = OpenUIErrorList.Empty;
    private volatile RenderSnapshot? _cached;
    private RenderSnapshot? _last;
    private long _revision;
    private long _evaluationFuel;
    private long _stateChars;
    private bool _streaming;
    private bool _evaluating;
    private bool _emitClear;
    private bool _notifyPending;
    private bool _notifying;
    private bool _disposed;

    private OpenUISession(OpenUILibrary library, OpenUISessionOptions options)
    {
        _options = options;
        _limits = options.Limits;
        _toolSlots = new SemaphoreSlim(Math.Max(_limits.MaxConcurrentToolCalls, 1));
        _parser = new OpenUIParser(library, new OpenUIParserOptions { Limits = _limits });
        _stream = _parser.CreateStream();
        _document = _stream.Current;
        _otherErrors = new DiagnosticBag(_limits.MaxErrors);
        _state = options.PersistedState?.Where(pair => !IsTooDeep(pair.Value)).ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary.Create<string, OuiValue>(StringComparer.Ordinal);
        _stateChars = _state.Sum(pair => StateChars(pair.Key, pair.Value));
        _forms = options.PersistedForms ?? FormStateSnapshot.Empty;
        State = new StateAccessor(this);
        Forms = new FormStateAccessor(this);
    }

    /// <summary>Says that <see cref="Current"/> is stale; read it to render again (language.md, sections 4 to 6).</summary>
    public event EventHandler<SessionInvalidatedEventArgs>? Invalidated;

    /// <summary>
    /// The error list, by the rules of language.md section 8.3: an empty list when a stream starts if
    /// the last one was not empty, then only a changed list at stream end and after later renders.
    /// </summary>
    public event EventHandler<OpenUIErrorList>? ErrorsChanged;

    /// <summary>The state store (section 5.1).</summary>
    public StateAccessor State { get; }

    /// <summary>Form fields (section 5.3).</summary>
    public FormStateAccessor Forms { get; }

    /// <summary>The evaluated view of the session, evaluated on demand and cached per revision. After disposal, the last one (language.md, sections 4 to 6).</summary>
    public RenderSnapshot Current
    {
        get
        {
            if (_cached is { } cached)
            {
                return cached;
            }

            lock (_gate)
            {
                return EvaluateLocked();
            }
        }
    }

    /// <summary>Creates a session for <paramref name="library"/> (language.md, sections 4 to 6).</summary>
    public static OpenUISession Create(OpenUILibrary library, OpenUISessionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        return new OpenUISession(library, options ?? new OpenUISessionOptions());
    }

    /// <summary>Yields the current view, then the latest one after each change; a slow reader skips views (language.md, sections 4 to 6).</summary>
    public async IAsyncEnumerable<RenderSnapshot> WatchAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        var signal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _watchers.Add(signal.Writer);
        }

        try
        {
            var snapshot = Current;
            yield return snapshot;
            while (await signal.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                signal.Reader.TryRead(out _);
                var next = Current;
                if (next.Revision != snapshot.Revision)
                {
                    snapshot = next;
                    yield return snapshot;
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _watchers.Remove(signal.Writer);
            }
        }
    }

    /// <summary>Starts a new stream: clears the errors and parses from nothing (language.md, sections 4 and 8.3).</summary>
    public void BeginStream() => Commit(BeginStreamLocked);

    /// <summary>Reads one chunk; chunks after the end of a stream start a new one (section 4, rule 7). Evaluation waits for <see cref="Current"/>.</summary>
    public void Append(ReadOnlySpan<char> chunk)
    {
        var text = chunk.ToString();
        Commit(() =>
        {
            if (!_streaming)
            {
                BeginStreamLocked();
            }

            AppendLocked(text);
        });
    }

    /// <summary>Ends the stream: finalizes the pending tail, applies state defaults and starts queries (section 4, rules 6 and 7).</summary>
    public void EndStream() => Commit(EndStreamLocked);

    /// <summary>Streams <paramref name="chunks"/>. When cancelled or the source fails, ends the stream if <see cref="OpenUISessionOptions.FinalizeOnCancel"/> and rethrows (language.md, sections 4 to 6).</summary>
    public async Task ConsumeAsync(IAsyncEnumerable<string> chunks, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        BeginStream();
        try
        {
            await foreach (var chunk in chunks.WithCancellation(ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                Append(chunk);
            }
        }
        catch (Exception)
        {
            if (_options.FinalizeOnCancel)
            {
                CommitIfAlive(EndStreamLocked);
            }

            throw;
        }

        EndStream();
    }

    /// <summary>Loads a stored message or program text as one commit: begin, append, end (section 4).</summary>
    public void Load(string storedOrProgramText)
    {
        ArgumentNullException.ThrowIfNull(storedOrProgramText);
        Commit(() => ReplaceProgramLocked(storedOrProgramText));
    }

    /// <summary>
    /// Merges a finished patch into the current program and renders the result, as one commit (section 7).
    /// State keys stay, and a query whose tool and arguments did not change is not fetched again.
    /// </summary>
    public MergeResult ApplyEdit(string patchText)
    {
        ArgumentNullException.ThrowIfNull(patchText);
        MergeResult? merged = null;
        Commit(() =>
        {
            merged = ProgramMerger.Merge(OpenUIMessage.Parse(_text.ToString()).Content, OpenUIMessage.Parse(patchText).Content, _limits);
            ReplaceProgramLocked(merged.Text);
            foreach (var error in merged.Errors)
            {
                _otherErrors.Add(error);
            }
        });
        return merged!;
    }

    /// <summary>Records a component renderer's failure as <c>render-error</c> (section 8.2).</summary>
    public void ReportRenderError(OuiComponent node, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(exception);
        Trace.TraceError("OpenUISession: the component {0} failed to render: {1}", node.Type, exception);
        Commit(() => _otherErrors.Add(new OpenUIError
        {
            Source = OpenUIErrorSource.Runtime,
            Code = OpenUIErrorCode.RenderError,
            Message = $"{node.Type} failed to render.",
            StatementId = node.StatementId,
            Component = node.Type,
        }));
    }

    /// <summary>Cancels tools and timers, completes watchers and detaches handlers. Then every member but <see cref="Current"/> throws (language.md, sections 4 to 6).</summary>
    public async ValueTask DisposeAsync()
    {
        Task[] running;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _last = EvaluateLocked();
            _disposed = true;
            StopDataLocked();
            foreach (var watcher in _watchers)
            {
                watcher.TryComplete();
            }

            _watchers.Clear();
            Invalidated = null;
            ErrorsChanged = null;
            running = [.. _inFlight];
        }

        RunPendingCancellations();
        try
        {
            CancelQuietly(_lifetime);
            await Task.WhenAll(running).ConfigureAwait(false);
        }
        finally
        {
            _lifetime.Dispose();
        }
    }

    /// <summary>Completes when no tool call, fetch or event handler is in flight, so a prerender or a test can settle before it reads the tree (language.md, section 6).</summary>
    /// <returns>A task that completes once nothing is running. A query waiting for a timer, to refresh or for its turn under the fetch limits, does not count as running.</returns>
    public async Task WhenIdleAsync()
    {
        while (true)
        {
            Task[] running;
            lock (_gate)
            {
                running = [.. _inFlight];
            }

            if (running.Length == 0)
            {
                return;
            }

            await Task.WhenAll(running).ConfigureAwait(false);
        }
    }

    internal FormStateSnapshot FormSnapshot()
    {
        lock (_gate)
        {
            return _forms;
        }
    }

    internal ImmutableDictionary<string, OuiValue> StateSnapshot()
    {
        lock (_gate)
        {
            EnsureNotEvaluating();
            return EffectiveStateLocked();
        }
    }

    internal void WriteField(string? formName, string field, FormField value) => Commit(() =>
    {
        if (IsTooDeep(value.Value))
        {
            ReportTooDeepLocked(field);
            return;
        }

        _forms = _forms.With(formName, field, value);
    });

    internal void WriteState(StateKey key, OuiValue value) => Commit(() =>
    {
        if (IsTooDeep(value))
        {
            ReportTooDeepLocked(key.Name);
            return;
        }

        WriteStateLocked([(key, value)]);
    });

    private bool IsTooDeep(OuiValue value) => value.Measure.Depth > _limits.MaxValueDepth;

    private void ReportTooDeepLocked(string name) => _otherErrors.Add(new OpenUIError
    {
        Source = OpenUIErrorSource.Runtime,
        Code = OpenUIErrorCode.LimitExceeded,
        Message = $"The value written to {name} nests deeper than {_limits.MaxValueDepth} levels.",
    });

    internal void ResetState(ImmutableArray<StateKey> keys) => Commit(() => ResetStateLocked(keys));

    // A custom function that calls back into the session arrives here on the evaluating thread, since
    // the lock is reentrant. Throwing turns that into its runtime-error instead of corrupting the pass.
    private void EnsureNotEvaluating()
    {
        if (_evaluating)
        {
            throw new InvalidOperationException("A custom function must not call back into the session (language.md, section 3.6).");
        }
    }

    private void Commit(Action body) => Commit(() =>
    {
        body();
        return true;
    });

    // The one way to change the session. The body returns false when it changed nothing.
    private void Commit(Func<bool> body)
    {
        bool changed;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureNotEvaluating();
            changed = body();
            if (changed)
            {
                _revision++;
                _cached = null;
            }
        }

        RunPendingCancellations();
        if (changed)
        {
            Notify();
        }
    }

    // For tool continuations and timers: after disposal there is nothing to change and nobody to throw to.
    private void CommitIfAlive(Func<bool> body)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
        }

        try
        {
            Commit(body);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    // Every evaluator run goes through here, so re-entry is caught wherever it happens.
    private T Guarded<T>(Func<T> evaluate)
    {
        EnsureNotEvaluating();
        _evaluating = true;
        try
        {
            return evaluate();
        }
        finally
        {
            _evaluating = false;
        }
    }

    private RenderSnapshot EvaluateLocked()
    {
        if (_cached is { } cached)
        {
            return cached;
        }

        if (_last is not null)
        {
            return _last;
        }

        var defaults = DefaultsLocked();
        var snapshot = Guarded(() =>
        {
            var render = OpenUIEvaluator.Evaluate(_document, new EvaluationInputs(_state, QueryDataLocked()), _limits, _options.Collator, defaults);
            _evaluationFuel += render.FuelSpent;
            var errors = new DiagnosticBag(_limits.MaxErrors);
            foreach (var error in render.Errors.Concat(DataErrorsLocked()).Concat(DiagnosticBag.Visible(_otherErrors.Items, true)))
            {
                errors.Add(error);
            }

            return new RenderSnapshot(_revision, _streaming, _document, render, DiagnosticBag.Visible(errors.Items, true), QueryStatusesLocked());
        });
        _cached = snapshot;
        return snapshot;
    }

    private StateDefaultValues DefaultsLocked() =>
        _defaults ??= Guarded(() => OpenUIEvaluator.StateDefaults(_document, _limits, _options.Collator));

    private ImmutableDictionary<string, OuiValue> EffectiveStateLocked() => DefaultsLocked().Values.SetItems(_state);

    private void SetDocumentLocked(OpenUIDocument document)
    {
        _document = document;
        _defaults = null;
    }

    private void BeginStreamLocked()
    {
        _stream = _parser.CreateStream();
        SetDocumentLocked(_stream.Current);
        _text.Clear();
        _streaming = true;
        _otherErrors = new DiagnosticBag(_limits.MaxErrors);
        ClearDataErrorsLocked();
        _emitClear |= _lastEmitted.Count > 0;
        _lastEmitted = OpenUIErrorList.Empty;
    }

    internal int StoredTextLength => _text.Length;

    private void AppendLocked(string chunk)
    {
        var room = _limits.MaxInputChars - _text.Length;
        _text.Append(chunk.AsSpan(0, Math.Min(chunk.Length, Math.Max(room, 0))));
        SetDocumentLocked(_stream.Append(chunk));
    }

    private bool EndStreamLocked()
    {
        if (!_streaming)
        {
            return false;
        }

        SetDocumentLocked(_stream.Complete());
        _streaming = false;
        ApplyDefaultsLocked();
        SyncDataLocked();
        return true;
    }

    private void ReplaceProgramLocked(string text)
    {
        BeginStreamLocked();
        AppendLocked(text);
        EndStreamLocked();
    }

    // Defaults become values only once the stream ends: until then a later definition of the same
    // name can still win (section 4). Parsing again never overwrites a value (section 5.1). A default
    // counts against the state limit like a write, and one that would pass it is dropped to null.
    private void ApplyDefaultsLocked()
    {
        var defaults = DefaultsLocked();
        var dropped = new List<string>();
        foreach (var statement in _document.Statements)
        {
            if (statement.Kind != StatementKind.State || _state.ContainsKey(statement.Name))
            {
                continue;
            }

            var value = defaults.Values.GetValueOrDefault(statement.Name);
            if (_stateChars + StateChars(statement.Name, value) > _limits.MaxTextChars)
            {
                ReportLimitLocked(new LimitException($"The default of {statement.Name} would make the state hold more than {_limits.MaxTextChars} characters."));
                dropped.Add(statement.Name);
                continue;
            }

            SetStateLocked(statement.Name, value);
        }

        if (dropped.Count > 0)
        {
            _defaults = defaults with { Values = defaults.Values.SetItems(dropped.Select(name => KeyValuePair.Create(name, OuiValue.Null))) };
        }
    }

    // A program's writes pay from its action's budget, which a host's writes do not have. Comparing
    // walks no more of the old value than the new one holds, so the new one sets the price.
    private void WriteStateLocked(IEnumerable<(StateKey Key, OuiValue Value)> writes, EvaluationBudget? budget = null)
    {
        var defaults = DefaultsLocked().Values;
        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, value) in writes)
        {
            if (budget is not null)
            {
                ChargeStateWriteLocked(key.Name, value, budget);
            }

            var known = _state.TryGetValue(key.Name, out var old) || defaults.TryGetValue(key.Name, out old);
            if (!known || !old.Equals(value))
            {
                changed.Add(key.Name);
            }

            SetStateLocked(key.Name, value);
        }

        RefetchDependentsLocked(changed);
    }

    private void ChargeStateWriteLocked(string name, OuiValue value, EvaluationBudget budget)
    {
        budget.Spend(StateWriteSteps + budget.ReadCost(name.Length));
        var after = _stateChars + StateChars(name, value) - (_state.TryGetValue(name, out var old) ? StateChars(name, old) : 0);
        if (after > _limits.MaxTextChars)
        {
            throw new LimitException($"The state holds more than {_limits.MaxTextChars} characters.");
        }
    }

    private void SetStateLocked(string name, OuiValue value)
    {
        if (_state.TryGetValue(name, out var old))
        {
            _stateChars -= StateChars(name, old);
        }

        _state = _state.SetItem(name, value);
        _stateChars += StateChars(name, value);
    }

    private static long StateChars(string name, OuiValue value) => name.Length + value.Measure.Chars;

    private void ResetStateLocked(ImmutableArray<StateKey> keys, EvaluationBudget? budget = null)
    {
        var defaults = DefaultsLocked().Values;
        WriteStateLocked(keys.Select(key => (key, defaults.GetValueOrDefault(key.Name))), budget);
    }

    private Evaluator NewEvaluatorLocked(EvaluationBudget? budget = null)
    {
        var state = _state;
        var defaults = DefaultsLocked().Values;
        return new Evaluator(
            _document,
            key => state.TryGetValue(key.Name, out var value) ? value : defaults.GetValueOrDefault(key.Name),
            QueryDataLocked(),
            _limits,
            _options.Collator,
            _otherErrors,
            budget);
    }

    private void Track(Task task)
    {
        lock (_gate)
        {
            _inFlight.Add(task);
        }

        task.ContinueWith(
            finished =>
            {
                lock (_gate)
                {
                    _inFlight.Remove(finished);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    // One thread raises at a time, so two commits cannot deliver their error lists out of order and
    // leave a host on a stale one. A commit made during a delivery is raised by the delivering thread
    // when that delivery ends, and the list it raises is the current one.
    private void Notify()
    {
        lock (_gate)
        {
            _notifyPending = true;
            if (_notifying)
            {
                return;
            }

            _notifying = true;
        }

        try
        {
            while (RaisePendingNotification())
            {
            }
        }
        catch
        {
            lock (_gate)
            {
                _notifying = false;
            }

            throw;
        }
    }

    private bool RaisePendingNotification()
    {
        bool clear;
        long revision;
        ChannelWriter<bool>[] watchers;
        EventHandler<OpenUIErrorList>? errorHandlers;
        EventHandler<SessionInvalidatedEventArgs>? invalidatedHandlers;
        lock (_gate)
        {
            if (!_notifyPending)
            {
                _notifying = false;
                return false;
            }

            _notifyPending = false;
            clear = _emitClear;
            _emitClear = false;
            revision = _revision;
            watchers = [.. _watchers];
            errorHandlers = ErrorsChanged;
            invalidatedHandlers = Invalidated;
        }

        if (clear)
        {
            Raise(errorHandlers, OpenUIErrorList.Empty);
        }

        Raise(invalidatedHandlers, new SessionInvalidatedEventArgs(revision));
        foreach (var watcher in watchers)
        {
            watcher.TryWrite(true);
        }

        EmitErrors();
        return true;
    }

    private void EmitErrors()
    {
        OpenUIErrorList errors;
        EventHandler<OpenUIErrorList>? handlers;
        lock (_gate)
        {
            handlers = ErrorsChanged;
            if (_streaming || _disposed || handlers is null)
            {
                return;
            }

            errors = EvaluateLocked().Errors;
            if (errors.Equals(_lastEmitted))
            {
                return;
            }

            _lastEmitted = errors;
        }

        Raise(handlers, errors);
    }

    // A host handler's exception must not reach the commit that raised the event, which may be a tool
    // continuation with nobody to catch it.
    private void Raise<T>(EventHandler<T>? handlers, T args)
    {
        foreach (var handler in handlers?.GetInvocationList().Cast<EventHandler<T>>() ?? [])
        {
            try
            {
                handler(this, args);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Trace.TraceError("OpenUISession: an event handler threw: {0}", ex);
            }
        }
    }
}
