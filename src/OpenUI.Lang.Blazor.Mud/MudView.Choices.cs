using System.Collections.Immutable;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor.Mud;

/// <summary>One option of a choice field, read from an item component such as a SelectItem.</summary>
/// <param name="Key">The value as text, for a widget that holds strings.</param>
/// <param name="Value">The value the program wrote, which is what goes back to it: a number stays a number.</param>
/// <param name="Label">What to show: the item's <c>label</c>, or its <c>title</c>, or else the key.</param>
/// <param name="Item">The item, for anything else it carries, such as a description.</param>
public sealed record Choice(string Key, OuiValue Value, string Label, OuiComponent Item);

/// <summary>Choice fields: one pick (Select, RadioGroup), a list of picks (Chips, OptionCards) or a map of flags (CheckBoxGroup, SwitchGroup).</summary>
public abstract partial class MudView
{
    /// <summary>The components in the <c>items</c> prop that have a value. One with no value yet, as while it streams, does not count.</summary>
    protected IReadOnlyList<OuiComponent> ItemsWithValue() => [.. ItemComponents("items").Where(item => !item["value"].Value.IsNull)];

    /// <summary>The options in the <c>items</c> prop; see <see cref="ItemsWithValue"/>.</summary>
    protected IReadOnlyList<Choice> Choices() =>
    [
        .. ItemsWithValue()
            .Select(item => new Choice(
                item.Text("value"),
                item["value"].Value,
                item.Text("label") is { Length: > 0 } label ? label : item.Text("title") is { Length: > 0 } title ? title : item.Text("value"),
                item)),
    ];

    /// <summary>The key of the one option picked; empty for none.</summary>
    protected string ChosenKey() => RuntimeValues.ToText(InputOrDefault());

    /// <summary>What to show for an option's key, such as in a closed dropdown. A key no option has shows as itself.</summary>
    protected string ChoiceLabel(string? key) =>
        Choices().FirstOrDefault(choice => choice.Key == key)?.Label ?? key ?? "";

    /// <summary>Writes the value of the option with this key as the one pick; null when no option has it.</summary>
    protected void Choose(string? key) =>
        Write("value", Choices().FirstOrDefault(choice => choice.Key == key)?.Value ?? OuiValue.Null);

    /// <summary>True when the option is picked, for a field holding one pick or a list of them.</summary>
    protected bool IsChosen(Choice choice) => Picks().Any(pick => RuntimeValues.ToText(pick) == choice.Key);

    /// <summary>
    /// Picks the option, or unpicks it if it is picked. With <c>type</c> "single" the field holds that one value or
    /// null; otherwise it holds the list of values, in the order they were picked.
    /// </summary>
    protected void Toggle(Choice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        if (HoldsOnePick())
        {
            Write("value", IsChosen(choice) ? OuiValue.Null : choice.Value);
            return;
        }

        var kept = Picks().Where(pick => RuntimeValues.ToText(pick) != choice.Key);
        Write("value", OuiArray.Create((IsChosen(choice) ? kept : kept.Append(choice.Value)).ToImmutableArray()));
    }

    /// <summary>True when the item's flag is on: its entry in the field's map by the item's <c>name</c>, or else its <c>defaultChecked</c>.</summary>
    protected bool IsOn(OuiComponent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var flag = InputOrDefault().TryGetObject(out var flags) && flags.TryGetValue(item.Text("name"), out var entry) ? entry : item["defaultChecked"].Value;
        return flag.TryGetBoolean(out var on) && on;
    }

    /// <summary>Sets the item's flag, and writes the field's whole map with every item in it (language.md §5.4 counts them for <c>required</c>).</summary>
    protected void SetOn(OuiComponent item, bool on)
    {
        ArgumentNullException.ThrowIfNull(item);
        var name = item.Text("name");
        Write("value", OuiObject.Create(ItemComponents("items").Select(other =>
            new KeyValuePair<string, OuiValue>(other.Text("name"), other.Text("name") == name ? on : IsOn(other)))));
    }

    private bool HoldsOnePick() => Text("type") is { Length: > 0 } type ? type == "single" : MudDefinitions.FieldRuleOf(Node).OnePickByDefault;

    private IEnumerable<OuiValue> Picks()
    {
        var value = InputOrDefault();
        return value.TryGetArray(out var picks) ? picks : value.IsNull ? [] : [value];
    }

    /// <inheritdoc />
    protected override void SeedField()
    {
        switch (MudDefinitions.FieldRuleOf(Node).Seed)
        {
            case FieldSeed.Default:
                SeedDefault();
                break;
            case FieldSeed.Picks:
                SeedPicks();
                break;
            case FieldSeed.Flags:
                SeedFlags();
                break;
        }
    }

    private void SeedDefault()
    {
        if (Input("value").IsNull && Value("defaultValue") is { IsNull: false } value)
        {
            Write("value", value);
        }
    }

    // The one string a single pick stores, or the list of strings a multiple pick stores.
    private void SeedPicks()
    {
        if (!Input("value").IsNull || Value("defaultValue") is not { IsNull: false } preset)
        {
            return;
        }

        var picks = (preset.TryGetArray(out var list) ? (IEnumerable<OuiValue>)list : [preset]).Where(pick => pick.TryGetString(out _)).ToImmutableArray();
        if (!HoldsOnePick())
        {
            Write("value", OuiArray.Create(picks));
        }
        else if (picks.FirstOrDefault(pick => RuntimeValues.ToText(pick).Length > 0) is { IsNull: false } first)
        {
            Write("value", first);
        }
    }

    // A flag the map does not mention draws as its default, so one that defaults on must go in, even beside a bound map's other flags.
    private void SeedFlags()
    {
        var flags = Input("value").TryGetObject(out var held) ? held : OuiObject.Empty;
        if (ItemComponents("items").FirstOrDefault(item => item.Flag("defaultChecked") && !flags.ContainsKey(item.Text("name"))) is { } preset)
        {
            SetOn(preset, true);
        }
    }
}
