using MudBlazor;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor.Mud;

internal static partial class MudCharts
{
    /// <summary>The Series items as MudBlazor 9's MudChart takes them.</summary>
    public static List<ChartSeries<double>> Series(IEnumerable<OuiComponent> series) =>
    [
        .. series.Select(item => new ChartSeries<double>
        {
            Name = item.Text("category"),
            Data = item.Items("values").Select(ChartValues.Finite).ToArray(),
        }),
    ];

    public static int YStep(IEnumerable<ChartSeries<double>> series) =>
        ChartValues.TickStep(series.SelectMany(item => item.Data.Values).Select(Math.Abs).DefaultIfEmpty().Max());
}
