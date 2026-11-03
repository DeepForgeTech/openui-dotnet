using MudBlazor;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor.Mud;

/// <summary>What the chart views share: the Series items as MudChart takes them, and the colours the plain views paint with.</summary>
internal static class MudCharts
{
    // Mud spaces its grid 20 apart by default, which flattens small numbers against the axis.
    private const int GridLines = 5;

    // Mud's theme colours in the order a chart takes them, as CSS variables a plain view can paint with. Info comes last because restrained themes often make it nearly the primary.
    private static readonly string[] Palette = ["primary", "secondary", "tertiary", "warning", "error", "success", "info"];

    /// <summary>The same colours for MudChart, whose own palette is fixed and ignores the theme.</summary>
    public static string[] Colours { get; } = [.. Palette.Select(name => $"var(--mud-palette-{name})")];

    public static List<ChartSeries<double>> Series(IEnumerable<OuiComponent> series) =>
    [
        .. series.Select(item => new ChartSeries<double>
        {
            Name = item.Text("category"),
            Data = item.Items("values").Select(ChartValues.Finite).ToArray(),
        }),
    ];

    public static int YStep(IEnumerable<ChartSeries<double>> series) =>
        Step(series.SelectMany(item => item.Data.Values).Select(Math.Abs).DefaultIfEmpty().Max());

    /// <summary>The tick spacing that puts about five grid lines to a number as large as <paramref name="extent"/>.</summary>
    public static int Step(double extent) => (int)Math.Clamp(Math.Ceiling(extent / GridLines), 1, int.MaxValue);

    public static string Height(double? height) => height is > 0 and var pixels ? $"{pixels}px" : "300px";

    public static string Colour(int index) => Colours[index % Colours.Length];
}
