using System.Globalization;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor;

/// <summary>What components show from an <see cref="OuiValue"/>, and what a parent reads from an item component.</summary>
public static class RuntimeValues
{
    /// <summary>A scalar as text for display, the way <c>String(value)</c> prints it. Empty for null and non-scalars.</summary>
    public static string ToText(OuiValue value) =>
        value.Kind is OuiValueKind.String or OuiValueKind.Number or OuiValueKind.Boolean ? value.ToString() : "";

    /// <summary>A number, or a string that parses as one; otherwise null.</summary>
    public static double? AsNumber(OuiValue value)
    {
        if (value.TryGetNumber(out var number))
        {
            return number;
        }

        return value.TryGetString(out var text) ? AsNumber(text) : null;
    }

    /// <summary>A number typed into a field, or null when the text is not one.</summary>
    public static double? AsNumber(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    /// <summary>The components in a value: itself, or a list's in order, a list inside a list counting as one run.</summary>
    public static IEnumerable<OuiComponent> Components(OuiValue value)
    {
        if (value.TryGetComponent(out var node))
        {
            return [node];
        }

        return value.TryGetArray(out var items) ? items.SelectMany(Components) : [];
    }

    /// <summary>The prop of an item component as display text; empty for null and non-scalars.</summary>
    public static string Text(this OuiComponent item, string prop) => ToText(item[prop].Value);

    /// <summary>The prop of an item component as a number, or null.</summary>
    public static double? Number(this OuiComponent item, string prop) => AsNumber(item[prop].Value);

    /// <summary>True when the prop of an item component is the boolean true, such as a flag on an item.</summary>
    public static bool Flag(this OuiComponent item, string prop) => item[prop].Value.TryGetBoolean(out var on) && on;

    /// <summary>A field of an object as display text; empty for null and non-scalars.</summary>
    public static string Text(this OuiObject fields, string name) => ToText(fields[name]);

    /// <summary>The prop of an item component as a list; empty when it is not one.</summary>
    public static IReadOnlyList<OuiValue> Items(this OuiComponent item, string prop) =>
        item[prop].Value.TryGetArray(out var items) ? items : OuiArray.Empty;

    /// <summary>True when the input's validation rules say a value is required (language.md §5.4), so a label can mark it.</summary>
    public static bool IsRequired(this OuiComponent input) =>
        input.Spec?.Traits.FieldRulesProp is { } rulesProp
        && input[rulesProp].Value.TryGetObject(out var rules)
        && rules.TryGetValue("required", out var required)
        && required.TryGetBoolean(out var on)
        && on;
}
