using System.Globalization;
using OpenUI.Lang.Blazor;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Samples.BlazorApp.Spray;

/// <summary>One day of the forecast as the spray views show it.</summary>
/// <param name="Date">The day as the API sends it, <c>yyyy-MM-dd</c>.</param>
/// <param name="HighF">The high.</param>
/// <param name="LowF">The low.</param>
/// <param name="RainChance">The chance of rain, in percent.</param>
/// <param name="WindMph">The wind.</param>
/// <param name="Sprayable">True when the API would take a job on it.</param>
public sealed record ForecastDay(string Date, double? HighF, double? LowF, double? RainChance, double? WindMph, bool Sprayable)
{
    /// <summary>The weekday, or nothing when a program sent something other than a date.</summary>
    public string Weekday => Parsed?.ToString("ddd", CultureInfo.InvariantCulture) ?? "";

    /// <summary>The day, such as <c>Nov 4</c>, or whatever a program sent when it is not a date.</summary>
    public string Day => Parsed?.ToString("MMM d", CultureInfo.InvariantCulture) ?? Date;

    /// <summary>Whether to go ahead, and if not, what stands in the way. The API's rule is wind over 15 mph or rain at 60%.</summary>
    public string Verdict => (Sprayable, WindMph > 15, RainChance >= 60) switch
    {
        (true, _, _) => "Good to spray",
        (false, true, true) => "Wind and rain",
        (false, true, false) => "Too windy",
        (false, false, true) => "Rain likely",
        _ => "Not advised",
    };

    private DateOnly? Parsed => DateOnly.TryParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    /// <summary>The days in a value a program passed, skipping anything that is not one.</summary>
    public static IReadOnlyList<ForecastDay> From(IEnumerable<OuiValue> days) =>
    [
        .. days
            .Select(day => day.TryGetObject(out var fields) ? fields : null)
            .OfType<OuiObject>()
            .Select(fields => new ForecastDay(
                fields.Text("date"),
                RuntimeValues.AsNumber(fields["highF"]),
                RuntimeValues.AsNumber(fields["lowF"]),
                RuntimeValues.AsNumber(fields["rainChance"]),
                RuntimeValues.AsNumber(fields["windMph"]),
                fields["sprayable"].TryGetBoolean(out var sprayable) && sprayable)),
    ];
}
