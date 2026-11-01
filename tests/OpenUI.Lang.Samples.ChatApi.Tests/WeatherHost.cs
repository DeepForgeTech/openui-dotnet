using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenUI.Lang.Samples.ChatApi.Weather;

namespace OpenUI.Lang.Samples.ChatApi.Tests;

/// <summary>A clock that stands still, so a forecast and its dates are the same in every run.</summary>
internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

/// <summary>The weather endpoints, in process on a <see cref="TestServer"/>.</summary>
internal sealed class WeatherHost : IAsyncDisposable
{
    public static readonly DateOnly Today = new(2026, 11, 2);

    private readonly WebApplication _app;

    private WeatherHost(WebApplication app, WeatherService weather)
    {
        _app = app;
        Weather = weather;
        Client = app.GetTestClient();
    }

    public HttpClient Client { get; }

    public WeatherService Weather { get; }

    public static async Task<WeatherHost> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<TimeProvider>(new FixedTime(new DateTimeOffset(Today.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero)));
        builder.Services.AddSingleton<WeatherService>();
        var app = builder.Build();
        app.MapWeatherApi();
        await app.StartAsync();
        return new WeatherHost(app, app.Services.GetRequiredService<WeatherService>());
    }

    public async Task<(int Status, JsonNode Body)> GetAsync(string path)
    {
        using var response = await Client.GetAsync(path);
        return ((int)response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync())!);
    }

    public async Task<(int Status, JsonNode Body)> PostJobAsync(string json)
    {
        using var response = await Client.PostAsync("/api/spray-jobs", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        return ((int)response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync())!);
    }

    /// <summary>The first day from today on that the field can be sprayed, or the first that it cannot, with no job on it yet.</summary>
    public string FirstDay(string location, bool sprayable) =>
        Weather.GetForecast(location, WeatherService.Horizon)!.Days
            .First(day => day.Sprayable == sprayable && !Weather.GetJobs(null).Any(job => job.Location == location && job.Date == day.Date))
            .Date.ToString("yyyy-MM-dd");

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }
}
