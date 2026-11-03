using System.Collections.Frozen;
using OpenUI.Lang.Library;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor.Mud;

/// <summary>
/// The definitions of this package's components. <c>spec.json</c> is an unchanged copy of
/// <c>samples/react-chat/src/generated/spec.json</c>, the <c>openuiLibrary</c> and <c>openuiPromptOptions</c> of
/// <c>@openuidev/react-ui</c> 0.18.1 as <c>openui generate --spec</c> writes them (see THIRD_PARTY_NOTICES.md): to update
/// it, run <c>bun run generate</c> in <c>samples/react-chat</c> and copy the file here, and a test fails while the two
/// differ. What the JSON cannot say lives here: which components are items, the form and field roles, and how each field seeds.
/// </summary>
internal static class MudDefinitions
{
    private const string SpecResource = "OpenUI.Lang.Blazor.Mud.spec.json";

    // What the JSON cannot say about each input. Every one is named by its name prop.
    private static readonly FrozenDictionary<string, FieldRule> Fields = new Dictionary<string, FieldRule>(StringComparer.Ordinal)
    {
        ["Input"] = new("input"),
        ["TextArea"] = new("textarea"),
        ["Select"] = new("select"),
        ["DatePicker"] = new("datepicker"),
        ["Slider"] = new("slider", FieldSeed.Default),
        ["CheckBoxGroup"] = new("checkbox", FieldSeed.Flags),
        ["RadioGroup"] = new("radio", FieldSeed.Default),
        ["SwitchGroup"] = new("switch", FieldSeed.Flags),
        ["Chips"] = new("chips", FieldSeed.Picks),
        ["OptionCards"] = new("optioncards", FieldSeed.Picks, OnePickByDefault: true),
        ["EditableTable"] = new("editabletable"),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The embedded spec, as the file has it.</summary>
    public static LibrarySpec Spec { get; } = ReadSpec();

    /// <summary>What the package teaches: the spec, then the components of its own, which go here because the spec's file stays a copy of upstream's.</summary>
    public static LibrarySpec Taught { get; } = ComponentSources.Compose(
    [
        ComponentSources.FromSpec(Spec),
        new LibraryDefinition("openui-mudblazor-own", null,
        [
            new("Appearance",
            [
                new("ThemeSwitcher", "Lets the person choose a light or dark theme, or follow their system. The choice applies to the whole app and is remembered. Use it when they ask about the theme or appearance. Goes in a Stack or a Card.", typeof(ThemeSwitcherView),
                [
                    new("label", TypeShape.String, "A heading above the choice"),
                ]),
            ], []),
        ]),
    ]).Spec;

    /// <summary>
    /// The components a parent reads as data and never draws, so they need no view: the ones upstream's own
    /// renderers define with <c>component: () =&gt; null</c>. The JSON does not say which they are.
    /// </summary>
    public static FrozenSet<string> Items { get; } = FrozenSet.ToFrozenSet(
    [
        "TabItem", "AccordionItem", "StepsItem", "Col", "Series", "Slice", "ScatterSeries", "Point",
        "SelectItem", "CheckBoxItem", "RadioItem", "SwitchItem", "ChipItem", "OptionCard", "ListItem",
        "SnippetCardItem", "OverviewCardItem", "ContextCardItem", "CompositeCardItem", "VisualCardItem",
    ], StringComparer.Ordinal);

    /// <summary>The form and field roles of these components.</summary>
    public static void Implement(LibraryImplementation implement)
    {
        implement.Traits("Form", new ComponentTraits(FormNameProp: "name"));
        foreach (var (component, rule) in Fields)
        {
            implement.Traits(component, new ComponentTraits(
                FieldNameProp: "name",
                FieldComponentType: rule.ComponentType,
                FieldRulesProp: HasRules(component) ? "rules" : null));
        }
    }

    // A source registered after this one can have a component of the same name, which these rules know nothing about.
    public static FieldRule FieldRuleOf(OuiComponent node) =>
        Fields.TryGetValue(node.Type, out var rule) && node.Spec?.Traits.FieldComponentType == rule.ComponentType ? rule : default;

    private static LibrarySpec ReadSpec()
    {
        using var stream = typeof(MudDefinitions).Assembly.GetManifestResourceStream(SpecResource)
            ?? throw new InvalidOperationException($"The package has no embedded resource {SpecResource}.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return LibrarySpec.Parse(buffer.ToArray());
    }

    private static bool HasRules(string component) =>
        Spec.Components.First(entry => entry.Name == component).Signature.Contains(" rules?:", StringComparison.Ordinal);
}

/// <summary>How a field shows the default a program gave it in its value, once the program has finished.</summary>
internal enum FieldSeed
{
    /// <summary>It has no default, or shows none.</summary>
    None,

    /// <summary>Its <c>defaultValue</c>, as it stands.</summary>
    Default,

    /// <summary>Its <c>defaultValue</c>, as one pick or a list of them.</summary>
    Picks,

    /// <summary>A map with every item's flag, once an item has <c>defaultChecked</c>.</summary>
    Flags,
}

/// <summary>What the definitions say about one input.</summary>
/// <param name="ComponentType">The componentType recorded with its field.</param>
/// <param name="Seed">How it seeds.</param>
/// <param name="OnePickByDefault">True when a program that leaves <c>type</c> out means one pick, rather than a list.</param>
internal readonly record struct FieldRule(string ComponentType, FieldSeed Seed = FieldSeed.None, bool OnePickByDefault = false);
