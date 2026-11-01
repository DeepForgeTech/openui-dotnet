using System.Collections.Immutable;

namespace OpenUI.Lang.Library;

/// <summary>The kinds of <see cref="TypeShape"/> (prompt.md, section 5.1).</summary>
public enum TypeShapeKind
{
    /// <summary>A string (prompt.md, section 5.1).</summary>
    String,

    /// <summary>A number; JSON Schema <c>number</c> or <c>integer</c> (prompt.md, section 5.1).</summary>
    Number,

    /// <summary>A boolean (prompt.md, section 5.1).</summary>
    Boolean,

    /// <summary>Any value (prompt.md, section 5.1).</summary>
    Any,

    /// <summary>One of a fixed set of strings (prompt.md, section 5.1).</summary>
    Enum,

    /// <summary>An array whose items have one shape (prompt.md, section 5.1).</summary>
    Array,

    /// <summary>An object, with named fields when the schema lists them (prompt.md, section 5.1).</summary>
    Object,

    /// <summary>A component slot: a <c>$ref</c> or <c>anyOf</c> of <c>$ref</c>s to components (prompt.md, section 2.3).</summary>
    ComponentRef,

    /// <summary>An action slot: <c>ActionExpression</c>, or an <c>anyOf</c> of action names (prompt.md, section 2.3).</summary>
    ActionSlot,

    /// <summary>Any of several shapes (prompt.md, section 5.1).</summary>
    Union,
}

/// <summary>
/// The type of a prop, function param or return value, read from the library's JSON Schema
/// (prompt.md, sections 2.3 and 5.1). The hierarchy is closed; switch on <see cref="Kind"/>.
/// </summary>
public abstract record TypeShape
{
    private protected TypeShape()
    {
    }

    /// <summary>Which shape this is (prompt.md, sections 2.3 and 5.1).</summary>
    public abstract TypeShapeKind Kind { get; }

    /// <summary>A string (prompt.md, sections 2.3 and 5.1).</summary>
    public static TypeShape String { get; } = new ScalarShape(TypeShapeKind.String);

    /// <summary>A number (prompt.md, sections 2.3 and 5.1).</summary>
    public static TypeShape Number { get; } = new ScalarShape(TypeShapeKind.Number);

    /// <summary>A boolean (prompt.md, sections 2.3 and 5.1).</summary>
    public static TypeShape Boolean { get; } = new ScalarShape(TypeShapeKind.Boolean);

    /// <summary>Any value (prompt.md, sections 2.3 and 5.1).</summary>
    public static TypeShape Any { get; } = new ScalarShape(TypeShapeKind.Any);
}

/// <summary>A <see cref="TypeShapeKind.String"/>, <see cref="TypeShapeKind.Number"/>, <see cref="TypeShapeKind.Boolean"/> or <see cref="TypeShapeKind.Any"/> shape (prompt.md, section 5.1).</summary>
public sealed record ScalarShape : TypeShape
{
    internal ScalarShape(TypeShapeKind kind) => Kind = kind;

    /// <inheritdoc/>
    public override TypeShapeKind Kind { get; }
}

/// <summary>One of a fixed set of strings (prompt.md, section 5.1: quoted alternatives).</summary>
/// <param name="Values">The allowed strings, in schema order.</param>
public sealed record EnumShape(ImmutableArray<string> Values) : TypeShape
{
    /// <inheritdoc/>
    public override TypeShapeKind Kind => TypeShapeKind.Enum;
}

/// <summary>An array (prompt.md, section 5.1: <c>T[]</c>). A list prop needs <c>[...]</c> (language.md, section 8.4).</summary>
/// <param name="Item">The shape of every item.</param>
public sealed record ArrayShape(TypeShape Item) : TypeShape
{
    /// <inheritdoc/>
    public override TypeShapeKind Kind => TypeShapeKind.Array;
}

/// <summary>An object (prompt.md, section 5.1: <c>{field: type}</c>).</summary>
/// <param name="Fields">The listed fields in schema order; empty for an untyped object.</param>
public sealed record ObjectShape(ImmutableArray<PropSpec> Fields) : TypeShape
{
    /// <inheritdoc/>
    public override TypeShapeKind Kind => TypeShapeKind.Object;
}

/// <summary>
/// A slot that takes components (prompt.md, section 2.3). In 1.0 it accepts any component; data
/// there is a <c>type-mismatch</c> (language.md, section 8.4).
/// </summary>
/// <param name="Components">The component names the schema lists; empty means any component.</param>
public sealed record ComponentRefShape(ImmutableArray<string> Components) : TypeShape
{
    /// <inheritdoc/>
    public override TypeShapeKind Kind => TypeShapeKind.ComponentRef;
}

/// <summary>
/// An action slot (language.md, section 6.3). It shapes the prompt and the schema only: in 1.0 it
/// accepts any action, and a plain list of actions is passed through as data.
/// </summary>
/// <param name="Actions">The action names the slot lists, such as <c>CopyToClipboard</c>; empty for <c>ActionExpression</c>.</param>
public sealed record ActionSlotShape(ImmutableArray<string> Actions) : TypeShape
{
    /// <inheritdoc/>
    public override TypeShapeKind Kind => TypeShapeKind.ActionSlot;
}

/// <summary>Any of several shapes (prompt.md, section 5.1: alternatives joined by <c>|</c>).</summary>
/// <param name="Options">The alternatives.</param>
public sealed record UnionShape(ImmutableArray<TypeShape> Options) : TypeShape
{
    /// <inheritdoc/>
    public override TypeShapeKind Kind => TypeShapeKind.Union;
}
