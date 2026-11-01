using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenUI.Lang.Samples.BlazorApp.Weather;
using OpenUI.Lang.Samples.ChatApi.Weather;

namespace OpenUI.Lang.Blazor.Tests.Sample;

/// <summary>The sample API's real weather endpoints, in process, with the sample's tools talking to them over its HTTP pipeline.</summary>
internal sealed class WeatherApi : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    private WeatherApi(WebApplication app)
    {
        _app = app;
        _client = app.GetTestClient();
        Tools = new WeatherTools(_client, Log);
    }

    public ToolCallLog Log { get; } = new();

    public WeatherTools Tools { get; }

    private WeatherService Weather => _app.Services.GetRequiredService<WeatherService>();

    public static async Task<WeatherApi> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<WeatherService>();
        var app = builder.Build();
        app.MapWeatherApi();
        await app.StartAsync();
        return new WeatherApi(app);
    }

    /// <summary>The first day the field can (or cannot) be sprayed that has no job on it yet.</summary>
    public string FirstDay(string location, bool sprayable) =>
        Weather.GetForecast(location, WeatherService.Horizon)!.Days
            .First(day => day.Sprayable == sprayable && !Weather.GetJobs(null).Any(job => job.Location == location && job.Date == day.Date))
            .Date.ToString("yyyy-MM-dd");

    public int JobCount => Weather.GetJobs(null).Count;

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }
}
