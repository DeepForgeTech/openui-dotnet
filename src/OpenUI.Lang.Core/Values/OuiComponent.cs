using System.Collections.Immutable;
using OpenUI.Lang.Library;

namespace OpenUI.Lang.Values;

/// <summary>A state variable name, including its <c>$</c> (language.md, sections 1.3 and 5.1).</summary>
/// <param name="Name">The name with its <c>$</c>, such as <c>$query</c>.</param>
public readonly record struct StateKey(string Name)
{
    /// <summary>The name with its <c>$</c> (language.md, sections 1.3 and 5.1).</summary>
    public override string ToString() => Name;
}

/// <summary>
/// A component's place inside its statement: the prop names and positions that reach it, such as
/// <c>children/2</c>, and empty for the statement's own value (language.md, section 9.1). It doesn't
/// change when content is added after it or a conditional elsewhere changes shape. It repeats across
/// statements and references, so a renderer that diffs a list uses <see cref="RenderKey.ForSiblings"/>.
/// </summary>
/// <param name="Value">The key text.</param>
public readonly record struct NodeId(string Value)
{
    /// <summary>The key text (language.md, section 9.1).</summary>
    public override string ToString() => Value;
}

/// <summary>
/// One evaluated prop of a component (language.md, sections 8.4 and 5.2). A prop bound to state
/// carries both the current value and the key, so a renderer can show and write it without a lookup.
/// </summary>
/// <param name="Name">The prop name from the schema.</param>
/// <param name="Value">The value after validation and recovery.</param>
/// <param name="Binding">The bound state variable when the argument was a bare <c>$name</c> on a bindable prop.</param>
public readonly record struct OuiProp(string Name, OuiValue Value, StateKey? Binding);

/// <summary>
/// An evaluated component instance (language.md, sections 3.1 and 9.1). Instances are immutable and
/// shared: a statement referenced from two places yields one instance (section 2.3 copies only the tree).
/// </summary>
public sealed class OuiComponent
{
    internal OuiComponent(
        string type,
        ComponentSpec? spec,
        string? statementId,
        NodeId id,
        ImmutableArray<OuiProp> props,
        bool isUnknown,
        string? declaredFormName)
    {
        Type = type;
        Spec = spec;
        StatementId = statementId;
        Id = id;
        Props = props;
        IsUnknown = isUnknown;
        DeclaredFormName = declaredFormName;

        var measure = new MeasureBuilder();
        measure.AddText(type);
        measure.AddText(statementId);
        measure.AddText(id.Value);
        foreach (var prop in props)
        {
            measure.AddText(prop.Name);
            measure.AddText(prop.Binding?.Name);
            measure.Add(prop.Value);
        }

        Measure = measure.ToMeasure();
    }

    /// <summary>The component name, such as <c>Card</c> (language.md, sections 3.1 and 9.1).</summary>
    public string Type { get; }

    /// <summary>The library entry, or null for a component the library does not have (section 8.4).</summary>
    public ComponentSpec? Spec { get; }

    /// <summary>The statement whose text holds this component call (Appendix B uses it as the click target) (language.md, sections 3.1 and 9.1).</summary>
    public string? StatementId { get; }

    /// <summary>A stable key for this node within its statement (language.md, sections 3.1 and 9.1).</summary>
    public NodeId Id { get; }

    /// <summary>The props present after validation, in schema order (section 8.4).</summary>
    public ImmutableArray<OuiProp> Props { get; }

    /// <summary>The prop named <paramref name="name"/>; a null value with no binding when it is absent (language.md, sections 3.1 and 9.1).</summary>
    public OuiProp this[string name] => TryGetProp(name, out var prop) ? prop : new OuiProp(name, OuiValue.Null, null);

    /// <summary>
    /// True for a component the library does not have, kept in a computed expression so its error has
    /// something to point at. It renders nothing (section 8.4).
    /// </summary>
    public bool IsUnknown { get; }

    /// <summary>
    /// The form name this component gives its subtree, read from the prop its
    /// <see cref="ComponentTraits.FormNameProp"/> names (section 5.3); null for other components.
    /// Renderers carry it down while walking, since a shared instance can sit inside different forms.
    /// </summary>
    public string? DeclaredFormName { get; }

    /// <summary>The number of value nodes in this subtree when expanded, saturating (language.md, sections 3.1 and 9.1).</summary>
    public int ExpandedSize => Measure.Size;

    internal ValueMeasure Measure { get; }

    /// <summary>Reads the prop named <paramref name="name"/> (language.md, sections 3.1 and 9.1).</summary>
    public bool TryGetProp(string name, out OuiProp prop)
    {
        foreach (var candidate in Props)
        {
            if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
            {
                prop = candidate;
                return true;
            }
        }

        prop = default;
        return false;
    }

    /// <summary>The ECMAScript <c>String</c> of a component in an expression, <c>[object Object]</c> (section 3.1).</summary>
    public override string ToString() => "[object Object]";

    internal bool StructurallyEquals(OuiComponent other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (!string.Equals(Type, other.Type, StringComparison.Ordinal)
            || !string.Equals(StatementId, other.StatementId, StringComparison.Ordinal)
            || IsUnknown != other.IsUnknown
            || Props.Length != other.Props.Length)
        {
            return false;
        }

        for (var i = 0; i < Props.Length; i++)
        {
            var (a, b) = (Props[i], other.Props[i]);
            if (!string.Equals(a.Name, b.Name, StringComparison.Ordinal) || a.Binding != b.Binding || !a.Value.Equals(b.Value))
            {
                return false;
            }
        }

        return true;
    }
}
