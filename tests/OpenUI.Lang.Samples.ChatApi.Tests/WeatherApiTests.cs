using OpenUI.Lang.Samples.ChatApi.Weather;

namespace OpenUI.Lang.Samples.ChatApi.Tests;

/// <summary>The weather endpoints that the Query and Mutation sample calls as tools.</summary>
public sealed class WeatherApiTests
{
    private const string Field = "north-field";

    [Fact]
    public async Task Locations_lists_the_fields()
    {
        await using var host = await WeatherHost.StartAsync();

        var (status, body) = await host.GetAsync("/api/locations");

        Assert.Equal(200, status);
        Assert.Equal(["north-field", "river-bend", "east-ridge"], body["locations"]!.AsArray().Select(location => (string)location!["id"]!));
    }

    [Fact]
    public async Task Forecast_starts_today_and_defaults_to_seven_days()
    {
        await using var host = await WeatherHost.StartAsync();

        var (status, body) = await host.GetAsync($"/api/forecast?location={Field}");

        Assert.Equal(200, status);
        var days = body["days"]!.AsArray();
        Assert.Equal(7, days.Count);
        Assert.Equal("2026-11-02", (string)days[0]!["date"]!);
        Assert.Equal("2026-11-08", (string)days[6]!["date"]!);
    }

    [Fact]
    public async Task The_forecast_for_a_day_does_not_change_between_fetches_or_with_the_range()
    {
        await using var host = await WeatherHost.StartAsync();

        var (_, short1) = await host.GetAsync($"/api/forecast?location={Field}&days=3");
        var (_, short2) = await host.GetAsync($"/api/forecast?location={Field}&days=3");
        var (_, long1) = await host.GetAsync($"/api/forecast?location={Field}&days=10");

        Assert.Equal(short1.ToJsonString(), short2.ToJsonString());
        Assert.Equal(short1["days"]![2]!.ToJsonString(), long1["days"]![2]!.ToJsonString());
    }

    [Fact]
    public async Task Sprayable_means_calm_and_dry()
    {
        await using var host = await WeatherHost.StartAsync();

        var (_, body) = await host.GetAsync($"/api/forecast?location={Field}&days=14");

        foreach (var day in body["days"]!.AsArray())
        {
            var calmAndDry = (int)day!["windMph"]! <= 15 && (int)day["rainChance"]! < 60;
            Assert.Equal(calmAndDry, (bool)day["sprayable"]!);
        }
    }

    [Theory]
    [InlineData("/api/forecast", 400)]
    [InlineData("/api/forecast?location=nowhere", 404)]
    [InlineData("/api/forecast?location=north-field&days=0", 400)]
    [InlineData("/api/forecast?location=north-field&days=15", 400)]
    [InlineData("/api/forecast?location=north-field&days=soon", 400)]
    [InlineData("/api/spray-jobs?status=done", 400)]
    public async Task A_bad_read_is_answered_with_a_status_and_a_readable_error(string path, int expected)
    {
        await using var host = await WeatherHost.StartAsync();

        var (status, body) = await host.GetAsync(path);

        Assert.Equal(expected, status);
        Assert.False(string.IsNullOrWhiteSpace((string?)body["error"]));
    }

    [Fact]
    public async Task The_jobs_start_with_two_on_sprayable_days()
    {
        await using var host = await WeatherHost.StartAsync();

        var (status, body) = await host.GetAsync("/api/spray-jobs");

        Assert.Equal(200, status);
        var jobs = body["jobs"]!.AsArray();
        Assert.Equal(2, jobs.Count);
        foreach (var job in jobs)
        {
            var day = host.Weather.GetForecast((string)job!["location"]!, WeatherService.Horizon)!.Days.Single(d => d.Date.ToString("yyyy-MM-dd") == (string)job["date"]!);
            Assert.True(day.Sprayable);
        }
    }

    [Fact]
    public async Task A_scheduled_job_shows_up_in_the_next_fetch()
    {
        await using var host = await WeatherHost.StartAsync();
        var date = host.FirstDay("river-bend", sprayable: true);
        var (_, before) = await host.GetAsync("/api/spray-jobs");

        var (status, created) = await host.PostJobAsync($$"""{"location":"river-bend","product":"Herbicide","date":"{{date}}"}""");
        var (_, after) = await host.GetAsync("/api/spray-jobs?status=scheduled");

        Assert.Equal(201, status);
        Assert.Equal("scheduled", (string)created["status"]!);
        Assert.Equal(before["jobs"]!.AsArray().Count + 1, after["jobs"]!.AsArray().Count);
        Assert.Contains(after["jobs"]!.AsArray(), job => (string)job!["id"]! == (string)created["id"]!);
    }

    [Fact]
    public async Task A_job_is_rejected_with_the_fields_that_are_wrong()
    {
        await using var host = await WeatherHost.StartAsync();

        var (status, body) = await host.PostJobAsync("""{"location":"nowhere","product":"Salt","date":"tomorrow"}""");

        Assert.Equal(400, status);
        Assert.Equal(["date", "location", "product"], body["fields"]!.AsObject().Select(field => field.Key).Order());
        Assert.Contains("location:", (string)body["error"]!);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"location":"north-field"}""")]
    [InlineData("""{"location":" ","product":"Fungicide","date":"2026-11-03"}""")]
    public async Task A_job_with_a_missing_member_is_a_400_that_names_it(string json)
    {
        await using var host = await WeatherHost.StartAsync();

        var (status, body) = await host.PostJobAsync(json);

        Assert.Equal(400, status);
        Assert.Contains("Required.", body["fields"]!.AsObject().Select(field => (string)field.Value!));
    }

    [Fact]
    public async Task A_day_outside_the_forecast_is_a_400()
    {
        await using var host = await WeatherHost.StartAsync();

        var (status, body) = await host.PostJobAsync("""{"location":"north-field","product":"Fungicide","date":"2027-03-01"}""");

        Assert.Equal(400, status);
        Assert.Equal("date", Assert.Single(body["fields"]!.AsObject()).Key);
    }

    [Fact]
    public async Task Yesterday_is_outside_the_forecast()
    {
        await using var host = await WeatherHost.StartAsync();
        var yesterday = WeatherHost.Today.AddDays(-1).ToString("yyyy-MM-dd");

        var (status, body) = await host.PostJobAsync($$"""{"location":"north-field","product":"Fungicide","date":"{{yesterday}}"}""");

        Assert.Equal(400, status);
        Assert.Equal("date", Assert.Single(body["fields"]!.AsObject()).Key);
    }

    [Fact]
    public async Task The_forecast_ends_on_the_last_day_of_the_horizon()
    {
        await using var host = await WeatherHost.StartAsync();
        var last = WeatherHost.Today.AddDays(WeatherService.Horizon - 1).ToString("yyyy-MM-dd");
        var past = WeatherHost.Today.AddDays(WeatherService.Horizon).ToString("yyyy-MM-dd");

        var (atEnd, _) = await host.PostJobAsync($$"""{"location":"north-field","product":"Fungicide","date":"{{last}}"}""");
        var (pastEnd, body) = await host.PostJobAsync($$"""{"location":"north-field","product":"Fungicide","date":"{{past}}"}""");

        Assert.NotEqual(400, atEnd);
        Assert.Equal(400, pastEnd);
        Assert.Equal("date", Assert.Single(body["fields"]!.AsObject()).Key);
    }

    [Fact]
    public async Task A_day_with_bad_weather_is_a_422_that_says_why_and_changes_nothing()
    {
        await using var host = await WeatherHost.StartAsync();
        var date = host.FirstDay("north-field", sprayable: false);
        var (_, before) = await host.GetAsync("/api/spray-jobs");

        var (status, body) = await host.PostJobAsync($$"""{"location":"north-field","product":"Fungicide","date":"{{date}}"}""");
        var (_, after) = await host.GetAsync("/api/spray-jobs");

        Assert.Equal(422, status);
        Assert.Contains("not a spraying day", (string)body["error"]!);
        Assert.Equal(before.ToJsonString(), after.ToJsonString());
    }

    [Fact]
    public async Task Two_fields_can_have_a_job_on_the_same_day()
    {
        await using var host = await WeatherHost.StartAsync();
        var east = host.Weather.GetForecast("east-ridge", WeatherService.Horizon)!.Days;
        var date = host.Weather.GetForecast("river-bend", WeatherService.Horizon)!.Days
            .First(day => day.Sprayable
                && east.Single(other => other.Date == day.Date).Sprayable
                && !host.Weather.GetJobs(null).Any(job => job.Date == day.Date))
            .Date.ToString("yyyy-MM-dd");

        var (first, _) = await host.PostJobAsync($$"""{"location":"river-bend","product":"Fungicide","date":"{{date}}"}""");
        var (second, _) = await host.PostJobAsync($$"""{"location":"east-ridge","product":"Fungicide","date":"{{date}}"}""");

        Assert.Equal(201, first);
        Assert.Equal(201, second);
    }

    [Fact]
    public async Task The_status_filter_leaves_out_the_other_statuses()
    {
        // Every job is "scheduled" for now, so only the service can be asked about another status.
        await using var host = await WeatherHost.StartAsync();

        Assert.Equal(2, host.Weather.GetJobs("scheduled").Count);
        Assert.Empty(host.Weather.GetJobs("done"));
    }

    [Fact]
    public async Task Every_job_gets_an_id_of_its_own()
    {
        await using var host = await WeatherHost.StartAsync();
        foreach (var location in new[] { "river-bend", "east-ridge" })
        {
            var date = host.FirstDay(location, sprayable: true);
            await host.PostJobAsync($$"""{"location":"{{location}}","product":"Fungicide","date":"{{date}}"}""");
        }

        var (_, body) = await host.GetAsync("/api/spray-jobs");

        var ids = body["jobs"]!.AsArray().Select(job => (string)job!["id"]!).ToArray();
        Assert.Equal(4, ids.Length);
        Assert.Equal(4, ids.Distinct().Count());
    }

    [Fact]
    public async Task A_second_job_on_the_same_field_and_day_is_a_409()
    {
        await using var host = await WeatherHost.StartAsync();
        var date = host.FirstDay("river-bend", sprayable: true);
        var json = $$"""{"location":"river-bend","product":"Fungicide","date":"{{date}}"}""";

        var (first, _) = await host.PostJobAsync(json);
        var (second, body) = await host.PostJobAsync(json);

        Assert.Equal(201, first);
        Assert.Equal(409, second);
        Assert.Contains("already has a job", (string)body["error"]!);
    }
}
