using System.Globalization;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor;

/// <summary>The numbers a chart may draw from what a program sent. A program can send anything, so nothing here comes back infinite or NaN.</summary>
public static class ChartValues
{
    // A chart library that spaces its grid 20 apart flattens small numbers against the axis.
    private const int GridLines = 5;

    /// <summary>The labels as display text.</summary>
    public static string[] Labels(IEnumerable<OuiValue> labels) => [.. labels.Select(RuntimeValues.ToText)];

    /// <summary>A number a program sent, or zero when it is not a finite one.</summary>
    public static double Finite(OuiValue value) => RuntimeValues.AsNumber(value) is { } number && double.IsFinite(number) ? number : 0;

    /// <summary>The values of one series, one per label: a missing one is zero, an extra one is dropped, and a number that is not finite is zero.</summary>
    public static double[] Values(IReadOnlyList<OuiValue> values, int count) =>
        [.. Enumerable.Range(0, count).Select(index => index < values.Count ? Finite(values[index]) : 0)];

    /// <summary><see cref="Values"/> with none below zero, for a part of a whole.</summary>
    public static double[] Magnitudes(IReadOnlyList<OuiValue> values, int count) => [.. Values(values, count).Select(value => Math.Max(0, value))];

    /// <summary>The points of one scatter series that have a finite x and y. The rest are dropped: a point has no place to go without both.</summary>
    public static IReadOnlyList<(double X, double Y)> Points(OuiComponent series) =>
    [
        .. RuntimeValues.Components(series["points"].Value)
            .Select(point => (X: point.Number("x"), Y: point.Number("y")))
            .Where(point => point.X is { } x && double.IsFinite(x) && point.Y is { } y && double.IsFinite(y))
            .Select(point => (point.X!.Value, point.Y!.Value)),
    ];

    /// <summary>The largest value, or zero when none is above zero: the whole a bar or a ring is measured against.</summary>
    public static double Peak(IEnumerable<double> values) => values.Select(value => Math.Max(0, value)).DefaultIfEmpty().Max();

    /// <summary>The largest stack of bars, one value from each series per label; zero when there is none or it overflows.</summary>
    /// <param name="series">The series, each with <paramref name="count"/> values.</param>
    /// <param name="count">The number of labels.</param>
    public static double StackPeak(IReadOnlyList<double[]> series, int count) =>
        Whole(Peak(Enumerable.Range(0, count).Select(index => series.Sum(values => Math.Max(0, values[index])))));

    /// <summary>The sum of the values; zero when there are none or it overflows.</summary>
    public static double Total(IEnumerable<double> values) => Whole(values.Sum());

    /// <summary>A CSS length for <paramref name="value"/> out of <paramref name="whole"/>, never outside 0 to 100 percent.</summary>
    public static string Percent(double value, double whole) =>
        (whole > 0 && double.IsFinite(whole) ? Math.Clamp(value / whole * 100, 0, 100) : 0).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    /// <summary>A number as a chart labels it, with at most two decimals.</summary>
    public static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>The tick spacing that puts about five grid lines to a number as large as <paramref name="extent"/>; never below one.</summary>
    /// <param name="extent">The largest distance from zero the axis must reach.</param>
    public static int TickStep(double extent) => (int)Math.Clamp(Math.Ceiling(extent / GridLines), 1, int.MaxValue);

    private static double Whole(double sum) => double.IsFinite(sum) ? sum : 0;
}
