using System.Text.RegularExpressions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using OpenUI.Lang.Blazor.Mud;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Samples.BlazorApp;
using OpenUI.Lang.Samples.BlazorApp.Chat;

namespace OpenUI.Lang.Blazor.Tests.Mud;

public sealed class MudSetTests
{
    private static BunitContext NewContext()
    {
        var context = TestLibrary.NewContext(services => services.AddMudServices().AddOpenUIComponents(TestLibrary.Mud));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Render<MudPopoverProvider>();
        return context;
    }

    private static IRenderedComponent<OpenUIProgram> Render(BunitContext context, string program) =>
        context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, program));

    [Fact]
    public void The_one_definition_gives_the_parser_and_the_prompt_the_same_components()
    {
        var prompt = TestLibrary.Mud.SystemPrompt();

        foreach (var component in TestLibrary.Mud.Library.Components)
        {
            Assert.Contains($"{component.Name}(", prompt);
        }
    }

    [Fact]
    public void The_package_has_a_view_for_everything_it_is_meant_to_draw()
    {
        Assert.Empty(TestLibrary.Mud.MissingViews);
    }

    [Fact]
    public async Task An_input_writes_its_variable_and_a_reader_follows()
    {
        await using var context = NewContext();
        var cut = Render(context, "$field = \"North\"\nroot = Stack([Input(\"field\", null, \"text\", null, $field), TextContent(\"On \" + $field)])\n");
        Assert.Contains("On North", cut.Markup);

        cut.Find("input").Input("South");

        cut.WaitForAssertion(() => Assert.Contains("On South", cut.Markup));
    }

    [Fact]
    public async Task A_form_with_a_required_field_stops_its_action_and_says_why()
    {
        await using var context = NewContext();
        var cut = Render(context, "$sent = false\nroot = Stack([Form(\"job\", Buttons([Button(\"Send\", @Set($sent, true))]), [FormControl(\"Operator\", Input(\"operator\", null, \"text\", {required: true}))]), TextContent($sent ? \"Sent\" : \"Not sent\")])\n");

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Contains("required", cut.Markup, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Not sent", cut.Markup);
    }

    [Fact]
    public async Task A_form_control_marks_a_required_input_and_shows_its_hint()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = FormControl(\"Operator\", Input(\"operator\", null, \"text\", {required: true}), \"Who sprays\")\n");

        Assert.Contains("Operator *", cut.Markup);
        Assert.Contains("Who sprays", cut.Markup);
    }

    [Fact]
    public async Task A_table_draws_its_rows_with_components_in_the_cells()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = Table([Col(\"Day\", [\"Mon\", \"Tue\"]), Col(\"Spray\", [Tag(\"yes\", null, null, \"success\"), Tag(\"no\", null, null, \"neutral\")])])\n");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, cut.FindAll("tbody tr").Count);
            Assert.Equal(2, cut.FindComponents<MudChip<string>>().Count);
            Assert.Contains("Tue", cut.Markup);
        });
    }

    [Fact]
    public async Task A_table_with_no_rows_says_so()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = Table([Col(\"Day\", [])])\n");

        cut.WaitForAssertion(() => Assert.Contains("No rows", cut.Markup));
    }

    [Fact]
    public async Task A_button_runs_its_action()
    {
        await using var context = NewContext();
        var cut = Render(context, "$n = 0\nroot = Stack([Button(\"Add\", @Set($n, $n + 1)), TextContent(\"Count \" + $n)])\n");

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Contains("Count 1", cut.Markup));
    }

    [Fact]
    public async Task A_button_with_no_action_sends_its_label_to_the_assistant()
    {
        await using var context = NewContext();
        var events = new List<OpenUIHostEvent>();
        var cut = context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.OnHostEvent, (OpenUIHostEvent e) => events.Add(e))
            .Add(component => component.Program, "root = Button(\"Tell me more\")\n"));

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Equal("Tell me more", Assert.Single(events).HumanFriendlyMessage));
    }

    [Theory]
    [InlineData("BarChart", ChartType.Bar)]
    [InlineData("LineChart", ChartType.Line)]
    public async Task A_chart_draws_each_series_with_its_labels(string component, ChartType type)
    {
        await using var context = NewContext();
        var cut = Render(context, $"root = {component}([\"Apple\", \"Cherry\"], [Series(\"Votes\", [3, 5]), Series(\"Slices\", [2, 4])])\n");

        var chart = cut.FindComponent<MudChart<double>>().Instance;
        Assert.Equal(type, chart.ChartType);
        Assert.Equal(["Apple", "Cherry"], chart.ChartLabels);
        Assert.Equal(["Votes", "Slices"], chart.ChartSeries.Select(series => series.Name));
        Assert.Contains("Cherry", cut.Markup);
        Assert.Contains("Slices", cut.Markup);
        Assert.NotEmpty(cut.FindAll("svg path"));
    }

    [Fact]
    public async Task A_stacked_bar_chart_stacks()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = BarChart([\"Apple\"], [Series(\"Votes\", [3])], \"stacked\")\n");

        Assert.Equal(ChartType.StackedBar, cut.FindComponent<MudChart<double>>().Instance.ChartType);
    }

    [Theory]
    [InlineData("""root = BarChart([], [])""")]
    [InlineData("""root = BarChart([], [], "stacked")""")]
    [InlineData("""root = LineChart([], [], "natural")""")]
    [InlineData("""root = BarChart(["A", "B", "C"], [Series("Short", [1]), Series("Long", [1, 2, 3, 4, 5])])""")]
    [InlineData("""root = LineChart(["A"], [Series("Huge", [1e308, -1e308, 2e308])])""")]
    public async Task A_chart_with_odd_data_still_draws(string program)
    {
        await using var context = NewContext();
        var cut = Render(context, program + "\n");

        Assert.NotNull(cut.FindComponent<MudChart<double>>().Instance);
    }

    [Fact]
    public async Task A_select_writes_back_the_value_of_the_option_picked()
    {
        await using var context = NewContext();
        var cut = Render(context, "$day = \"\"\nroot = Stack([Select(\"day\", [SelectItem(\"mon\", \"Monday\"), SelectItem(\"tue\", \"Tuesday\")], null, null, $day), TextContent(\"Picked \" + $day)])\n");

        await cut.InvokeAsync(() => cut.FindComponent<MudSelect<string>>().Instance.ValueChanged.InvokeAsync("tue"));

        cut.WaitForAssertion(() => Assert.Contains("Picked tue", cut.Markup));
    }

    [Fact]
    public async Task A_switch_and_a_slider_write_their_variables()
    {
        await using var context = NewContext();
        var cut = Render(context, "$units = {on: false}\n$wind = [6]\nroot = Stack([SwitchGroup(\"units\", [SwitchItem(\"On\", \"\", \"on\")], null, $units), Slider(\"wind\", \"continuous\", 0, 25, null, null, \"Wind\", null, $wind), TextContent(($units.on ? \"yes \" : \"no \") + $wind[0])])\n");

        await cut.InvokeAsync(() => cut.FindComponent<MudSwitch<bool>>().Instance.ValueChanged.InvokeAsync(true));
        await cut.InvokeAsync(() => cut.FindComponent<MudSlider<double>>().Instance.ValueChanged.InvokeAsync(9));

        cut.WaitForAssertion(() => Assert.Contains("yes 9", cut.Markup));
    }

    [Fact]
    public async Task A_slider_with_a_default_shows_it_and_holds_it_in_its_form()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = Form(\"f\", Buttons([Button(\"Go\")]), [FormControl(\"Wind\", Slider(\"wind\", \"continuous\", 0, 25, 1, [7]))])\n");

        Assert.Contains("mud-typography-caption\">7<", cut.Markup);
        Assert.DoesNotContain("wind: 7", cut.Markup);
        cut.WaitForAssertion(() => Assert.True(cut.Instance.Session.Forms.Get("f", "wind").TryGetArray(out var seeded) && seeded.Count == 1));
    }

    [Fact]
    public async Task A_callout_bound_to_a_variable_hides_itself_after_three_seconds()
    {
        await using var context = NewContext();
        var cut = Render(context, "$show = true\nroot = Callout(\"success\", \"Saved\", \"Done.\", $show)\n");
        Assert.Contains("Saved", cut.Markup);

        cut.WaitForAssertion(() => Assert.DoesNotContain("Saved", cut.Markup), TimeSpan.FromSeconds(10));

        Assert.Equal("false", cut.Instance.Session.State.Get("$show").ToString());
    }

    [Fact]
    public async Task The_spray_window_presets_move_the_sliders_the_verdict_follows_and_reset_restores_it()
    {
        await using var context = NewContext();
        var cut = Render(context, ProgramFiles.Read("spray-window.oui"));
        Assert.Contains("Good to spray", cut.Markup);
        Assert.Contains("Wind speed (mph): 6", cut.Markup);

        await cut.ClickWhere("button", "Gusty afternoon");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Hold off", cut.Markup);
            Assert.Contains("2 conditions fail.", cut.Markup);
            Assert.Contains("Wind speed (mph): 17", cut.Markup);
        });

        await cut.InvokeAsync(() => cut.FindComponent<MudSwitch<bool>>().Instance.ValueChanged.InvokeAsync(true));

        cut.WaitForAssertion(() => Assert.Contains("Wind 27 km/h", cut.Markup));

        await cut.ClickWhere("button", "Reset");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Good to spray", cut.Markup);
            Assert.Contains("Wind 6 mph", cut.Markup);
        });
    }

    [Theory]
    [InlineData("Can we spray?")]
    [InlineData("Show me the forecast")]
    [InlineData("Plan a spray job")]
    public async Task No_canned_reply_draws_an_unknown_component(string message)
    {
        await using var context = NewContext();
        var cut = Render(context, FakeReplies.For([new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, message)]));

        Assert.DoesNotContain(cut.Instance.Session.Current.Errors, error => error.Code == OpenUI.Lang.Diagnostics.OpenUIErrorCode.UnknownComponent);
        Assert.DoesNotMatch(new Regex("unknown", RegexOptions.IgnoreCase), cut.Markup);
    }
}
