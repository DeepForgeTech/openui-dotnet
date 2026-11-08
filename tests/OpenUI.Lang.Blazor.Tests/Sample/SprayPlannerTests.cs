using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using OpenUI.Lang.Blazor.Mud;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Samples.BlazorApp.Chat;
using OpenUI.Lang.Samples.BlazorApp.Components.Pages;
using OpenUI.Lang.Samples.BlazorApp.Spray;
using OpenUI.Lang.Samples.BlazorApp.Weather;

namespace OpenUI.Lang.Blazor.Tests.Sample;

/// <summary>The planner page with its real program, the Mud set and the real endpoints.</summary>
public sealed class SprayPlannerTests
{
    private static (BunitContext Context, IRenderedComponent<SprayPlanner> Page) Open(WeatherApi api)
    {
        var context = TestLibrary.NewContext(services =>
        {
            services.AddMudServices();
            services.AddOpenUIComponents<OpenUI.Lang.Blazor.Mud.MudComponents>();
            services.AddOpenUIComponents<SprayComponents>();
            services.AddSingleton(api.Log);
            services.AddSingleton(api.Tools);
        });
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Render<MudPopoverProvider>();
        var page = context.Render<SprayPlanner>();
        page.WaitForAssertion(() => Assert.Equal(3, api.Log.Calls.Count));
        return (context, page);
    }

    // The planner's third select picks the day.
    private static Task PickDay(IRenderedComponent<SprayPlanner> page, string day) =>
        page.InvokeAsync(() => page.FindComponents<MudSelect<string>>()[2].Instance.ValueChanged.InvokeAsync(day));

    private static Task PressSchedule(IRenderedComponent<SprayPlanner> page) => page.ClickWhere("button", "Schedule spray");

    [Fact]
    public async Task The_page_shows_what_it_fetched_and_logs_each_call()
    {
        await using var api = await WeatherApi.StartAsync();
        var (context, page) = Open(api);
        await using var _ = context;

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Spray jobs: 2", page.Markup);
            Assert.Contains("job-2", page.Markup);
            Assert.Contains("GET /api/locations", page.Find("#tool-calls").TextContent);
            Assert.Contains("None.", page.Find("#program-errors").TextContent);
        });
    }

    [Fact]
    public async Task Validation_stops_a_post_with_no_day_picked()
    {
        await using var api = await WeatherApi.StartAsync();
        var (context, page) = Open(api);
        await using var _ = context;
        var before = api.JobCount;

        await PressSchedule(page);

        page.WaitForAssertion(() => Assert.Contains("required", page.Markup, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(before, api.JobCount);
        Assert.DoesNotContain(api.Log.Calls, call => call.Tool == "scheduleSprayJob");
    }

    [Fact]
    public async Task A_good_post_reports_the_job_and_the_list_refetches()
    {
        await using var api = await WeatherApi.StartAsync();
        var (context, page) = Open(api);
        await using var _ = context;
        var day = api.FirstDay("north-field", sprayable: true);

        await PickDay(page, day);
        await PressSchedule(page);

        page.WaitForAssertion(() =>
        {
            Assert.Contains($"on {day}.", page.Find(".mud-alert-text-success, .mud-alert-filled-success, .mud-alert-outlined-success").TextContent);
            Assert.Contains("Spray jobs: 3", page.Markup);
            Assert.Contains("job-3", page.Markup);
        });
        Assert.Equal("scheduleSprayJob", api.Log.Calls.First(call => call.Status == 201).Tool);
        var listings = api.Log.Calls.Count(call => call.Tool == "listSprayJobs");
        Assert.True(listings >= 2, $"expected the jobs fetched again after the post, saw {listings} fetches");
    }

    [Fact]
    public async Task Pressing_a_forecast_tile_picks_that_day_for_the_form()
    {
        await using var api = await WeatherApi.StartAsync();
        var (context, page) = Open(api);
        await using var _ = context;
        var day = api.FirstDay("north-field", sprayable: true);
        page.WaitForAssertion(() => Assert.Equal(7, page.FindAll(".spray-day").Count));

        await page.ClickWhere(".spray-day", new ForecastDay(day, null, null, null, null, true).Day);
        page.WaitForAssertion(() => Assert.Equal("true", page.Find(".spray-day-picked").GetAttribute("aria-pressed")));
        await PressSchedule(page);

        page.WaitForAssertion(() => Assert.Contains($"on {day}.", page.Markup));
    }

    [Fact]
    public async Task The_planner_s_own_components_add_to_the_prompt_and_leave_mud_s_root_alone()
    {
        await using var context = TestLibrary.NewContext(services => services.AddOpenUIComponents<OpenUI.Lang.Blazor.Mud.MudComponents>().AddOpenUIComponents<SprayComponents>());
        var components = context.Services.GetRequiredService<OpenUIComponents>();

        Assert.Equal("Stack", components.Library.RootComponent);
        Assert.Contains("SprayForecast(days:", components.SystemPrompt());
        Assert.Contains("| SprayOutlook | SprayForecast)[]", components.SystemPrompt());
    }

    [Fact]
    public async Task A_rejected_post_shows_the_api_sentence_on_the_page()
    {
        await using var api = await WeatherApi.StartAsync();
        var (context, page) = Open(api);
        await using var _ = context;
        var before = api.JobCount;

        await PickDay(page, api.FirstDay("north-field", sprayable: false));
        await PressSchedule(page);

        page.WaitForAssertion(() =>
        {
            Assert.Contains("not a spraying day", page.Find(".mud-alert-text-error, .mud-alert-filled-error, .mud-alert-outlined-error").TextContent);
            Assert.Contains("422", page.Find("#tool-calls").TextContent);
        });
        Assert.Equal(before, api.JobCount);
    }

    // OpenUIProgram caps the values a program may expand to, and the chat draws these with it.
    [Fact]
    public async Task Every_program_the_fake_model_sends_fits_the_default_value_limit()
    {
        await using var api = await WeatherApi.StartAsync();
        await using var context = TestLibrary.NewContext(services => services.AddMudServices().AddOpenUIComponents(TestLibrary.Mud));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Render<MudPopoverProvider>();

        foreach (var message in new[] { "Can we spray?", "Show me the forecast", "Plan a spray job", "Here is the plan\ncontext and form values = [\"x\"]" })
        {
            var cut = context.Render<OpenUIProgram>(parameters => parameters
                .Add(component => component.Tools, api.Tools)
                .Add(component => component.Program, FakeReplies.For([new ChatMessage(ChatRole.User, message)])));
            await cut.Instance.Session.WhenIdleAsync();

            Assert.DoesNotContain(cut.Instance.Session.Current.Errors, error => error.Code == OpenUIErrorCode.LimitExceeded);
        }
    }
}
