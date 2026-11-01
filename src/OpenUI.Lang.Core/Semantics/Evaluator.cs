using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Semantics;

/// <summary>Why an evaluation produced no value (language.md, sections 2.3 and 8.4).</summary>
internal enum Absence : byte
{
    Present,

    /// <summary>A reference to a missing statement, or one that closes a cycle.</summary>
    Unresolved,

    /// <summary>An invalid or unknown component, or <c>Query</c>/<c>Mutation</c>, in a value position.</summary>
    Dropped,
}

/// <summary>A value, or nothing. Nothing is null in an expression and is left out of arrays.</summary>
internal readonly record struct Result(OuiValue Value, Absence Absence)
{
    public static readonly Result Unresolved = new(OuiValue.Null, Absence.Unresolved);
    public static readonly Result Dropped = new(OuiValue.Null, Absence.Dropped);

    public bool IsAbsent => Absence != Absence.Present;

    public static Result Of(OuiValue value) => new(value, Absence.Present);
}

/// <summary>
/// Evaluates a document against state and query data (language.md, sections 3, 5 and 6). Each
/// statement is evaluated at most once per pass (memoized), which keeps reference fan-out linear;
/// a value computed while a cycle was cut above it is not memoized. Evaluation is bounded by depth,
/// fuel, string length and value size (<see cref="OpenUILimits"/>).
/// </summary>
internal sealed class Evaluator
{
    private readonly OpenUIDocument _document;
    private readonly Func<StateKey, OuiValue> _state;
    private readonly IReadOnlyDictionary<string, OuiValue>? _queryData;
    private readonly OpenUILimits _limits;
    private readonly IOuiCollator _collator;
    private readonly DiagnosticBag _bag;
    private readonly Dictionary<string, Result> _memo = new(StringComparer.Ordinal);
    private readonly HashSet<string> _droppedByLimit = new(StringComparer.Ordinal);
    private readonly List<Frame> _frames = [];
    private readonly StringBuilder _outsideStatements = new();
    private readonly EvaluationBudget _budget;
    private List<OuiValue> _loop = [];
    private int _depth;
    private bool _hitLimit;

    public Evaluator(
        OpenUIDocument document,
        Func<StateKey, OuiValue> state,
        IReadOnlyDictionary<string, OuiValue>? queryData,
        OpenUILimits limits,
        IOuiCollator collator,
        DiagnosticBag bag,
        EvaluationBudget? budget = null)
    {
        _document = document;
        _state = state;
        _queryData = queryData;
        _limits = limits;
        _collator = collator;
        _bag = bag;
        _budget = budget ?? new EvaluationBudget(limits);
    }

    private string? StatementId => _frames.Count > 0 ? _frames[^1].Name : null;

    /// <summary>The steps this evaluator has spent.</summary>
    public long FuelSpent => _budget.FuelSpent;

    private DiagnosticTiming Timing => _document.IsFinal ? DiagnosticTiming.Immediate : DiagnosticTiming.StreamEnd;

    /// <summary>Evaluates the statement <paramref name="name"/>, as a reference to it would.</summary>
    public Result EvaluateStatement(string name)
    {
        if (!_document.TryGetStatement(name, out var statement))
        {
            return Result.Unresolved;
        }

        var onStack = _frames.FindIndex(f => f.Name == name);
        if (onStack >= 0)
        {
            // This reference closes a cycle: it is unresolved here, and the frames above it saw a
            // cut value, so their results are not reused elsewhere (section 2.3).
            for (var i = onStack + 1; i < _frames.Count; i++)
            {
                _frames[i].Tainted = true;
            }

            return Result.Unresolved;
        }

        if (_memo.TryGetValue(name, out var cached))
        {
            _hitLimit |= _droppedByLimit.Contains(name);
            return cached;
        }

        var hitBefore = _hitLimit;
        _hitLimit = false;

        // A statement has its own loop scope: a reference made inside a loop must not shift the slots
        // of the loops the statement holds, whose numbers start from its own outermost loop.
        var outerLoops = _loop;
        _loop = [];
        var frame = new Frame(name);
        _frames.Add(frame);
        Result result;
        try
        {
            result = statement.Kind switch
            {
                StatementKind.Query => _queryData?.TryGetValue(name, out var data) == true ? Result.Of(data) : Present(Evaluate(statement.Bound.Value)),
                StatementKind.Mutation => _queryData?.TryGetValue(name, out var status) == true ? Result.Of(status) : Result.Of(IdleMutation),
                _ => Evaluate(statement.Bound.Value),
            };
        }
        catch (LimitException limit)
        {
            Report(OpenUIErrorCode.LimitExceeded, OpenUIErrorSource.Runtime, limit.Message, timing: DiagnosticTiming.Immediate);
            result = Result.Dropped;
            _hitLimit = true;
        }
        finally
        {
            _frames.RemoveAt(_frames.Count - 1);
            _loop = outerLoops;
        }

        // A reference to a dropped statement reads as null when drawing (section 8.1), but anything about
        // to act on the value has to know, however many statements away the limit was hit.
        if (_hitLimit)
        {
            _droppedByLimit.Add(name);
        }

        _hitLimit |= hitBefore;
        if (!frame.Tainted)
        {
            _memo[name] = result;
        }

        return result;
    }

    /// <summary>Evaluates an action's deferred expression now, inside the statement and loop scope it captured. False when a limit stopped it, which was reported.</summary>
    public bool TryEvaluateDeferred(DeferredExpression deferred, out OuiValue value)
    {
        _loop.Clear();
        _loop.AddRange(deferred.Loop);
        if (deferred.StatementId is not null)
        {
            _frames.Add(new Frame(deferred.StatementId));
        }

        _hitLimit = false;
        try
        {
            value = Evaluate(deferred.Node).Value;
            return !_hitLimit;
        }
        catch (LimitException limit)
        {
            Report(OpenUIErrorCode.LimitExceeded, OpenUIErrorSource.Runtime, limit.Message, timing: DiagnosticTiming.Immediate);
            value = OuiValue.Null;
            return false;
        }
        finally
        {
            _frames.Clear();
            _loop.Clear();
        }
    }

    /// <summary>Evaluates a bound expression outside any statement (a state default being one).</summary>
    public OuiValue EvaluateIn(string statementId, Bound node)
    {
        TryEvaluateIn(statementId, node, out var value);
        return value;
    }

    /// <summary>Evaluates a bound expression outside any statement. False when a limit stopped it, which was reported.</summary>
    public bool TryEvaluateIn(string statementId, Bound node, out OuiValue value)
    {
        _frames.Add(new Frame(statementId));
        _hitLimit = false;
        try
        {
            value = Evaluate(node).Value;
            return !_hitLimit;
        }
        catch (LimitException limit)
        {
            Report(OpenUIErrorCode.LimitExceeded, OpenUIErrorSource.Runtime, limit.Message, timing: DiagnosticTiming.Immediate);
            value = OuiValue.Null;
            return false;
        }
        finally
        {
            _frames.RemoveAt(_frames.Count - 1);
        }
    }

    /// <summary>The text of a value for a host event, paid for from this pass. False when a limit stopped it, which was reported.</summary>
    public bool TryStringify(OuiValue value, out string text)
    {
        try
        {
            text = _budget.Stringify(value);
            return true;
        }
        catch (LimitException limit)
        {
            Report(OpenUIErrorCode.LimitExceeded, OpenUIErrorSource.Runtime, limit.Message, timing: DiagnosticTiming.Immediate);
            text = "";
            return false;
        }
    }

    /// <summary>Pays for writing a value out as a tool's arguments. False when a limit stopped it, which was reported.</summary>
    public bool TryChargeJson(OuiValue value)
    {
        try
        {
            _budget.ChargeJson(value);
            return true;
        }
        catch (LimitException limit)
        {
            Report(OpenUIErrorCode.LimitExceeded, OpenUIErrorSource.Runtime, limit.Message, timing: DiagnosticTiming.Immediate);
            return false;
        }
    }

    private static OuiObject IdleMutation { get; } = OuiObject.Create(
    [
        KeyValuePair.Create("status", (OuiValue)"idle"),
        KeyValuePair.Create("data", OuiValue.Null),
        KeyValuePair.Create("error", OuiValue.Null),
    ]);

    private static Result Present(Result result) => result.IsAbsent ? Result.Of(OuiValue.Null) : result;

    private Result Evaluate(Bound node)
    {
        _budget.Spend();
        if (++_depth > _limits.MaxEvalDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            _depth--;
            throw new LimitException($"Evaluation nests deeper than {_limits.MaxEvalDepth} levels.");
        }

        try
        {
            return EvaluateNode(node);
        }
        finally
        {
            _depth--;
        }
    }

    private Result EvaluateNode(Bound node)
    {
        switch (node)
        {
            case ConstantNode constant:
                return Result.Of(Checked(constant.Value));
            case ArrayNode array:
                return Result.Of(Checked(OuiArray.Create(Collect(array.Items))));
            case ObjectNode obj:
                var builder = new OuiObject.Builder();
                // By position: a key can be as long as the program and would be copied into each id below it.
                for (var i = 0; i < obj.Entries.Length; i++)
                {
                    var (key, value) = obj.Entries[i];
                    _budget.Read(key);
                    builder.Set(key, EvaluateAt(i, value).Value);
                }

                return Result.Of(Checked(builder.ToObject()));
            case ComponentNode component:
                return EvaluateComponent(component);
            case UnknownComponentNode unknown:
                return unknown.InValuePosition
                    ? Result.Dropped
                    : Result.Of(new OuiComponent(unknown.Name, null, StatementId, NextNodeId(), [], isUnknown: true, null));
            case InlineReservedNode reserved:
                return reserved.InValuePosition ? Result.Dropped : Result.Of(OuiValue.Null);
            case StatementRefNode reference:
                // Looked up by name in the document, the memo and each open frame.
                _budget.Spend(_budget.ReadCost(reference.Name.Length) * (2 + _frames.Count));
                return EvaluateStatement(reference.Name);
            case StateRefNode state:
                _budget.Read(state.Key.Name);
                return Result.Of(Checked(_state(state.Key)));
            case LoopVariableNode loop:
                return Result.Of(loop.Slot < _loop.Count ? _loop[loop.Slot] : OuiValue.Null);
            case MemberNode member:
                return Result.Of(Member(Evaluate(member.Target).Value, member.Field));
            case IndexNode index:
                return Result.Of(Index(Evaluate(index.Target).Value, Evaluate(index.Index).Value));
            case UnaryNode unary:
                var operand = Evaluate(unary.Operand).Value;
                return Result.Of(unary.Operator == Syntax.UnaryOperator.Not
                    ? !Ecma.ToBoolean(operand)
                    : -_budget.ToNumber(operand));
            case BinaryNode binary:
                return Result.Of(EvaluateBinary(binary));
            case TernaryNode ternary:
                return Ecma.ToBoolean(Evaluate(ternary.Condition).Value) ? Evaluate(ternary.WhenTrue) : Evaluate(ternary.WhenFalse);
            case BuiltinNode builtin:
                return Result.Of(Checked(EvaluateBuiltin(builtin)));
            case EachNode each:
                return Result.Of(Checked(EvaluateEach(each)));
            case FunctionNode function:
                return Result.Of(EvaluateFunction(function));
            case SetNode set:
                return Result.Of(Checked(set.Target is { } target
                    ? Action(new SetStep(target, Evaluate(set.Value).Value, Defer(set.Value)))
                    : Action()));
            case ResetNode reset:
                return Result.Of(Action(new ResetStep(reset.Targets)));
            case RunNode run:
                return Result.Of(run.Target is { } runTarget ? Action(new RunStep(runTarget)) : Action());
            case ToAssistantNode say:
                var context = say.Context is { } contextNode ? Evaluate(contextNode).Value : OuiValue.Null;
                return Result.Of(Checked(Action(new ToAssistantStep(
                    Evaluate(say.Message).Value,
                    say.Context is null ? (OuiValue?)null : context,
                    Defer(say.Message),
                    say.Context is null ? null : Defer(say.Context)))));
            case OpenUrlNode open:
                return Result.Of(Checked(Action(new OpenUrlStep(Evaluate(open.Url).Value, Defer(open.Url)))));
            case CustomActionNode custom:
                return Result.Of(Checked(EvaluateCustomAction(custom)));
            case ActionGroupNode group:
                return Result.Of(Checked(EvaluateActionGroup(group)));
            default:
                throw new InvalidOperationException($"Unhandled bound node {node.GetType().Name}.");
        }
    }

    // A sort wraps whatever its comparer throws in InvalidOperationException, so that is unwrapped:
    // out of fuel stays a limit, and a collator that cannot run leaves the array unsorted (section 3.4).
    private OuiValue EvaluateBuiltin(BuiltinNode builtin)
    {
        var args = builtin.Arguments.Select(a => Evaluate(a).Value).ToArray();
        try
        {
            return Builtins.Call(builtin.Name, args, _collator, _budget);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var cause = ex is InvalidOperationException { InnerException: { } inner } ? inner : ex;
            if (cause is LimitException limit)
            {
                throw limit;
            }

            if (cause is NotSupportedException)
            {
                Report(OpenUIErrorCode.CollationDegraded, OpenUIErrorSource.Runtime, cause.Message, "@" + builtin.Name);
                return args[0];
            }

            Trace.TraceError("OpenUI: @{0} threw: {1}", builtin.Name, cause);
            ReportRuntime($"@{builtin.Name} failed.", "@" + builtin.Name, Timing);
            return OuiValue.Null;
        }
    }

    // Array elements that are absent or unknown components are left out (sections 2.3 and 8.4).
    private ImmutableArray<OuiValue> Collect(ImmutableArray<Bound> items)
    {
        var values = ImmutableArray.CreateBuilder<OuiValue>(items.Length);
        for (var i = 0; i < items.Length; i++)
        {
            var result = EvaluateAt(i, items[i]);
            if (!result.IsAbsent && !(result.Value.TryGetComponent(out var c) && c.IsUnknown))
            {
                values.Add(result.Value);
            }
        }

        return values.ToImmutable();
    }

    private OuiValue EvaluateBinary(BinaryNode binary)
    {
        var left = Evaluate(binary.Left).Value;
        switch (binary.Operator)
        {
            case Syntax.BinaryOperator.And:
                return Ecma.ToBoolean(left) ? Evaluate(binary.Right).Value : left;
            case Syntax.BinaryOperator.Or:
                return Ecma.ToBoolean(left) ? left : Evaluate(binary.Right).Value;
        }

        var right = Evaluate(binary.Right).Value;
        switch (binary.Operator)
        {
            case Syntax.BinaryOperator.Add:
                return _budget.Add(left, right);
            case Syntax.BinaryOperator.Equal:
                return _budget.LooselyEquals(left, right);
            case Syntax.BinaryOperator.NotEqual:
                return !_budget.LooselyEquals(left, right);
        }

        var (a, b) = (_budget.ToNumber(left), _budget.ToNumber(right));
        return binary.Operator switch
        {
            Syntax.BinaryOperator.Subtract => a - b,
            Syntax.BinaryOperator.Multiply => a * b,
            Syntax.BinaryOperator.Divide => b == 0 ? 0 : a / b,
            Syntax.BinaryOperator.Remainder => b == 0 ? 0 : a % b,
            Syntax.BinaryOperator.Greater => a > b,
            Syntax.BinaryOperator.Less => a < b,
            Syntax.BinaryOperator.GreaterOrEqual => a >= b,
            Syntax.BinaryOperator.LessOrEqual => a <= b,
            _ => throw new InvalidOperationException($"Unhandled operator {binary.Operator}."),
        };
    }

    // a.b on an object reads the field; on an array it plucks, and .length counts (section 3.3).
    private OuiValue Member(OuiValue target, string field)
    {
        if (target.TryGetObject(out var obj))
        {
            _budget.Read(field);
            return obj[field];
        }

        if (!target.TryGetArray(out var array))
        {
            return OuiValue.Null;
        }

        if (field == "length")
        {
            return array.Count;
        }

        _budget.Spend(array.Count * (1 + _budget.ReadCost(field.Length)));
        var plucked = ImmutableArray.CreateBuilder<OuiValue>(array.Count);
        foreach (var item in array)
        {
            plucked.Add(item.TryGetObject(out var element) ? element[field] : ItemAt(item, field));
        }

        return Checked(OuiArray.Create(plucked.MoveToImmutable()));
    }

    private OuiValue Index(OuiValue target, OuiValue index)
    {
        if (index.IsNull)
        {
            return OuiValue.Null;
        }

        if (target.TryGetArray(out _))
        {
            return index.TryGetNumber(out var n) ? ItemAt(target, n) : index.TryGetString(out var s) ? ItemAt(target, s) : OuiValue.Null;
        }

        return target.TryGetObject(out var obj) ? obj[_budget.Stringify(index)] : OuiValue.Null;
    }

    private static OuiValue ItemAt(OuiValue target, string key) =>
        OuiObject.IsArrayIndex(key, out var i) ? ItemAt(target, i) : OuiValue.Null;

    private static OuiValue ItemAt(OuiValue target, double index) =>
        target.TryGetArray(out var array) && index >= 0 && index < array.Count && index == Math.Floor(index) ? array[(int)index] : OuiValue.Null;

    private OuiValue EvaluateEach(EachNode each)
    {
        if (!Evaluate(each.Items).Value.TryGetArray(out var items))
        {
            return OuiArray.Create(ImmutableArray<OuiValue>.Empty);
        }

        // Checked as it grows: the iterations cost little each, and what they keep could be huge.
        var measure = new MeasureBuilder();
        var results = ImmutableArray.CreateBuilder<OuiValue>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            _loop.Add(items[i]);
            try
            {
                var result = EvaluateAt(i, each.Template);
                if (!result.IsAbsent && !(result.Value.TryGetComponent(out var c) && c.IsUnknown))
                {
                    results.Add(result.Value);
                    measure.Add(result.Value);
                    Admit(measure.ToMeasure());
                }
            }
            finally
            {
                _loop.RemoveAt(_loop.Count - 1);
            }
        }

        return OuiArray.Create(results.ToImmutable());
    }

    // Arguments map to props by position, each checked with the §8.2 recovery. Literal arguments
    // were reported when binding; only runtime values are reported here.
    private Result EvaluateComponent(ComponentNode node)
    {
        var spec = node.Spec;
        var props = ImmutableArray.CreateBuilder<OuiProp>();
        var issues = new List<(Issue Issue, bool OnlyUnresolved)>();
        var drop = false;
        for (var i = 0; i < spec.Props.Length; i++)
        {
            var prop = spec.Props[i];
            var argument = node.Arguments[i];
            if (argument?.BindingName is { } bindingName)
            {
                _budget.Read(bindingName);
                var key = new StateKey(bindingName);
                props.Add(new OuiProp(prop.Name, _state(key), key));
                continue;
            }

            var result = argument is null ? Result.Dropped : EvaluateAt(prop.Name, argument.Value);
            _budget.Spend(PropValidator.Cost(prop.Type, result.Value));
            var found = new List<Issue>();
            var check = PropValidator.Check(prop, result.IsAbsent ? (OuiValue?)null : result.Value, found);
            if (argument is { IsLiteral: false })
            {
                issues.AddRange(found.Select(issue => (issue, result.Absence == Absence.Unresolved)));
            }

            switch (check.Verdict)
            {
                case ArgumentVerdict.Use:
                    props.Add(new OuiProp(prop.Name, check.Value, null));
                    break;
                case ArgumentVerdict.Reject:
                    drop = true;
                    break;
            }
        }

        var hint = $"Signature: {spec.Hint}. * marks required";
        foreach (var (issue, onlyUnresolved) in issues)
        {
            ChargeReport();
            var timing = issue.IsEnum || onlyUnresolved ? DiagnosticTiming.StreamEnd : Timing;
            Report(issue.Code, OpenUIErrorSource.Parser, Messages.For(issue), spec.Name, issue.Path, hint, timing);
        }

        if (drop)
        {
            return Result.Dropped;
        }

        var formName = spec.Traits.FormNameProp is { } formProp && props.FirstOrDefault(p => p.Name == formProp).Value.TryGetString(out var name)
            ? name
            : null;
        return Result.Of(Checked(new OuiComponent(spec.Name, spec, StatementId, NextNodeId(), props.ToImmutable(), isUnknown: false, formName)));
    }

    // A custom function (section 3.6): an invalid runtime argument reports runtime-error; a required
    // argument that is null at runtime makes the call null with no error.
    private OuiValue EvaluateFunction(FunctionNode node)
    {
        var spec = node.Spec;
        var (values, rejected, invalid, _) = CheckArguments(spec.Params, node.Arguments);
        if (invalid)
        {
            ReportRuntime($"@{spec.Name} received an argument of the wrong type.", "@" + spec.Name, DiagnosticTiming.StreamEnd);
        }

        if (rejected || _document.Library.GetImplementation(spec.Name) is not { } implementation)
        {
            return OuiValue.Null;
        }

        OuiValue result;
        try
        {
            result = implementation(new FunctionArguments(spec, values));
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or LimitException))
        {
            Trace.TraceError("OpenUI: @{0} threw: {1}", spec.Name, ex);
            ReportRuntime($"@{spec.Name} failed.", "@" + spec.Name, Timing);
            return OuiValue.Null;
        }

        if (spec.Returns is { } returns && !result.IsNull && PropValidator.Validate(returns, result, "", []) is null)
        {
            ReportRuntime($"@{spec.Name} returned a value that does not match its declared type.", "@" + spec.Name, Timing);
            return OuiValue.Null;
        }

        return Checked(result);
    }

    // A custom action (section 6.3): any invalid argument, or a required one that is null, makes it do nothing.
    private OuiValue EvaluateCustomAction(CustomActionNode node)
    {
        var spec = node.Spec;
        var (values, rejected, invalid, anyIssue) = CheckArguments(spec.Params, node.Arguments);
        if (invalid)
        {
            ReportRuntime($"@{spec.Name} received an argument of the wrong type.", "@" + spec.Name, Timing);
        }

        var parameters = new OuiObject.Builder();
        for (var i = 0; i < spec.Params.Length; i++)
        {
            parameters.Set(spec.Params[i].Name, values[i] ?? OuiValue.Null);
        }
        return Action(new CustomActionStep(spec, parameters.ToObject(), rejected || anyIssue));
    }

    private (ImmutableArray<OuiValue?> Values, bool Rejected, bool Invalid, bool AnyIssue) CheckArguments(
        ImmutableArray<PropSpec> parameters,
        ImmutableArray<BoundArgument?> arguments)
    {
        var values = new OuiValue?[parameters.Length];
        var rejected = false;
        var invalid = false;
        var anyIssue = false;
        for (var i = 0; i < parameters.Length; i++)
        {
            var argument = arguments[i];
            var result = argument is null ? Result.Dropped : Evaluate(argument.Value);
            _budget.Spend(PropValidator.Cost(parameters[i].Type, result.Value));
            var issues = new List<Issue>();
            var check = PropValidator.Check(parameters[i], result.IsAbsent ? (OuiValue?)null : result.Value, issues);
            anyIssue |= issues.Count > 0;
            if (argument is { IsLiteral: false } && !result.IsAbsent && !result.Value.IsNull && issues.Count > 0)
            {
                invalid = true;
            }

            switch (check.Verdict)
            {
                case ArgumentVerdict.Use:
                    values[i] = check.Value;
                    break;
                case ArgumentVerdict.Reject:
                    rejected = true;
                    break;
            }
        }

        return ([.. values], rejected, invalid, anyIssue);
    }

    // Action([...]): the steps of every action in the list; anything else in it is ignored.
    private OuiAction EvaluateActionGroup(ActionGroupNode group)
    {
        var steps = ImmutableArray.CreateBuilder<ActionStep>();
        var items = group.Items is ArrayNode array ? Collect(array.Items) : Evaluate(group.Items).Value.TryGetArray(out var list) ? list.Items : [];
        foreach (var item in items)
        {
            if (item.TryGetAction(out var action))
            {
                // Checked as it grows: groups of groups multiply, and the total may not fit in memory.
                if (steps.Count + action.Steps.Length > _limits.MaxActionSteps)
                {
                    throw new LimitException($"An action has more than {_limits.MaxActionSteps} steps.");
                }

                _budget.Spend(action.Steps.Length);
                steps.AddRange(action.Steps);
            }
        }

        return new OuiAction(steps.ToImmutable());
    }

    private DeferredExpression Defer(Bound node)
    {
        _budget.Spend(_loop.Count);
        return new DeferredExpression(node, [.. _loop], StatementId);
    }

    private static OuiAction Action(params ActionStep[] steps) => new([.. steps]);

    private OuiValue Checked(OuiValue value)
    {
        Admit(value.Measure);
        return value;
    }

    // A deep value would overflow the stack of whatever walks it next, and a wide or long one would
    // cost whoever writes it out, so neither gets past evaluation.
    private void Admit(ValueMeasure measure)
    {
        if (measure.Size > _limits.MaxValueNodes)
        {
            throw new LimitException($"A value expands to more than {_limits.MaxValueNodes} nodes.");
        }

        if (measure.Depth > _limits.MaxValueDepth)
        {
            throw new LimitException($"A value nests deeper than {_limits.MaxValueDepth} levels.");
        }

        if (measure.Chars > _limits.MaxTextChars)
        {
            throw new LimitException($"A value expands to more than {_limits.MaxTextChars} characters.");
        }
    }

    private StringBuilder CurrentPath => _frames.Count > 0 ? _frames[^1].Path : _outsideStatements;

    private Result EvaluateAt(string prop, Bound node)
    {
        var path = CurrentPath;
        var mark = path.Length;
        path.Append(mark > 0 ? "/" : "").Append(prop);
        try
        {
            return Evaluate(node);
        }
        finally
        {
            path.Length = mark;
        }
    }

    private Result EvaluateAt(int position, Bound node)
    {
        var path = CurrentPath;
        var mark = path.Length;
        path.Append(mark > 0 ? "/" : "").Append(position);
        try
        {
            return Evaluate(node);
        }
        finally
        {
            path.Length = mark;
        }
    }

    private NodeId NextNodeId() => new(CurrentPath.ToString());

    private void ReportRuntime(string message, string component, DiagnosticTiming timing)
    {
        ChargeReport();
        Report(OpenUIErrorCode.RuntimeError, OpenUIErrorSource.Runtime, message, component, null, null, timing);
    }

    // The error list hashes the statement's name to tell whether it has this error already.
    private void ChargeReport()
    {
        if (StatementId is { } name)
        {
            _budget.Read(name);
        }
    }

    private void Report(
        OpenUIErrorCode code,
        OpenUIErrorSource source,
        string message,
        string? component = null,
        string? path = null,
        string? hint = null,
        DiagnosticTiming timing = DiagnosticTiming.Immediate)
    {
        _bag.Add(
            new OpenUIError { Source = source, Code = code, Message = message, StatementId = StatementId, Component = component, Path = path, Hint = hint },
            timing);
    }

    private sealed class Frame(string name)
    {
        public string Name { get; } = name;

        public bool Tainted { get; set; }

        public StringBuilder Path { get; } = new();
    }
}
