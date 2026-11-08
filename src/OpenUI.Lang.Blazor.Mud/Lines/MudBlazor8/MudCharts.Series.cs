using MudBlazor;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor.Mud;

internal static partial class MudCharts
{
    /// <summary>The Series items as MudBlazor 8's MudChart takes them.</summary>
    public static List<ChartSeries> Series(IEnumerable<OuiComponent> series) =>
    [
        .. series.Select(item => new ChartSeries
        {
            Name = item.Text("category"),
            Data = [.. item.Items("values").Select(ChartValues.Finite)],
        }),
    ];

    public static int YStep(IEnumerable<ChartSeries> series) =>
        ChartValues.TickStep(series.SelectMany(item => item.Data).Select(Math.Abs).DefaultIfEmpty().Max());
}
