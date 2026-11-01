using System.Collections.Immutable;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;
using OpenUI.Lang.Syntax;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Semantics;

/// <summary>
/// The bound form of an expression: syntax resolved against the library, independent of other
/// statements (statement references resolve by name when evaluated). Closed hierarchy; internal so it
/// can change between versions.
/// </summary>
internal abstract record Bound;

/// <summary>A literal, or an array or object built only from literals, folded into one value.</summary>
internal sealed record ConstantNode(OuiValue Value) : Bound;

/// <summary>An array literal; absent elements are left out (language.md, sections 2.3 and 8.4).</summary>
internal sealed record ArrayNode(ImmutableArray<Bound> Items) : Bound;

/// <summary>An object literal.</summary>
internal sealed record ObjectNode(ImmutableArray<(string Key, Bound Value)> Entries) : Bound;

/// <summary>One positional argument mapped to a prop or param. <see cref="IsLiteral"/> arguments were checked when binding.</summary>
internal sealed record BoundArgument(Bound Value, bool IsLiteral, string? BindingName);

/// <summary>A library component call; <see cref="Arguments"/> has one slot per prop, null where no argument was given.</summary>
internal sealed record ComponentNode(ComponentSpec Spec, ImmutableArray<BoundArgument?> Arguments) : Bound;

/// <summary>A component the library lacks: dropped in a value position, an inert node elsewhere (section 8.4).</summary>
internal sealed record UnknownComponentNode(string Name, bool InValuePosition) : Bound;

/// <summary><c>Query</c> or <c>Mutation</c> inside an expression: nothing in a value position, null elsewhere (section 2.1).</summary>
internal sealed record InlineReservedNode(bool InValuePosition) : Bound;

/// <summary>A reference to a statement, resolved by name when evaluated.</summary>
internal sealed record StatementRefNode(string Name) : Bound;

/// <summary>A <c>$name</c>.</summary>
internal sealed record StateRefNode(StateKey Key) : Bound;

/// <summary>An <c>@Each</c> loop variable; <see cref="Slot"/> counts enclosing loops from the outermost.</summary>
internal sealed record LoopVariableNode(int Slot) : Bound;

internal sealed record MemberNode(Bound Target, string Field) : Bound;

internal sealed record IndexNode(Bound Target, Bound Index) : Bound;

internal sealed record UnaryNode(UnaryOperator Operator, Bound Operand) : Bound;

internal sealed record BinaryNode(BinaryOperator Operator, Bound Left, Bound Right) : Bound;

internal sealed record TernaryNode(Bound Condition, Bound WhenTrue, Bound WhenFalse) : Bound;

/// <summary>A built-in function other than <c>@Each</c> (section 3.4).</summary>
internal sealed record BuiltinNode(string Name, ImmutableArray<Bound> Arguments) : Bound;

/// <summary><c>@Each(array, varName, template)</c> (section 3.5).</summary>
internal sealed record EachNode(Bound Items, Bound Template) : Bound;

/// <summary>A custom function call (section 3.6); one slot per param.</summary>
internal sealed record FunctionNode(FunctionSpec Spec, ImmutableArray<BoundArgument?> Arguments) : Bound;

/// <summary><c>@Set($var, value)</c>; a null target makes an action with no steps.</summary>
internal sealed record SetNode(StateKey? Target, Bound Value) : Bound;

internal sealed record ResetNode(ImmutableArray<StateKey> Targets) : Bound;

/// <summary><c>@Run(ref)</c>; a null target makes an action with no steps.</summary>
internal sealed record RunNode(string? Target) : Bound;

internal sealed record ToAssistantNode(Bound Message, Bound? Context) : Bound;

internal sealed record OpenUrlNode(Bound Url) : Bound;

/// <summary>A custom action (section 6.3); one slot per param.</summary>
internal sealed record CustomActionNode(ActionSpec Spec, ImmutableArray<BoundArgument?> Arguments) : Bound;

/// <summary><c>Action([...])</c>: the steps of every action in the list, in order.</summary>
internal sealed record ActionGroupNode(Bound Items) : Bound;

/// <summary>The bound parts of a query or mutation statement (sections 6.1 and 6.2).</summary>
internal sealed record ToolCall(Bound Tool, Bound Arguments, Bound Defaults, Bound? RefreshSeconds, ImmutableArray<string> LiteralStateDeps);

/// <summary>One statement after binding: everything a document and the evaluator need from it.</summary>
internal sealed record BoundStatement(
    StatementSyntax Syntax,
    StatementFacts Facts,
    Bound Value,
    ToolCall? Tool,
    ImmutableArray<Diagnostic> Diagnostics)
{
    public string Name => Syntax.Name;

    public StatementKind Kind => Facts.Kind;
}
