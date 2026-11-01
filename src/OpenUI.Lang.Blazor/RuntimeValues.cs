using System.Globalization;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor;

/// <summary>The text and numbers components show from an <see cref="OuiValue"/>.</summary>
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
}
