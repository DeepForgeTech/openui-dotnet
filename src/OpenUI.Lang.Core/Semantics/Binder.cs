using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;
using OpenUI.Lang.Syntax;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Semantics;

/// <summary>
/// Binds one statement against the library (language.md, sections 2.1, 3.6, 6.3 and 8.4) without
/// looking at other statements, so the result can be cached with the statement. Literal arguments
/// are checked here; arguments known only at runtime are checked by the evaluator.
/// </summary>
internal sealed class Binder
{
    private const int MaxHintNames = 40;

    private readonly OpenUILibrary _library;
    private readonly string _statement;
    private readonly bool _pending;
    private readonly DiagnosticBag _diagnostics;
    private readonly List<string> _loopNames = [];

    private Binder(OpenUILibrary library, string statement, bool pending, DiagnosticBag diagnostics)
    {
        _diagnostics = diagnostics;
        _library = library;
        _statement = statement;
        _pending = pending;
    }

    /// <summary>Binds <paramref name="syntax"/>. Diagnostics of a pending statement wait for stream end.</summary>
    public static BoundStatement Bind(StatementSyntax syntax, OpenUILibrary library, bool pending, DiagnosticBag diagnostics)
    {
        var binder = new Binder(library, syntax.Name, pending, diagnostics);
        var facts = StatementFacts.Of(syntax);
        Bound value;
        ToolCall? tool = null;
        if (facts.Kind is StatementKind.Query or StatementKind.Mutation)
        {
            var args = ((CallSyntax)syntax.Value).Arguments;
            tool = binder.BindToolCall(args);
            value = tool.Defaults;
        }
        else
        {
            value = binder.BindExpression(syntax.Value, inValuePosition: false);
        }

        return new BoundStatement(syntax, facts, value, tool, [.. diagnostics.Items]);
    }

    private ToolCall BindToolCall(ImmutableArray<ExpressionSyntax> args)
    {
        Bound Arg(int i, Bound fallback) => i < args.Length ? BindExpression(args[i], inValuePosition: false) : fallback;
        var empty = new ConstantNode(OuiObject.Empty);
        return new ToolCall(
            Arg(0, new ConstantNode(OuiValue.Null)),
            Arg(1, empty),
            Arg(2, new ConstantNode(OuiValue.Null)),
            args.Length > 3 ? BindExpression(args[3], inValuePosition: false) : null,
            args.Length > 1 ? StatementFacts.StateNamesIn(args[1]) : []);
    }

    private Bound BindExpression(ExpressionSyntax expression, bool inValuePosition)
    {
        // A host may raise MaxSyntaxDepth; running out of stack then becomes parse-exception, not a crash.
        RuntimeHelpers.EnsureSufficientExecutionStack();
        switch (expression)
        {
            case LiteralSyntax literal:
                return new ConstantNode(literal.Value);
            case ArraySyntax array:
                var items = array.Items.Select(item => BindExpression(item, inValuePosition: true)).ToImmutableArray();
                return items.All(item => item is ConstantNode)
                    ? new ConstantNode(OuiArray.Create(items.Select(item => ((ConstantNode)item).Value).ToImmutableArray()))
                    : new ArrayNode(items);
            case ObjectSyntax obj:
                var entries = obj.Entries.Select(e => (e.Key, BindExpression(e.Value, inValuePosition: false))).ToImmutableArray();
                return entries.All(e => e.Item2 is ConstantNode)
                    ? new ConstantNode(OuiObject.Create(entries.Select(e => KeyValuePair.Create(e.Key, ((ConstantNode)e.Item2).Value))))
                    : new ObjectNode(entries);
            case ReferenceSyntax reference:
                var slot = _loopNames.LastIndexOf(reference.Name);
                return slot >= 0 ? new LoopVariableNode(slot) : new StatementRefNode(reference.Name);
            case StateReferenceSyntax state:
                return new StateRefNode(new StateKey(state.Name));
            case BindingAssignmentSyntax binding:
                return new StateRefNode(new StateKey(binding.Name));
            case MemberSyntax member:
                return new MemberNode(BindExpression(member.Target, false), member.Field);
            case IndexSyntax index:
                return new IndexNode(BindExpression(index.Target, false), BindExpression(index.Index, false));
            case UnarySyntax unary:
                return new UnaryNode(unary.Operator, BindExpression(unary.Operand, false));
            case BinarySyntax binary:
                return new BinaryNode(binary.Operator, BindExpression(binary.Left, false), BindExpression(binary.Right, false));
            case TernarySyntax ternary:
                return new TernaryNode(
                    BindExpression(ternary.Condition, false),
                    BindExpression(ternary.WhenTrue, false),
                    BindExpression(ternary.WhenFalse, false));
            case CallSyntax call:
                return call.Head == CallHeadKind.Component ? BindComponentCall(call, inValuePosition) : BindAtCall(call);
            default:
                throw new InvalidOperationException($"Unhandled expression kind {expression.Kind}.");
        }
    }

    private Bound BindComponentCall(CallSyntax call, bool inValuePosition)
    {
        switch (call.Name)
        {
            case "Query" or "Mutation":
                if (inValuePosition)
                {
                    Report(OpenUIErrorCode.InlineReserved, $"{call.Name}(...) is valid only as the whole right side of a statement.");
                }

                return new InlineReservedNode(inValuePosition);
            case "Action":
                return BindActionGroup(call);
        }

        if (!_library.TryGetComponent(call.Name, out var spec))
        {
            Report(
                OpenUIErrorCode.UnknownComponent,
                $"Unknown component \"{call.Name}\".",
                component: call.Name,
                hint: "Available components: " + JoinCapped(_library.Components.Select(c => c.Name)));
            return new UnknownComponentNode(call.Name, inValuePosition);
        }

        var hint = $"Signature: {spec.Hint}. * marks required";
        var arguments = BindArguments(spec.Name, spec.Props, call.Arguments, inValuePosition: true, hint, spec.Name, DiagnosticTiming.Immediate);
        return new ComponentNode(spec, arguments);
    }

    private Bound BindAtCall(CallSyntax call)
    {
        var args = call.Arguments;
        Bound Arg(int i) => i < args.Length ? BindExpression(args[i], inValuePosition: false) : new ConstantNode(OuiValue.Null);
        switch (call.Name)
        {
            case "Each":
                return BindEach(args);
            case var name when ReservedNames.BuiltinFunctions.Contains(name):
                return new BuiltinNode(name, [.. args.Select(a => BindExpression(a, inValuePosition: false))]);
            case "Set":
                return new SetNode(args.Length > 0 && args[0] is StateReferenceSyntax target ? new StateKey(target.Name) : null, Arg(1));
            case "Reset":
                return new ResetNode([.. args.OfType<StateReferenceSyntax>().Select(a => new StateKey(a.Name))]);
            case "Run":
                return new RunNode(args.Length > 0 && args[0] is ReferenceSyntax target2 ? target2.Name : null);
            case "ToAssistant":
                return new ToAssistantNode(Arg(0), args.Length > 1 ? Arg(1) : null);
            case "OpenUrl":
                return new OpenUrlNode(Arg(0));
            case "Action":
                return BindActionGroup(call);
        }

        if (_library.TryGetFunction(call.Name, out var function))
        {
            var hint = "Signature: " + function.Signature;
            return new FunctionNode(function, BindArguments("@" + function.Name, function.Params, args, false, hint, null, DiagnosticTiming.StreamEnd));
        }

        if (_library.TryGetAction(call.Name, out var action))
        {
            var hint = "Signature: " + action.Signature;
            return new CustomActionNode(action, BindArguments("@" + action.Name, action.Params, args, false, hint, null, DiagnosticTiming.Immediate));
        }

        Report(
            OpenUIErrorCode.UnknownFunction,
            $"Unknown function \"@{call.Name}\".",
            hint: "Available functions: " + JoinCapped(ReservedNames.BuiltinFunctions.Order(StringComparer.Ordinal)
                .Concat(_library.Functions.Select(f => f.Name)).Select(n => "@" + n)));
        return new ConstantNode(OuiValue.Null);
    }

    private Bound BindActionGroup(CallSyntax call) =>
        new ActionGroupNode(call.Arguments.Length > 0 ? BindExpression(call.Arguments[0], inValuePosition: false) : new ConstantNode(OuiArray.Empty));

    // @Each(array, varName, template): the loop variable must be a literal string (section 3.5).
    private Bound BindEach(ImmutableArray<ExpressionSyntax> args)
    {
        var items = args.Length > 0 ? BindExpression(args[0], inValuePosition: false) : new ConstantNode(OuiValue.Null);
        if (args.Length >= 3 && args[1] is ReferenceSyntax bare)
        {
            Report(
                OpenUIErrorCode.TypeMismatch,
                $"@Each's loop name must be in quotes: write \"{bare.Name}\", not {bare.Name}.",
                path: "/varName",
                hint: $"@Each(array, \"{bare.Name}\", template)");
            return new ConstantNode(OuiArray.Empty);
        }

        if (args.Length < 3 || args[1] is not LiteralSyntax literal || !literal.Value.TryGetString(out var name))
        {
            Report(OpenUIErrorCode.TypeMismatch, "@Each needs an array, a literal variable name and a template.", path: "/varName");
            return new ConstantNode(OuiArray.Empty);
        }

        _loopNames.Add(name);
        var template = BindExpression(args[2], inValuePosition: false);
        _loopNames.RemoveAt(_loopNames.Count - 1);
        return new EachNode(items, template);
    }

    // Maps arguments to props or params by position. Extra arguments are dropped with excess-args;
    // literal arguments are checked now, the rest when evaluated.
    private ImmutableArray<BoundArgument?> BindArguments(
        string owner,
        ImmutableArray<PropSpec> props,
        ImmutableArray<ExpressionSyntax> args,
        bool inValuePosition,
        string hint,
        string? component,
        DiagnosticTiming timing)
    {
        if (args.Length > props.Length)
        {
            Report(OpenUIErrorCode.ExcessArgs, $"{owner} takes {props.Length} arguments; {args.Length - props.Length} extra dropped.", component, hint: hint, timing: timing);
        }

        var bound = ImmutableArray.CreateBuilder<BoundArgument?>(props.Length);
        for (var i = 0; i < props.Length; i++)
        {
            var prop = props[i];
            var issues = new List<Issue>();
            if (i >= args.Length)
            {
                if (prop.IsEffectivelyRequired)
                {
                    issues.Add(new Issue(OpenUIErrorCode.MissingRequired, "/" + prop.Name, false));
                }

                bound.Add(null);
            }
            else
            {
                var value = BindExpression(args[i], inValuePosition);
                var bindingName = prop.Bindable && args[i] is StateReferenceSyntax state ? state.Name : null;
                var isLiteral = value is ConstantNode && bindingName is null;
                if (isLiteral)
                {
                    PropValidator.Check(prop, ((ConstantNode)value).Value, issues);
                }

                bound.Add(new BoundArgument(value, isLiteral, bindingName));
            }

            foreach (var issue in issues)
            {
                Report(issue.Code, Messages.For(issue), component, issue.Path, hint, issue.IsEnum ? DiagnosticTiming.StreamEnd : timing);
            }
        }

        return bound.MoveToImmutable();
    }

    private void Report(
        OpenUIErrorCode code,
        string message,
        string? component = null,
        string? path = null,
        string? hint = null,
        DiagnosticTiming timing = DiagnosticTiming.Immediate)
    {
        var error = new OpenUIError
        {
            Source = OpenUIErrorSource.Parser,
            Code = code,
            Message = message,
            StatementId = _statement,
            Component = component,
            Path = path,
            Hint = hint,
        };
        _diagnostics.Add(error, _pending ? DiagnosticTiming.StreamEnd : timing);
    }

    private static string JoinCapped(IEnumerable<string> names)
    {
        var list = names.Take(MaxHintNames + 1).ToList();
        return list.Count > MaxHintNames ? string.Join(", ", list.Take(MaxHintNames)) + ", ..." : string.Join(", ", list);
    }
}

/// <summary>Error message text for the model (language.md, section 8.3). Never compared by conformance tests.</summary>
internal static class Messages
{
    public static string For(Issue issue) => issue.Code.Value switch
    {
        "missing-required" => $"missing required field \"{issue.Path}\"",
        "null-required" => $"required field \"{issue.Path}\" is null",
        _ when issue.IsEnum => $"\"{issue.Path}\" is not one of the allowed values",
        _ => $"\"{issue.Path}\" has the wrong type",
    };
}
