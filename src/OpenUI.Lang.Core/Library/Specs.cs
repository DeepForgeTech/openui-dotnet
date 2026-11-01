using System.Collections.Immutable;
using System.Text;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Library;

/// <summary>
/// One prop of a component, or one param of a function or action (prompt.md, section 2.3).
/// Position in the owning list is the argument position.
/// </summary>
/// <param name="Name">The prop name.</param>
/// <param name="Type">The prop's shape.</param>
/// <param name="Required">True when the schema lists the prop in <c>required</c>.</param>
/// <param name="Default">The schema <c>default</c>, which fills a missing or invalid argument (language.md, section 8.2).</param>
/// <param name="Bindable">True when the signature prints the prop as <c>$binding&lt;type&gt;</c> (language.md, section 5.2).</param>
public sealed record PropSpec(string Name, TypeShape Type, bool Required, OuiValue? Default = null, bool Bindable = false)
{
    /// <summary>
    /// True when a missing argument is an error: required with no default. A required prop with a
    /// default counts as optional (prompt.md, section 3.1).
    /// </summary>
    public bool IsEffectivelyRequired => Required && Default is null;
}

/// <summary>Form and field roles of a component, which a platform decides (language.md, section 5.3; prompt.md, section 2.6).</summary>
/// <param name="FormNameProp">The prop that names a form for the component's subtree, as the reference <c>Form</c>'s <c>name</c>.</param>
/// <param name="FieldNameProp">The prop that names the field an input writes, as the reference <c>Input</c>'s <c>name</c>.</param>
/// <param name="FieldComponentType">The <c>componentType</c> recorded with the field's value, such as <c>input</c>.</param>
/// <param name="FieldRulesProp">The prop holding a field's validation rules, as the reference <c>Input</c>'s <c>rules</c> (section 5.4).</param>
public readonly record struct ComponentTraits(
    string? FormNameProp = null,
    string? FieldNameProp = null,
    string? FieldComponentType = null,
    string? FieldRulesProp = null);

/// <summary>A component of the library (prompt.md, sections 2.3 and 2.4).</summary>
/// <param name="Name">The component name, uppercase (prompt.md, section 3.1).</param>
/// <param name="Description">The description for the prompt.</param>
/// <param name="Props">The props in positional order.</param>
/// <param name="Traits">Form and field roles.</param>
/// <param name="Signature">The signature line (prompt.md, section 5.1).</param>
public sealed record ComponentSpec(string Name, string Description, ImmutableArray<PropSpec> Props, ComponentTraits Traits, string Signature)
{
    /// <summary>A component whose signature line is printed from its props (prompt.md, section 5.1).</summary>
    /// <param name="name">The component name, uppercase (prompt.md, section 3.1).</param>
    /// <param name="description">The description for the prompt.</param>
    /// <param name="props">The props in positional order.</param>
    /// <param name="traits">Form and field roles.</param>
    public ComponentSpec(string name, string description, ImmutableArray<PropSpec> props, ComponentTraits traits = default)
        : this(name, description, props, traits, SignatureText.Write(name, props))
    {
    }

    /// <summary>
    /// The short signature for error hints: prop names only, required ones starred, such as
    /// <c>Metric(label*, value*)</c> (language.md, section 8.3; prompt.md, section 5.3).
    /// </summary>
    public string Hint => ShortSignature(Name, Props);

    internal static string ShortSignature(string name, ImmutableArray<PropSpec> props)
    {
        var text = new StringBuilder(name).Append('(');
        for (var i = 0; i < props.Length; i++)
        {
            text.Append(i == 0 ? "" : ", ").Append(props[i].Name).Append(props[i].Required ? "*" : "");
        }

        return text.Append(')').ToString();
    }
}

/// <summary>A custom function's declaration (language.md, section 3.6; prompt.md, section 2.5).</summary>
/// <param name="Name">The function name, called as <c>@Name</c>.</param>
/// <param name="Description">The description for the prompt.</param>
/// <param name="Params">The params in positional order.</param>
/// <param name="Returns">The declared return shape, or null when the schema gives none.</param>
/// <param name="Signature">The signature line, such as <c>Upper(text: string) → string</c>.</param>
public sealed record FunctionSpec(string Name, string Description, ImmutableArray<PropSpec> Params, TypeShape? Returns, string Signature)
{
    /// <summary>A function whose signature line is printed from its params and return shape (prompt.md, section 5.1).</summary>
    /// <param name="name">The function name, called as <c>@Name</c>.</param>
    /// <param name="description">The description for the prompt.</param>
    /// <param name="params">The params in positional order.</param>
    /// <param name="returns">The declared return shape, or null for none.</param>
    public FunctionSpec(string name, string description, ImmutableArray<PropSpec> @params, TypeShape? returns = null)
        : this(name, description, @params, returns, SignatureText.Write(name, @params, returns))
    {
    }
}

/// <summary>A custom action's declaration (language.md, section 6.3; prompt.md, section 2.5).</summary>
/// <param name="Name">The action name, called as <c>@Name</c>; also the host event's <c>type</c>.</param>
/// <param name="Description">The description for the prompt.</param>
/// <param name="Params">The params in positional order.</param>
/// <param name="Signature">The signature line, such as <c>CopyToClipboard(text: string)</c>.</param>
public sealed record ActionSpec(string Name, string Description, ImmutableArray<PropSpec> Params, string Signature)
{
    /// <summary>An action whose signature line is printed from its params (prompt.md, section 5.1).</summary>
    /// <param name="name">The action name, called as <c>@Name</c>; also the host event's <c>type</c>.</param>
    /// <param name="description">The description for the prompt.</param>
    /// <param name="params">The params in positional order.</param>
    public ActionSpec(string name, string description, ImmutableArray<PropSpec> @params)
        : this(name, description, @params, SignatureText.Write(name, @params))
    {
    }
}

/// <summary>
/// A custom function's implementation (language.md, section 3.6). It MUST be pure and synchronous:
/// the runtime may cache it and call it any number of times. Exceptions become <c>runtime-error</c>.
/// </summary>
/// <param name="args">The checked arguments, keyed by param name.</param>
public delegate OuiValue OpenUIFunction(in FunctionArguments args);

/// <summary>A custom function: its declaration and, on a client, its implementation (prompt.md, section 2.5).</summary>
/// <param name="Spec">The declaration.</param>
/// <param name="Implementation">The implementation, or null where only the spec is known (a server validator).</param>
public sealed record FunctionDefinition(FunctionSpec Spec, OpenUIFunction? Implementation);

/// <summary>
/// The arguments of one custom function call after checking (language.md, section 3.6): one slot per
/// param, in param order. A missing optional argument with no default is absent.
/// </summary>
public readonly struct FunctionArguments
{
    private readonly ImmutableArray<OuiValue?> _values;

    internal FunctionArguments(FunctionSpec function, ImmutableArray<OuiValue?> values)
    {
        Function = function;
        _values = values;
    }

    /// <summary>The function being called (language.md, section 3.6).</summary>
    public FunctionSpec Function { get; }

    /// <summary>The number of params (language.md, section 3.6).</summary>
    public int Count => _values.IsDefault ? 0 : _values.Length;

    /// <summary>The argument for the param at <paramref name="index"/>; null when absent (language.md, section 3.6).</summary>
    public OuiValue this[int index] => _values[index] ?? OuiValue.Null;

    /// <summary>The argument for the param named <paramref name="name"/>; null when absent or unknown (language.md, section 3.6).</summary>
    public OuiValue this[string name] => TryGet(name, out var value) ? value : OuiValue.Null;

    /// <summary>Reads the argument for <paramref name="name"/>; false when the param is unknown or the argument absent (language.md, section 3.6).</summary>
    public bool TryGet(string name, out OuiValue value)
    {
        for (var i = 0; i < Count; i++)
        {
            if (string.Equals(Function.Params[i].Name, name, StringComparison.Ordinal) && _values[i] is { } present)
            {
                value = present;
                return true;
            }
        }

        value = OuiValue.Null;
        return false;
    }
}

/// <summary>
/// A .NET type that describes one component and reads its typed props from an evaluated node (language.md, section 9.1).
/// Implemented by hand or by a generator; static members keep it free of reflection.
/// </summary>
/// <typeparam name="TSelf">The implementing type.</typeparam>
public interface IOpenUIComponent<TSelf>
    where TSelf : IOpenUIComponent<TSelf>
{
    /// <summary>The component's declaration (language.md, section 9.1).</summary>
    static abstract ComponentSpec Spec { get; }

    /// <summary>Reads typed props from an evaluated node; <paramref name="writer"/> writes bound props back (language.md, section 9.1).</summary>
    static abstract TSelf FromNode(OuiComponent node, IStateWriter writer);
}
