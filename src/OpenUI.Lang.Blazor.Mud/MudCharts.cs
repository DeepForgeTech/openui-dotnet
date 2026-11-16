namespace OpenUI.Lang.Blazor.Mud;

/// <summary>
/// What the chart views share: the colours they paint with and their height. How a series becomes MudChart's series differs by
/// MudBlazor line, so that part lives in each line's folder.
/// </summary>
internal static partial class MudCharts
{
    // Mud's theme colours in the order a chart takes them, as CSS variables a plain view can paint with. Info comes last because restrained themes often make it nearly the primary.
    private static readonly string[] Palette = ["primary", "secondary", "tertiary", "warning", "error", "success", "info"];

    /// <summary>The same colours for MudChart, whose own palette is fixed and ignores the theme.</summary>
    public static string[] Colours { get; } = [.. Palette.Select(name => $"var(--mud-palette-{name})")];

    public static string Height(double? height) => height is > 0 and var pixels ? $"{pixels}px" : "300px";

    public static string Colour(int index) => Colours[index % Colours.Length];
}
