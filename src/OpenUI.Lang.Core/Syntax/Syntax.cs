using System.Collections.Immutable;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Syntax;

/// <summary>One statement, <c>name = expression</c> (language.md, sections 1.4 and 1.6).</summary>
/// <param name="Name">The statement name: a reference, component or <c>$</c> name.</param>
/// <param name="Value">The right side.</param>
/// <param name="Span">Where the statement sits in the prepared text.</param>
public sealed record StatementSyntax(string Name, ExpressionSyntax Value, SourceSpan Span);

/// <summary>The kinds of <see cref="ExpressionSyntax"/> (language.md, section 1.6).</summary>
public enum ExpressionKind
{
    /// <summary>A string, number, <c>true</c>, <c>false</c> or <c>null</c> (language.md, section 1.6).</summary>
    Literal,

    /// <summary><c>[a, b]</c> (language.md, section 1.6).</summary>
    Array,

    /// <summary><c>{key: value}</c> (language.md, section 1.6).</summary>
    Object,

    /// <summary>A component call or an <c>@</c> call (language.md, section 1.6).</summary>
    Call,

    /// <summary>A bare identifier, which names a statement (language.md, section 1.6).</summary>
    Reference,

    /// <summary>A <c>$name</c> (language.md, section 1.6).</summary>
    StateReference,

    /// <summary><c>a.field</c> (language.md, section 1.6).</summary>
    Member,

    /// <summary><c>a[expression]</c> (language.md, section 1.6).</summary>
    Index,

    /// <summary><c>!a</c> or <c>-a</c> (language.md, section 1.6).</summary>
    Unary,

    /// <summary>A binary operator (language.md, section 1.6).</summary>
    Binary,

    /// <summary><c>cond ? a : b</c> (language.md, section 1.6).</summary>
    Ternary,

    /// <summary><c>$name = expression</c> inside an expression (language.md, section 1.5).</summary>
    BindingAssignment,
}

/// <summary>
/// An expression (language.md, sections 1.5 and 1.6). The hierarchy is closed; switch on <see cref="Kind"/>.
/// </summary>
public abstract record ExpressionSyntax
{
    private protected ExpressionSyntax()
    {
    }

    /// <summary>Which expression this is (language.md, sections 1.5 and 1.6).</summary>
    public abstract ExpressionKind Kind { get; }
}

/// <summary>A literal (language.md, section 1.3).</summary>
/// <param name="Value">The decoded value.</param>
public sealed record LiteralSyntax(OuiValue Value) : ExpressionSyntax
{
    /// <inheritdoc/>
    public override ExpressionKind Kind => ExpressionKind.Literal;
}

/// <summary>An array literal (language.md, section 1.6).</summary>
/// <param name="Items">The elements, in order.</param>
public sealed record ArraySyntax(ImmutableArray<ExpressionSyntax> Items) : ExpressionSyntax
{
    /// <inheritdoc/>
    public override ExpressionKind Kind => ExpressionKind.Array;
}

/// <summary>One <c>key: value</c> entry of an object literal (language.md, section 1.5).</summary>
/// <param name="Key">The key; <c>?</c> for a token that is not a valid key.</param>
/// <param name="Value">The value; a null literal when the <c>:</c> is missing.</param>
public sealed record ObjectEntrySyntax(string Key, ExpressionSyntax Value);

/// <summary>An object literal (language.md, sections 1.5 and 1.6).</summary>
/// <param name="Entries">The entries in source order.</param>
public sealed record ObjectSyntax(ImmutableArray<ObjectEntrySyntax> Entries) : ExpressionSyntax
{
    /// <inheritdoc/>
    public override ExpressionKind Kind => ExpressionKind.Object;
}

/// <summary>How a call is written (language.md, section 1.5).</summary>
public enum CallHeadKind
{
    /// <summary>An uppercase name: a component, or the forms <c>Query</c>, <c>Mutation</c> and <c>Action</c> (language.md, section 1.5).</summary>
    Component,

    /// <summary>An <c>@</c> name: a built-in, custom function or action (language.md, section 1.5).</summary>
    At,
}

/// <summary>A call (language.md, section 1.5).</summary>
/// <param name="Head">How the call is written.</param>
/// <param name="Name">The name, without <c>@</c>.</param>
/// <param name="Arguments">The positional arguments.</param>
public sealed record CallSyntax(CallHeadKind Head, string Name, ImmutableArray<ExpressionSyntax> Arguments) : ExpressionSyntax
{
    /// <inheritdoc/>
    public override ExpressionKind Kind => ExpressionKind.Call;
}

/// <summary>A reference to a statement (language.md, section 2.3).</summary>
/// <param name="Name">The statement name.</param>
public sealed record ReferenceSyntax(string Name) : ExpressionSyntax
{
    /// <inheritdoc/>
    public override ExpressionKind Kind => ExpressionKind.Reference;
}

/// <summary>A state variable (language.md, section 5.1).</summary>
/// <param name="Name">The name, including <c>$</c>.</param>
public sealed record StateReferenceSyntax(string Name) : ExpressionSyntax
{
    /// <inheritdoc/>
    public override ExpressionKind Kind => ExpressionKind.StateReference;
}

/// <summary>Member access, <c>a.field</c> (language.md, section 3.3).</summary>
/// <param name="Target">The object or array.</param>
/// <param name="Field">The field, read as a string key.</param>
public sealed record MemberSyntax(ExpressionSyntax Target, string Field) : ExpressionSyntax
{
    /// <inheritdoc/>
    public override ExpressionKind Kind => ExpressionKind.Member;
}

/// <summary>Indexing, <c>a[i]</c> (language.md, section 3.3).</summary>
/// <param name="Target">The array or object.</param>
/// <param name="Index">The index or key.</param>
public sealed record IndexSyntax(ExpressionSyntax Target, ExpressionSyntax Index) : ExpressionSyntax
{
    /// <inheritdoc/>
    public override ExpressionKind Kind => ExpressionKind.Index;
}

/// <summary>Unary operators (language.md, section 3.2).</summary>
public enum UnaryOperator
{
    /// <summary><c>!</c> (language.md, section 3.2).</summary>
    Not,

    /// <summary><c>-</c> (language.md, section 3.2).</summary>
    Negate,
}

/// <summary>A unary expression (language.md, section 3.2).</summary>
/// <param name="Operator">The operator.</param>
/// <param name="Operand">The operand.</param>
public sealed record UnarySyntax(UnaryOperator Operator, ExpressionSyntax Operand) : ExpressionSyntax
{
    /// <inheritdoc/>
    public override ExpressionKind Kind => ExpressionKind.Unary;
}

/// <summary>Binary operators (language.md, sections 1.5 and 3.2).</summary>
public enum BinaryOperator
{
    /// <summary><c>||</c> (language.md, sections 1.5 and 3.2).</summary>
    Or,

    /// <summary><c>&amp;&amp;</c> (language.md, sections 1.5 and 3.2).</summary>
    And,

    /// <summary><c>==</c> (language.md, sections 1.5 and 3.2).</summary>
    Equal,

    /// <summary><c>!=</c> (language.md, sections 1.5 and 3.2).</summary>
    NotEqual,

    /// <summary><c>&gt;</c> (language.md, sections 1.5 and 3.2).</summary>
    Greater,

    /// <summary><c>&lt;</c> (language.md, sections 1.5 and 3.2).</summary>
    Less,

    /// <summary><c>&gt;=</c> (language.md, sections 1.5 and 3.2).</summary>
    GreaterOrEqual,

    /// <summary><c>&lt;=</c> (language.md, sections 1.5 and 3.2).</summary>
    LessOrEqual,

    /// <summary><c>+</c> (language.md, sections 1.5 and 3.2).</summary>
    Add,

    /// <summary><c>-</c> (language.md, sections 1.5 and 3.2).</summary>
    Subtract,

    /// <summary><c>*</c> (language.md, sections 1.5 and 3.2).</summary>
    Multiply,

    /// <summary><c>/</c> (language.md, sections 1.5 and 3.2).</summary>
    Divide,

    /// <summary><c>%</c> (language.md, sections 1.5 and 3.2).</summary>
    Remainder,
}

/// <summary>A binary expression (language.md, section 3.2).</summary>
/// <param name="Operator">The operator.</param>
/// <param name="Left">The left operand.</param>
/// <param name="Right">The right operand.</param>
public sealed record BinarySyntax(BinaryOperator Operator, ExpressionSyntax Left, ExpressionSyntax Right) : ExpressionSyntax
{
    /// <inheritdoc/>
    public override ExpressionKind Kind => ExpressionKind.Binary;
}

/// <summary>A ternary, <c>cond ? a : b</c> (language.md, section 3.2).</summary>
/// <param name="Condition">The condition.</param>
/// <param name="WhenTrue">The branch for a truthy condition.</param>
/// <param name="WhenFalse">The branch for a falsy condition.</param>
public sealed record TernarySyntax(ExpressionSyntax Condition, ExpressionSyntax WhenTrue, ExpressionSyntax WhenFalse) : ExpressionSyntax
{
    /// <inheritdoc/>
    public override ExpressionKind Kind => ExpressionKind.Ternary;
}

/// <summary><c>$name = expression</c> inside an expression, used by the runtime for two-way binding (language.md, section 1.5).</summary>
/// <param name="Name">The state name, including <c>$</c>.</param>
/// <param name="Value">The assigned expression.</param>
public sealed record BindingAssignmentSyntax(string Name, ExpressionSyntax Value) : ExpressionSyntax
{
    /// <inheritdoc/>
    public override ExpressionKind Kind => ExpressionKind.BindingAssignment;
}
