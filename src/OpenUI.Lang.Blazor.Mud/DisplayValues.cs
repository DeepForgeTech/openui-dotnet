using System.Globalization;
using System.Text.RegularExpressions;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor.Mud;

/// <summary>What a view may draw from values a program supplied: image addresses, icon names and metric trends.</summary>
internal static partial class DisplayValues
{
    /// <summary>
    /// The address of an image a program may load: an absolute <c>http</c> or <c>https</c> URL, or a relative path.
    /// Null for anything else, such as <c>javascript:</c> or <c>data:</c>, a protocol-relative <c>//host</c>, or a
    /// string with control characters or backslashes, which browsers read in ways a scheme check misses.
    /// </summary>
    public static string? ImageSource(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Any(char.IsControl) || text.Contains('\\') || text.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        // A colon before any slash, query or fragment makes what precedes it a scheme.
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        var delimiter = text.IndexOfAny(['/', '?', '#']);
        if (colon < 0 || (delimiter >= 0 && delimiter < colon))
        {
            return text;
        }

        return text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? text
            : null;
    }

    /// <summary>
    /// A length a program gave as a number of pixels or a CSS length, to put into a style. Anything but a plain
    /// length comes back as <paramref name="fallback"/>, so program text cannot add to the style.
    /// </summary>
    public static string CssLength(string? text, string fallback) =>
        text is null || !CssLengthPattern().IsMatch(text) ? fallback : text[^1] is >= '0' and <= '9' ? text + "px" : text;

    /// <summary>The first number of a <c>number[]</c> value, which is how a slider stores itself, or the value itself when it is a plain number; otherwise null.</summary>
    public static double? FirstNumber(OuiValue value) =>
        RuntimeValues.AsNumber(value.TryGetArray(out var items) && items.Count > 0 ? items[0] : value);

    /// <summary>A lucide icon name as upstream looks it up, in lower-case kebab-case: <c>CircleCheck</c> and <c>circle_check</c> read as <c>circle-check</c>.</summary>
    public static string IconName(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? ""
            : CamelBoundary().Replace(Separators().Replace(name.Trim(), "-"), "-").ToLowerInvariant();

    /// <summary>The images of an ImageGallery, each with an alt to show: its own, or its place in the gallery.</summary>
    public static IReadOnlyList<GalleryImage> GalleryImages(IEnumerable<OuiValue> images) =>
    [
        .. images
            .Select(image => image.TryGetObject(out var fields) ? fields : null)
            .OfType<OuiObject>()
            .Select((fields, index) => new GalleryImage(
                fields.Text("src"),
                fields.Text("alt") is { Length: > 0 } alt ? alt : $"Gallery image {index + 1}",
                fields.Text("details"))),
    ];

    /// <summary>
    /// A metric's trend, from <c>{direction: "up" | "down", value: number}</c>, with its text such as <c>+12%</c> or <c>-3.5%</c>.
    /// Null when the direction is neither or the value is not a finite number, so a half-streamed or malformed trend draws nothing.
    /// </summary>
    public static MetricTrend? Trend(OuiValue trend)
    {
        if (!trend.TryGetObject(out var fields)
            || !fields.TryGetValue("direction", out var direction)
            || !fields.TryGetValue("value", out var value)
            || RuntimeValues.AsNumber(value) is not { } number
            || !double.IsFinite(number))
        {
            return null;
        }

        return RuntimeValues.ToText(direction) switch
        {
            "up" => new MetricTrend(true, $"+{number.ToString(CultureInfo.InvariantCulture)}%"),
            "down" => new MetricTrend(false, $"-{number.ToString(CultureInfo.InvariantCulture)}%"),
            _ => null,
        };
    }

    [GeneratedRegex(@"^[0-9]{1,3}(\.[0-9]{1,2})?(px|rem|em)?\z")]
    private static partial Regex CssLengthPattern();

    [GeneratedRegex(@"[\s_]+")]
    private static partial Regex Separators();

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex CamelBoundary();
}

/// <summary>Which way a metric moved, and the text to show for it.</summary>
/// <param name="IsUp">True for up.</param>
/// <param name="Text">Such as <c>+12%</c>.</param>
internal sealed record MetricTrend(bool IsUp, string Text);

/// <summary>One image of an ImageGallery.</summary>
/// <param name="Src">Where it is, as the program wrote it. It goes through <see cref="DisplayValues.ImageSource"/> like any other.</param>
/// <param name="Alt">Its alternative text.</param>
/// <param name="Details">What to show under it when it is open; empty for none.</param>
internal sealed record GalleryImage(string Src, string Alt, string Details)
{
    /// <summary>How many images upstream shows in the grid. The rest show only once the gallery is open.</summary>
    public const int MaxInGrid = 5;
}
