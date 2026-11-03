using System.Globalization;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor.Mud;

/// <summary>Dates as upstream stores them: an ISO 8601 string in UTC, for the local start of the day picked.</summary>
internal static class DateValues
{
    /// <summary>The day in a stored value, or null for anything that is not a date. A bare date is that day; a time with an offset is read as local time.</summary>
    public static DateTime? Read(OuiValue value) =>
        value.TryGetString(out var text) && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
            ? (date.Kind == DateTimeKind.Utc ? date.ToLocalTime() : date).Date
            : null;

    /// <summary>The stored value for a day; null for no day.</summary>
    public static OuiValue Write(DateTime? day) =>
        day is { } date ? StartOf(date).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) : OuiValue.Null;

    /// <summary>The start and end in a stored range, or null when the value is not a range.</summary>
    public static DayRange? ReadRange(OuiValue value) =>
        value.TryGetObject(out var range) ? new DayRange(Read(range["from"]), Read(range["to"])) : null;

    /// <summary>The stored value for a range, as <c>{from, to}</c>. A half-picked range stores nothing yet.</summary>
    public static OuiValue WriteRange(DayRange? range) =>
        range is { From: { } from, To: { } to } ? OuiObject.Create([new("from", Write(from)), new("to", Write(to))]) : OuiValue.Null;

    // Where a clock change skips midnight, the day starts when the clock lands: midnight itself would read back as the day before.
    private static DateTime StartOf(DateTime day)
    {
        var start = DateTime.SpecifyKind(day.Date, DateTimeKind.Local).ToUniversalTime();
        while (start.ToLocalTime().Date < day.Date)
        {
            start = start.AddMinutes(30);
        }

        return start;
    }
}

/// <summary>A range of days as a picker holds it. Either end may be missing while it is being picked.</summary>
/// <param name="From">The first day.</param>
/// <param name="To">The last day.</param>
public sealed record DayRange(DateTime? From, DateTime? To);
