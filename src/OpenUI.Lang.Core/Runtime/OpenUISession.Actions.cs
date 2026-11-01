using System.Diagnostics;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Semantics;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Runtime;

/// <summary>The action pipeline (language.md, section 6.3).</summary>
public sealed partial class OpenUISession
{
    private const int SortedCopySteps = 8;

    /// <summary>
    /// Runs an action's steps in order (language.md, section 6.3). <c>@Set</c> and event arguments are
    /// evaluated when their step runs. A mutation is awaited, and its failure stops the rest; query
    /// fetches and host events are not awaited. An action run from inside a form checks that form first
    /// and does nothing if a field fails (section 5.4).
    /// </summary>
    public async ValueTask<ActionOutcome> RunActionAsync(OuiAction action, ActionOrigin origin, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        var stepsRun = 0;
        var budget = new EvaluationBudget(_limits);
        budget.CountEvaluationsFrom(EvaluationFuel());
        if (origin.FormName is { } form && CheckForm(form, budget) is { IsValid: false } invalid)
        {
            return new ActionOutcome(ActionOutcomeKind.Invalid, 0, null, invalid);
        }

        foreach (var step in action.Steps)
        {
            // The caller's thread, often a UI's, gets a turn between steps.
            if (stepsRun > 0)
            {
                await Task.Yield();
            }

            ct.ThrowIfCancellationRequested();
            if (ChargeStep(budget) is { } outOfBudget)
            {
                return new ActionOutcome(ActionOutcomeKind.Stopped, stepsRun, outOfBudget);
            }

            if (step is RunStep run)
            {
                if (await RunStepAsync(run, stepsRun, budget, ct).ConfigureAwait(false) is { } stopped)
                {
                    return stopped;
                }
            }
            else if (step is CustomActionStep { IsNoOp: true })
            {
                continue;
            }
            else if (RunSynchronousStep(step, origin, budget, ct) is { } overLimit)
            {
                return new ActionOutcome(ActionOutcomeKind.Stopped, stepsRun, overLimit);
            }

            stepsRun++;
        }

        return new ActionOutcome(stepsRun == 0 ? ActionOutcomeKind.NoOp : ActionOutcomeKind.Completed, stepsRun, null);
    }

    private long EvaluationFuel()
    {
        lock (_gate)
        {
            return _evaluationFuel;
        }
    }

    // One click pays for its steps out of one budget. A step costs what its commit can walk, the
    // statements of the program and the names in them, plus every evaluation it caused, whoever asked
    // for it: a host that renders at each commit makes each step cost a pass.
    private OpenUIError? ChargeStep(EvaluationBudget budget) => Charge(() =>
    {
        budget.Spend(1 + _document.Statements.Length + budget.ReadCost(_text.Length));
        budget.PayEvaluations(_evaluationFuel);
    });

    private OpenUIError? Charge(Action charge)
    {
        OpenUIError? error = null;
        Commit(() =>
        {
            try
            {
                charge();
                return false;
            }
            catch (LimitException limit)
            {
                error = ReportLimitLocked(limit);
                return true;
            }
        });

        return error;
    }

    private OpenUIError ReportLimitLocked(LimitException limit)
    {
        var error = new OpenUIError { Source = OpenUIErrorSource.Runtime, Code = OpenUIErrorCode.LimitExceeded, Message = limit.Message };
        _otherErrors.Add(error);
        return error;
    }

    private async Task<ActionOutcome?> RunStepAsync(RunStep run, int stepsRun, EvaluationBudget budget, CancellationToken ct)
    {
        Statement? mutation = null;
        Commit(() =>
        {
            if (!_document.TryGetStatement(run.StatementId, out var statement))
            {
                return false;
            }

            if (statement.Kind == StatementKind.Mutation)
            {
                mutation = statement;
                return false;
            }

            if (statement.Kind != StatementKind.Query || _streaming)
            {
                return false;
            }

            StartFetchLocked(statement, force: true, NewEvaluatorLocked(budget));
            return true;
        });

        if (mutation is null)
        {
            return null;
        }

        return Charge(budget.CountMutation) is { } overLimit
            ? new ActionOutcome(ActionOutcomeKind.Stopped, stepsRun, overLimit)
            : await RunMutationAsync(mutation, stepsRun, budget, ct).ConfigureAwait(false);
    }

    // A limit ends the action: a later step would run on state this one failed to change.
    private OpenUIError? RunSynchronousStep(ActionStep step, ActionOrigin origin, EvaluationBudget budget, CancellationToken ct)
    {
        OpenUIHostEvent? hostEvent = null;
        OpenUIError? overLimit = null;
        var lifetime = CancellationToken.None;
        Commit(() =>
        {
            lifetime = _lifetime.Token;
            try
            {
                hostEvent = RunStepLocked(step, origin, budget);
            }
            catch (LimitException limit)
            {
                overLimit = ReportLimitLocked(limit);
            }
            catch (StepStoppedException stopped)
            {
                overLimit = stopped.Error;
            }
        });

        if (hostEvent is not null && _options.Events is { } events)
        {
            // Started in order on this thread, then not awaited (section 6.3).
            Track(DispatchAsync(events, hostEvent, lifetime, ct));
        }

        return overLimit;
    }

    private OpenUIHostEvent? RunStepLocked(ActionStep step, ActionOrigin origin, EvaluationBudget budget)
    {
        switch (step)
        {
            case SetStep set:
                var evaluator = NewEvaluatorLocked(budget);
                OuiValue value = default;
                Evaluated(Guarded(() => evaluator.TryEvaluateDeferred(set.Deferred, out value)));
                WriteStateLocked([(set.Target, value)], budget);
                return null;
            case ResetStep reset:
                ResetStateLocked(reset.Targets, budget);
                return null;
            case ToAssistantStep say:
                return ToAssistantEventLocked(say, origin, budget);
            case OpenUrlStep open:
                return OpenUrlEventLocked(open, origin, budget);
            case CustomActionStep custom:
                return NewEventLocked(custom.Name, "", custom.Parameters, origin, budget);
            default:
                throw new InvalidOperationException($"Unhandled action step {step.Kind}.");
        }
    }

    // The evaluator only fails on a limit, and reported it as the last error: that ends the action.
    private void Evaluated(bool succeeded)
    {
        if (!succeeded)
        {
            throw new StepStoppedException(_otherErrors.LastAdded!);
        }
    }

    private sealed class StepStoppedException(OpenUIError error) : Exception
    {
        public OpenUIError Error { get; } = error;
    }

    private OpenUIHostEvent? ToAssistantEventLocked(ToAssistantStep say, ActionOrigin origin, EvaluationBudget budget)
    {
        var evaluator = NewEvaluatorLocked(budget);
        OuiValue message = default;
        OuiValue context = default;
        var text = "";
        Evaluated(Guarded(() => evaluator.TryEvaluateDeferred(say.DeferredMessage, out message)
            && (say.DeferredContext is not { } deferred || evaluator.TryEvaluateDeferred(deferred, out context))
            && (message.IsNull || evaluator.TryStringify(message, out text))));

        var parameters = new OuiObject.Builder();
        if (say.DeferredContext is not null)
        {
            parameters.Set("context", context);
        }

        return NewEventLocked("continue_conversation", text, parameters.ToObject(), origin, budget);
    }

    private OpenUIHostEvent? OpenUrlEventLocked(OpenUrlStep open, ActionOrigin origin, EvaluationBudget budget)
    {
        var evaluator = NewEvaluatorLocked(budget);
        OuiValue url = default;
        Evaluated(Guarded(() => evaluator.TryEvaluateDeferred(open.DeferredUrl, out url)));

        if (url.TryGetString(out var text) && HasAllowedScheme(text))
        {
            return NewEventLocked("open_url", "", new OuiObject.Builder().Set("url", url).ToObject(), origin, budget);
        }

        _otherErrors.Add(new OpenUIError
        {
            Source = OpenUIErrorSource.Runtime,
            Code = OpenUIErrorCode.RuntimeError,
            Message = "@OpenUrl needs an absolute URL with an allowed scheme.",
            StatementId = origin.Component?.StatementId,
            Component = "@OpenUrl",
        });
        return null;
    }

    // Web addresses need a host: browsers read "https:evil.example" as one. A host may allow others
    // with no authority at all, such as mailto.
    private bool HasAllowedScheme(string url) =>
        UrlText.Scheme(url) is { } scheme
        && _options.AllowedUrlSchemes.Contains(scheme, StringComparer.OrdinalIgnoreCase)
        && (UrlText.IsAbsolute(url) || !(scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || scheme.Equals("https", StringComparison.OrdinalIgnoreCase)));

    private static async Task DispatchAsync(IOpenUIHostEventHandler events, OpenUIHostEvent hostEvent, CancellationToken lifetime, CancellationToken ct)
    {
        try
        {
            await events.OnEventAsync(hostEvent, ct).AsTask().WaitAsync(lifetime).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Trace.TraceError("OpenUISession: a host event handler threw: {0}", ex);
        }
    }

    // The form state, the parameters and the message share their strings with the session, so only the
    // click's budget stops one event, or a thousand of them, from handing a host more text than the state holds.
    private OpenUIHostEvent? NewEventLocked(string type, string message, OuiObject parameters, ActionOrigin origin, EvaluationBudget budget)
    {
        if (_options.Events is null)
        {
            return null;
        }

        var formState = FormStateLocked(origin.FormName, budget);
        budget.ChargeText(message.Length);
        budget.ChargeJson(formState);
        budget.ChargeJson(parameters);
        return new(type, message, parameters, origin.FormName, formState);
    }

    // Inside a form: that form's fields. Outside: the page's $variables as plain values, and the
    // page-level fields (section 6.3).
    private OuiObject FormStateLocked(string? formName, EvaluationBudget budget)
    {
        var result = new OuiObject.Builder();
        if (formName is not null)
        {
            var fields = _forms.Fields(formName);
            ChargeSortedCopy(fields.Keys, budget);
            return result.Set(formName, FieldsObject(fields)).ToObject();
        }

        ChargeSortedCopy(_state.Keys.Concat(_forms.Page.Keys).Concat(DefaultsLocked().Values.Keys), budget);
        var state = EffectiveStateLocked();
        foreach (var (key, value) in state.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            result.Set(key, value);
        }

        foreach (var (key, field) in _forms.Page.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            result.Set(key, FieldObject(field));
        }

        return result.ToObject();
    }

    // An event copies each key, sorted, into a new object: several steps a key, and its characters.
    private static void ChargeSortedCopy(IEnumerable<string> keys, EvaluationBudget budget)
    {
        foreach (var key in keys)
        {
            budget.Spend(SortedCopySteps + budget.ReadCost(key.Length));
        }
    }

    private static OuiObject FieldsObject(IEnumerable<KeyValuePair<string, FormField>> fields)
    {
        var result = new OuiObject.Builder();
        foreach (var (key, field) in fields.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            result.Set(key, FieldObject(field));
        }

        return result.ToObject();
    }

    private static OuiObject FieldObject(FormField field) =>
        new OuiObject.Builder().Set("value", field.Value).Set("componentType", field.ComponentType).ToObject();
}
