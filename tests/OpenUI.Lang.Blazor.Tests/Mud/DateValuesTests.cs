using OpenUI.Lang.Values;

using OpenUI.Lang.Blazor.Mud;

namespace OpenUI.Lang.Blazor.Tests.Mud;

// These change the process's time zone, so xunit must run them apart from everything else: it runs a collection
// with parallelization off after all the others have finished.
[CollectionDefinition(nameof(TimeZoneChanges), DisableParallelization = true)]
public sealed class TimeZoneChanges;

[Collection(nameof(TimeZoneChanges))]
public sealed class DateValuesTests
{
    // West and east of UTC, DST changes at midnight (Havana skips it, Asuncion repeats it) and a half-hour offset.
    [Theory]
    [InlineData("America/Los_Angeles")]
    [InlineData("UTC")]
    [InlineData("Pacific/Auckland")]
    [InlineData("America/Havana")]
    [InlineData("America/Asuncion")]
    [InlineData("Asia/Kolkata")]
    public void A_day_written_reads_back_as_that_day_in_any_zone(string zone) => InZone(zone, () =>
    {
        // Every day for four years, across the DST changes, and the ends of the calendar a picker can reach.
        var days = Enumerable.Range(0, 4 * 366).Select(offset => new DateTime(2024, 1, 1).AddDays(offset))
            .Concat([new DateTime(1900, 1, 1), new DateTime(2100, 12, 31)]);

        foreach (var day in days)
        {
            Assert.Equal(day, DateValues.Read(DateValues.Write(day)));
        }
    });

    [Theory]
    [InlineData("America/Los_Angeles", "2026-03-15T07:00:00.000Z")]
    [InlineData("UTC", "2026-03-15T00:00:00.000Z")]
    [InlineData("Pacific/Auckland", "2026-03-14T11:00:00.000Z")]
    [InlineData("Asia/Kolkata", "2026-03-14T18:30:00.000Z")]
    public void A_day_is_stored_as_the_utc_time_of_its_local_midnight(string zone, string stored) => InZone(zone, () =>
        Assert.Equal(stored, DateValues.Write(new DateTime(2026, 3, 15)).ToString()));

    [Theory]
    [InlineData("2026-03-15", 15)]
    [InlineData("2026-03-15T23:30:00Z", 15)]
    [InlineData("2026-03-16T02:30:00Z", 15)]
    public void A_stored_time_reads_as_its_local_day(string stored, int day) => InZone("America/Los_Angeles", () =>
        Assert.Equal(new DateTime(2026, 3, day), DateValues.Read(stored)));

    [Theory]
    [InlineData("not a date")]
    [InlineData("")]
    public void Anything_else_is_no_day(string stored) => Assert.Null(DateValues.Read(stored));

    [Fact]
    public void A_range_reads_back_and_a_half_picked_one_stores_nothing()
    {
        var range = new DayRange(new DateTime(2026, 3, 1), new DateTime(2026, 3, 9));

        Assert.Equal(range, DateValues.ReadRange(DateValues.WriteRange(range)));
        Assert.True(DateValues.WriteRange(new DayRange(new DateTime(2026, 3, 1), null)).IsNull);
        Assert.True(DateValues.WriteRange(null).IsNull);
        Assert.Null(DateValues.ReadRange("2026-03-01"));
    }

    // .NET reads TZ on Linux and macOS. Elsewhere this runs in the machine's own zone.
    private static void InZone(string zone, Action test)
    {
        var before = Environment.GetEnvironmentVariable("TZ");
        Environment.SetEnvironmentVariable("TZ", zone);
        TimeZoneInfo.ClearCachedData();
        try
        {
            test();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TZ", before);
            TimeZoneInfo.ClearCachedData();
        }
    }
}
