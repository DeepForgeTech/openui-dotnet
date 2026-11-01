namespace OpenUI.Lang.Samples.WidgetPlayground.Data;

/// <summary>A place readings come from. <paramref name="X"/> and <paramref name="Y"/> place it on the map, in percent.</summary>
public sealed record Location(string Id, string Label, double X, double Y);

/// <summary>A kind of reading, with the range its synthetic values move in.</summary>
public sealed record MetricInfo(string Id, string Label, string Unit, double Baseline, double Swing);

/// <summary>One day's reading. <paramref name="Day"/> is <c>today</c> or <c>-Nd</c>.</summary>
public sealed record Reading(string Day, double Value);

/// <summary>A run of daily readings for one location and metric, oldest first.</summary>
public sealed record Series(string Location, string Metric, string Unit, IReadOnlyList<Reading> Readings)
{
    /// <summary>The newest reading's value.</summary>
    public double Latest => Readings.Count == 0 ? 0 : Readings[^1].Value;

    /// <summary>The lowest value.</summary>
    public double Min => Readings.Count == 0 ? 0 : Readings.Min(reading => reading.Value);

    /// <summary>The highest value.</summary>
    public double Max => Readings.Count == 0 ? 0 : Readings.Max(reading => reading.Value);

    /// <summary>The mean, to one decimal.</summary>
    public double Average => Readings.Count == 0 ? 0 : Math.Round(Readings.Average(reading => reading.Value), 1);
}

/// <summary>
/// Deterministic synthetic readings. The same location, metric and day always give the same value,
/// in every process, so screenshots and examples are repeatable.
/// </summary>
public static class SeriesData
{
    /// <summary>The longest series a caller can ask for.</summary>
    public const int MaxDays = 365;

    /// <summary>The known locations.</summary>
    public static IReadOnlyList<Location> Locations { get; } =
    [
        new("north", "North", 50, 14),
        new("west", "West", 14, 50),
        new("central", "Central", 50, 50),
        new("east", "East", 86, 50),
        new("south", "South", 50, 86),
    ];

    /// <summary>The known metrics.</summary>
    public static IReadOnlyList<MetricInfo> Metrics { get; } =
    [
        new("temperature", "Temperature", "°C", 16, 6),
        new("humidity", "Humidity", "%", 62, 14),
        new("wind", "Wind", "km/h", 15, 7),
    ];

    /// <summary>The last <paramref name="days"/> readings, ending today.</summary>
    /// <param name="location">A location id. Unknown ids still get a deterministic series.</param>
    /// <param name="metric">A metric id. Unknown ids get a neutral 0-100 range and no unit.</param>
    /// <param name="days">How many days, clamped to 1..<see cref="MaxDays"/>.</param>
    public static Series Generate(string location, string metric, int days)
    {
        var info = Metrics.FirstOrDefault(m => m.Id == metric) ?? new MetricInfo(metric, metric, "", 50, 20);
        var seed = Fnv1a($"{location}/{metric}");
        var phase = seed % 1000 / 100.0;
        var offset = (seed >> 10) % 100 / 100.0 * info.Swing - info.Swing / 2;
        var count = Math.Clamp(days, 1, MaxDays);

        // Indexed by days ago, so a shorter range shows the same recent values as a longer one.
        var readings = Enumerable.Range(0, count)
            .Select(ago => count - 1 - ago)
            .Select(ago => new Reading(
                ago == 0 ? "today" : $"-{ago}d",
                Math.Round(
                    info.Baseline + offset
                    + info.Swing * Math.Sin((ago + phase) / 3.0)
                    + info.Swing * 0.3 * Math.Sin((ago * 7 + phase) / 1.7),
                    1)))
            .ToList();
        return new Series(location, metric, info.Unit, readings);
    }

    // string.GetHashCode is randomized per process; this is not.
    private static uint Fnv1a(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text)
        {
            hash ^= c;
            hash *= 16777619u;
        }

        return hash;
    }
}
