using System.Collections.Immutable;
using OpenUI.Lang.Library;
using OpenUI.Lang.Semantics;

namespace OpenUI.Lang.Values;

/// <summary>
/// An action value: the steps a component runs when triggered (language.md, section 6.3). Every
/// action call evaluates to an action of one step, and <c>Action([...])</c> holds several.
/// </summary>
public sealed class OuiAction
{
    internal OuiAction(ImmutableArray<ActionStep> steps)
    {
        Steps = steps;
        var measure = new MeasureBuilder();
        foreach (var step in steps)
        {
            step.AddTo(ref measure);
        }

        Measure = measure.ToMeasure();
    }

    /// <summary>The steps, run in order (section 6.3).</summary>
    public ImmutableArray<ActionStep> Steps { get; }

    /// <summary>
    /// An action a renderer can run for a component that has none (language.md, section 6.3): it
    /// sends <c>continue_conversation</c> with this message and the form state of the origin it runs with.
    /// </summary>
    /// <param name="message">The message, such as a button's label.</param>
    public static OuiAction ToAssistant(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        OuiValue text = message;
        return new OuiAction([new ToAssistantStep(text, null, new DeferredExpression(new ConstantNode(text), [], null), null)]);
    }

    /// <summary>What this action costs when expanded: its steps and the values they hold.</summary>
    internal ValueMeasure Measure { get; }

    /// <summary>A short description for debugging (language.md, section 6.3).</summary>
    public override string ToString() => "[object Object]";

    internal bool StructurallyEquals(OuiAction other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (Steps.Length != other.Steps.Length)
        {
            return false;
        }

        for (var i = 0; i < Steps.Length; i++)
        {
            if (!Steps[i].StructurallyEquals(other.Steps[i]))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>The kinds of action step (language.md, section 6.3).</summary>
public enum ActionStepKind
{
    /// <summary><c>@Set($var, value)</c> (language.md, section 6.3).</summary>
    Set,

    /// <summary><c>@Reset($a, $b, ...)</c> (language.md, section 6.3).</summary>
    Reset,

    /// <summary><c>@Run(ref)</c> (language.md, section 6.3).</summary>
    Run,

    /// <summary><c>@ToAssistant(message, context?)</c> (language.md, section 6.3).</summary>
    ToAssistant,

    /// <summary><c>@OpenUrl(url)</c> (language.md, section 6.3).</summary>
    OpenUrl,

    /// <summary>A custom action from the library's <c>actions</c> (prompt.md, section 2.5).</summary>
    Custom,
}

/// <summary>
/// One step of an <see cref="OuiAction"/> (language.md, section 6.3). The hierarchy is closed: every
/// step is one of the sealed types in this file, told apart by <see cref="Kind"/>.
/// </summary>
public abstract class ActionStep
{
    private protected ActionStep()
    {
    }

    /// <summary>Which step this is (language.md, section 6.3).</summary>
    public abstract ActionStepKind Kind { get; }

    // One node for the step, and the values and names it holds.
    internal abstract void AddTo(ref MeasureBuilder measure);

    internal abstract bool StructurallyEquals(ActionStep other);
}

/// <summary>
/// <c>@Set($var, value)</c>: writes a state variable when the action runs (section 6.3). The value is
/// evaluated again when the step runs; <see cref="Value"/> is its value when the tree was evaluated.
/// </summary>
public sealed class SetStep : ActionStep
{
    internal SetStep(StateKey target, OuiValue value, DeferredExpression deferred)
    {
        Target = target;
        Value = value;
        Deferred = deferred;
    }

    /// <inheritdoc/>
    public override ActionStepKind Kind => ActionStepKind.Set;

    /// <summary>The state variable written (section 6.3).</summary>
    public StateKey Target { get; }

    /// <summary>The value as of the evaluated tree; the step evaluates it again when it runs (section 6.3).</summary>
    public OuiValue Value { get; }

    internal DeferredExpression Deferred { get; }

    internal override void AddTo(ref MeasureBuilder measure)
    {
        measure.AddText(Target.Name);
        measure.AddText(Deferred.StatementId);
        measure.Add(Value);
    }

    internal override bool StructurallyEquals(ActionStep other) =>
        other is SetStep set && set.Target == Target && set.Value.Equals(Value);
}

/// <summary><c>@Reset($a, $b, ...)</c>: restores the declared defaults, null if none (section 6.3).</summary>
public sealed class ResetStep : ActionStep
{
    internal ResetStep(ImmutableArray<StateKey> targets) => Targets = targets;

    /// <inheritdoc/>
    public override ActionStepKind Kind => ActionStepKind.Reset;

    /// <summary>The state variables restored, in order (section 6.3).</summary>
    public ImmutableArray<StateKey> Targets { get; }

    internal override void AddTo(ref MeasureBuilder measure)
    {
        foreach (var target in Targets)
        {
            measure.AddText(target.Name);
        }

        measure.Add(OuiValue.Null);
    }

    internal override bool StructurallyEquals(ActionStep other) =>
        other is ResetStep reset && reset.Targets.SequenceEqual(Targets);
}

/// <summary>
/// <c>@Run(ref)</c>: runs a mutation, awaiting it, or fetches a query again (sections 6.1 to 6.3).
/// A failed mutation stops the remaining steps.
/// </summary>
public sealed class RunStep : ActionStep
{
    internal RunStep(string statementId) => StatementId = statementId;

    /// <inheritdoc/>
    public override ActionStepKind Kind => ActionStepKind.Run;

    /// <summary>The query or mutation statement named by the step (sections 6.1 to 6.3).</summary>
    public string StatementId { get; }

    internal override void AddTo(ref MeasureBuilder measure)
    {
        measure.AddText(StatementId);
        measure.Add(OuiValue.Null);
    }

    internal override bool StructurallyEquals(ActionStep other) =>
        other is RunStep run && string.Equals(run.StatementId, StatementId, StringComparison.Ordinal);
}

/// <summary>
/// <c>@ToAssistant(message, context?)</c>: sends a <c>continue_conversation</c> host event (section 6.3).
/// Its arguments are evaluated when the step runs; the properties hold their values as of the tree.
/// </summary>
public sealed class ToAssistantStep : ActionStep
{
    internal ToAssistantStep(OuiValue message, OuiValue? context, DeferredExpression deferredMessage, DeferredExpression? deferredContext)
    {
        Message = message;
        Context = context;
        DeferredMessage = deferredMessage;
        DeferredContext = deferredContext;
    }

    /// <inheritdoc/>
    public override ActionStepKind Kind => ActionStepKind.ToAssistant;

    /// <summary>The message as of the evaluated tree (section 6.3).</summary>
    public OuiValue Message { get; }

    /// <summary>The context as of the evaluated tree, or null when the call had no second argument (section 6.3).</summary>
    public OuiValue? Context { get; }

    internal DeferredExpression DeferredMessage { get; }

    internal DeferredExpression? DeferredContext { get; }

    internal override void AddTo(ref MeasureBuilder measure)
    {
        measure.AddText(DeferredMessage.StatementId);
        measure.Add(Message);
        measure.Add(Context ?? OuiValue.Null);
    }

    internal override bool StructurallyEquals(ActionStep other) =>
        other is ToAssistantStep say && say.Message.Equals(Message) && Nullable.Equals(say.Context, Context);
}

/// <summary><c>@OpenUrl(url)</c>: sends an <c>open_url</c> host event (section 6.3).</summary>
public sealed class OpenUrlStep : ActionStep
{
    internal OpenUrlStep(OuiValue url, DeferredExpression deferredUrl)
    {
        Url = url;
        DeferredUrl = deferredUrl;
    }

    /// <inheritdoc/>
    public override ActionStepKind Kind => ActionStepKind.OpenUrl;

    /// <summary>The URL as of the evaluated tree (section 6.3).</summary>
    public OuiValue Url { get; }

    internal DeferredExpression DeferredUrl { get; }

    internal override void AddTo(ref MeasureBuilder measure)
    {
        measure.AddText(DeferredUrl.StatementId);
        measure.Add(Url);
    }

    internal override bool StructurallyEquals(ActionStep other) => other is OpenUrlStep open && open.Url.Equals(Url);
}

/// <summary>
/// A custom action from the library (section 6.3, prompt.md section 2.5). Its arguments were evaluated
/// and checked when the action was built; an invalid argument makes the step a no-op.
/// </summary>
public sealed class CustomActionStep : ActionStep
{
    internal CustomActionStep(ActionSpec action, OuiObject parameters, bool isNoOp)
    {
        Action = action;
        Parameters = parameters;
        IsNoOp = isNoOp;
    }

    /// <inheritdoc/>
    public override ActionStepKind Kind => ActionStepKind.Custom;

    /// <summary>The library's declaration of the action (section 6.3, prompt.md section 2.5).</summary>
    public ActionSpec Action { get; }

    /// <summary>The name, which is also the host event's <c>type</c> (section 6.3, prompt.md section 2.5).</summary>
    public string Name => Action.Name;

    /// <summary>The arguments keyed by the action's param names, in param order; null for a missing optional argument (section 6.3, prompt.md section 2.5).</summary>
    public OuiObject Parameters { get; }

    /// <summary>True when an argument was invalid, or a required one null: the step does nothing (section 6.3).</summary>
    public bool IsNoOp { get; }

    internal override void AddTo(ref MeasureBuilder measure)
    {
        measure.AddText(Name);
        measure.Add(Parameters);
    }

    internal override bool StructurallyEquals(ActionStep other) =>
        other is CustomActionStep custom && custom.Name == Name && custom.IsNoOp == IsNoOp
        && ((OuiValue)custom.Parameters).Equals(Parameters);
}
