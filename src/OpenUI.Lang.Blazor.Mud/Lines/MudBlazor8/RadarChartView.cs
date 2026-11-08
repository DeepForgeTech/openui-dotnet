namespace OpenUI.Lang.Blazor.Mud;

/// <summary>
/// MudBlazor 8 has no radar chart, so a RadarChart draws as a line chart over the same labels, a line to a series: every value
/// shows, without the web. AreaChart does the same on every line.
/// </summary>
public sealed class RadarChartView : LineChartView;
