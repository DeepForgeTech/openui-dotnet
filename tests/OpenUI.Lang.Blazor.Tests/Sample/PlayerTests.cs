using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using OpenUI.Lang.Samples.BlazorApp.Playback;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor.Tests.Sample;

/// <summary>The sample's Player, which steps a $variable on a timer.</summary>
public sealed class PlayerTests
{
    private static readonly OuiValue[] Hours = [0, 6, 12];

    private static (BunitContext Context, IRenderedComponent<OpenUIProgram> Program) Render(string program)
    {
        var context = TestLibrary.NewContext(services => services
            .AddMudServices()
            .AddOpenUIComponents<OpenUI.Lang.Blazor.Mud.MudComponents>()
            .AddOpenUIComponents<PlaybackComponents>());
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Render<MudPopoverProvider>();
        var cut = context.Render<OpenUIProgram>(parameters => parameters.Add(component => component.Program, program));
        return (context, cut);
    }

    [Fact]
    public void The_next_value_follows_the_current_one_and_wraps_only_when_looping()
    {
        Assert.Equal(6, PlaybackSteps.Next(0, Hours, loop: true));
        Assert.Equal(0, PlaybackSteps.Next(12, Hours, loop: true));
        Assert.Null(PlaybackSteps.Next(12, Hours, loop: false));
        Assert.True(PlaybackSteps.AtEnd(12, Hours));
    }

    [Fact]
    public void A_value_not_in_the_list_starts_from_the_first_and_an_empty_list_has_no_next()
    {
        Assert.Equal(0, PlaybackSteps.Next(OuiValue.Null, Hours, loop: true));
        Assert.Equal(0, PlaybackSteps.Next(7, Hours, loop: false));
        Assert.Null(PlaybackSteps.Next(0, [], loop: true));
    }

    [Fact]
    public void A_one_item_list_such_as_a_slider_writes_steps_by_its_item_and_stays_a_list()
    {
        Assert.Equal(OuiArray.Create(12), PlaybackSteps.Next(OuiArray.Create(6), Hours, loop: true));
    }

    [Fact]
    public void Values_written_as_one_item_lists_step_as_they_are_and_never_nest()
    {
        OuiValue[] wrapped = [OuiArray.Create(0), OuiArray.Create(6), OuiArray.Create(12)];

        Assert.Equal(OuiArray.Create(6), PlaybackSteps.Next(OuiArray.Create(0), wrapped, loop: true));
        Assert.Equal(OuiArray.Create(0), PlaybackSteps.Next(OuiArray.Create(12), wrapped, loop: true));
    }

    [Fact]
    public async Task Play_steps_the_variable_on_its_timer_and_stops_at_the_end_when_it_does_not_loop()
    {
        var (context, cut) = Render("""
            $hour = 0
            root = Card([player, readout])
            player = Player($hour, [0, 6, 12], 0.1, false)
            readout = TextContent("Hour " + $hour)
            """);
        await using var _ = context;
        Assert.Contains("Hour 0", cut.Markup);

        await cut.InvokeAsync(() => cut.Find("button[aria-label=Play]").Click());

        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("button[aria-label=Pause]").GetAttribute("aria-pressed")));
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Hour 12", cut.Markup);
            Assert.NotEmpty(cut.FindAll("button[aria-label=Play]"));
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Pause_holds_the_value_where_it_is()
    {
        var (context, cut) = Render("""
            $hour = 0
            root = Card([player, readout])
            player = Player($hour, [0, 6, 12, 18, 24, 30, 36, 42, 48], 0.1)
            readout = TextContent("Hour " + $hour)
            """);
        await using var _ = context;
        await cut.InvokeAsync(() => cut.Find("button[aria-label=Play]").Click());
        cut.WaitForAssertion(() => Assert.DoesNotContain("Hour 0<", cut.Markup), TimeSpan.FromSeconds(5));

        await cut.InvokeAsync(() => cut.Find("button[aria-label=Pause]").Click());
        var held = cut.Find(".mud-typography").TextContent;
        await Task.Delay(400);

        Assert.Equal(held, cut.Find(".mud-typography").TextContent);
        Assert.NotEmpty(cut.FindAll("button[aria-label=Play]"));
    }

    [Fact]
    public async Task Step_moves_one_value_and_a_slider_on_the_same_variable_follows()
    {
        var (context, cut) = Render("""
            $hour = [0]
            root = Card([player, slider, readout])
            player = Player($hour, [0, 6, 12])
            slider = Slider("hour", "discrete", 0, 12, 6, [0], "Forecast hour", null, $hour)
            readout = TextContent("Hour " + $hour[0])
            """);
        await using var _ = context;

        await cut.InvokeAsync(() => cut.Find("button[aria-label=Step]").Click());

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Hour 6", cut.Markup);
            Assert.Contains("Forecast hour: 6", cut.Markup);
        });
    }

    [Fact]
    public async Task Changing_the_speed_while_playing_changes_the_timer_at_once()
    {
        var (context, cut) = Render("""
            $hour = 0
            $speed = "30"
            root = Card([player, fast, readout])
            player = Player($hour, [0, 6, 12, 18], $speed)
            fast = Button("Fast", @Set($speed, "0.1"))
            readout = TextContent("Hour " + $hour)
            """);
        await using var _ = context;
        await cut.InvokeAsync(() => cut.Find("button[aria-label=Play]").Click());
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button[aria-label=Pause]")));
        Assert.Equal(TimeSpan.FromSeconds(30), cut.FindComponent<PlayerView>().Instance.Interval);

        // A 30 second step would not tick inside the wait below; the new one ticks several times.
        await cut.InvokeAsync(() => cut.FindAll("button").First(button => button.TextContent.Trim() == "Fast").Click());

        cut.WaitForAssertion(() => Assert.Contains("Hour 12", cut.Markup), TimeSpan.FromSeconds(3));
    }

    [Theory]
    [InlineData("2", 2)]
    [InlineData("[0.5]", 0.5)]
    [InlineData("null", 1)]
    [InlineData("0", 0.1)]
    public async Task Seconds_can_come_as_a_number_text_or_a_one_item_list(string seconds, double expected)
    {
        var (context, cut) = Render($"root = Player($hour, [0, 6], {seconds})\n");
        await using var _ = context;

        var player = cut.FindComponent<PlayerView>();

        Assert.Equal(TimeSpan.FromSeconds(expected), player.Instance.Interval);
    }

    [Fact]
    public void The_prompt_teaches_it()
    {
        var prompt = new OpenUIComponents([new OpenUI.Lang.Blazor.Mud.MudComponents(), new PlaybackComponents()]).SystemPrompt();

        Assert.Contains("Player(", prompt, StringComparison.Ordinal);
        Assert.Contains("steps a $variable through a list of values on a timer", prompt, StringComparison.Ordinal);
    }
}
