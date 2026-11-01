using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Samples.BlazorApp.Weather;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor.Tests.Sample;

public sealed class WeatherToolsTests
{
    private const string NorthField = "north-field";

    private static OuiValue Args(params (string Name, OuiValue Value)[] fields)
    {
        var args = new OuiObject.Builder();
        foreach (var (name, value) in fields)
        {
            args.Set(name, value);
        }

        return args.ToObject();
    }

    private static OuiValue JobArgs(string date) =>
        Args(("location", NorthField), ("product", "Fungicide"), ("date", date));

    [Fact]
    public async Task A_read_sends_its_arguments_as_the_query_and_returns_the_body()
    {
        await using var api = await WeatherApi.StartAsync();

        var forecast = await api.Tools.InvokeAsync("getForecast", Args(("location", NorthField), ("days", 3)), CancellationToken.None);

        Assert.True(forecast.TryGetObject(out var fields));
        Assert.True(fields["days"].TryGetArray(out var days));
        Assert.Equal(3, days.Count);
        var call = Assert.Single(api.Log.Calls);
        Assert.Equal("GET /api/forecast?location=north-field&days=3", call.Request);
        Assert.Equal(200, call.Status);
        Assert.Null(call.Error);
    }

    [Fact]
    public async Task A_post_creates_the_job_on_the_server()
    {
        await using var api = await WeatherApi.StartAsync();
        var before = api.JobCount;

        var job = await api.Tools.InvokeAsync("scheduleSprayJob", JobArgs(api.FirstDay(NorthField, sprayable: true)), CancellationToken.None);

        Assert.True(job.TryGetObject(out var fields));
        Assert.Equal("Fungicide", fields["product"].ToString());
        Assert.Equal(before + 1, api.JobCount);
        Assert.Equal(201, api.Log.Calls[0].Status);
    }

    [Fact]
    public async Task A_rejected_post_fails_with_the_sentence_the_api_wrote()
    {
        await using var api = await WeatherApi.StartAsync();
        var before = api.JobCount;

        var error = await Assert.ThrowsAsync<OpenUIToolException>(async () =>
            await api.Tools.InvokeAsync("scheduleSprayJob", JobArgs(api.FirstDay(NorthField, sprayable: false)), CancellationToken.None));

        Assert.Contains("not a spraying day", error.Message);
        Assert.Equal(OpenUIErrorCode.ToolError, error.Code);
        Assert.Equal(before, api.JobCount);
        Assert.Equal(422, api.Log.Calls[0].Status);
        Assert.Equal(error.Message, api.Log.Calls[0].Error);
    }

    [Fact]
    public async Task An_api_that_is_not_there_fails_with_a_message_naming_it()
    {
        using var http = new HttpClient(new Refusing()) { BaseAddress = new Uri("http://localhost:1") };
        var log = new ToolCallLog();
        var tools = new WeatherTools(http, log);

        var error = await Assert.ThrowsAsync<OpenUIToolException>(async () =>
            await tools.InvokeAsync("listLocations", Args(), CancellationToken.None));

        Assert.Contains("http://localhost:1", error.Message);
        Assert.Null(log.Calls[0].Status);
    }

    [Theory]
    [InlineData("listLocations", OpenUIToolEffect.Read)]
    [InlineData("getForecast", OpenUIToolEffect.Read)]
    [InlineData("listSprayJobs", OpenUIToolEffect.Read)]
    [InlineData("scheduleSprayJob", OpenUIToolEffect.Mutating)]
    public async Task Each_tool_declares_whether_it_changes_anything(string tool, OpenUIToolEffect effect)
    {
        await using var api = await WeatherApi.StartAsync();

        Assert.Equal(effect, api.Tools.GetEffect(tool));
        Assert.Contains(WeatherTools.Descriptors, descriptor => descriptor.Name == tool);
    }

    private sealed class Refusing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused");
    }
}
