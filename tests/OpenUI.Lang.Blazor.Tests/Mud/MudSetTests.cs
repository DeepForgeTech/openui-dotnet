using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using OpenUI.Lang.Blazor.Mud;
using OpenUI.Lang.Runtime;

namespace OpenUI.Lang.Blazor.Tests.Mud;

public sealed class MudSetTests
{
    private static BunitContext NewContext()
    {
        var context = TestLibrary.NewContext(services => services.AddMudServices());
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Render<MudPopoverProvider>();
        return context;
    }

    private static IRenderedComponent<OpenUIProgram> Render(BunitContext context, string program) =>
        context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Library, MudLibrary.Definition)
            .Add(component => component.Program, program));

    [Fact]
    public void The_one_definition_gives_the_parser_and_the_prompt_the_same_components()
    {
        var prompt = MudLibrary.Definition.SystemPrompt();

        foreach (var component in MudLibrary.Definition.AllComponents)
        {
            Assert.Contains($"{component.Name}(", prompt);
            Assert.True(MudLibrary.Definition.Library.TryGetComponent(component.Name, out _), component.Name);
        }
    }

    [Fact]
    public async Task A_text_field_writes_its_variable_and_a_reader_follows()
    {
        await using var context = NewContext();
        var cut = Render(context, "$field = \"North\"\nroot = Stack([TextField(\"Field\", $field), Text(\"On \" + $field)])\n");
        Assert.Contains("On North", cut.Markup);

        cut.Find("input").Input("South");

        cut.WaitForAssertion(() => Assert.Contains("On South", cut.Markup));
    }

    [Fact]
    public async Task A_form_with_a_required_field_stops_its_action_and_says_why()
    {
        await using var context = NewContext();
        var cut = Render(context, "$sent = false\nroot = Form(\"job\", [TextField(\"Operator\", null, {required: true}), Button(\"Send\", @Set($sent, true)), Text($sent ? \"Sent\" : \"Not sent\")])\n");

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Contains("required", cut.Markup, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_table_draws_its_rows_with_components_in_the_cells()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = Table([\"Day\", \"Spray\"], [[\"Mon\", Chip(\"yes\", \"success\")], [\"Tue\", Chip(\"no\", \"default\")]])\n");

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
        var cut = Render(context, "root = Table([\"Day\"], [], \"Nothing yet\")\n");

        cut.WaitForAssertion(() => Assert.Contains("Nothing yet", cut.Markup));
    }

    [Fact]
    public async Task A_button_runs_its_action()
    {
        await using var context = NewContext();
        var cut = Render(context, "$n = 0\nroot = Stack([Button(\"Add\", @Set($n, $n + 1)), Text(\"Count \" + $n)])\n");

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Contains("Count 1", cut.Markup));
    }

    [Fact]
    public async Task A_button_with_no_action_sends_its_label_to_the_assistant()
    {
        await using var context = NewContext();
        var events = new List<OpenUIHostEvent>();
        var cut = context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Library, MudLibrary.Definition)
            .Add(component => component.OnHostEvent, (OpenUIHostEvent e) => events.Add(e))
            .Add(component => component.Program, "root = Button(\"Tell me more\")\n"));

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Equal("Tell me more", Assert.Single(events).HumanFriendlyMessage));
    }

    [Theory]
    [InlineData("bar", ChartType.Bar)]
    [InlineData("line", ChartType.Line)]
    public async Task A_chart_draws_each_series_with_its_labels(string kind, ChartType type)
    {
        await using var context = NewContext();
        var cut = Render(context, $"root = Chart([\"Apple\", \"Cherry\"], [{{name: \"Votes\", values: [3, 5]}}, {{name: \"Slices\", values: [2, 4]}}], \"{kind}\")\n");

        var chart = cut.FindComponent<MudChart<double>>().Instance;
        Assert.Equal(type, chart.ChartType);
        Assert.Equal(["Apple", "Cherry"], chart.ChartLabels);
        Assert.Equal(["Votes", "Slices"], chart.ChartSeries.Select(series => series.Name));
        Assert.Contains("Cherry", cut.Markup);
        Assert.Contains("Slices", cut.Markup);
        Assert.NotEmpty(cut.FindAll("svg path"));
    }

    [Theory]
    [InlineData("""root = Chart([], [])""")]
    [InlineData("""root = Chart(["A", "B", "C"], [{name: "Short", values: [1]}, {name: "Long", values: [1, 2, 3, 4, 5]}])""")]
    [InlineData("""root = Chart(["A"], [{name: "Huge", values: [1e308, -1e308, 2e308, "x"]}], "line")""")]
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
        var cut = Render(context, "$day = \"\"\nroot = Stack([Select(\"Day\", [{value: \"mon\", label: \"Monday\"}, {value: \"tue\", label: \"Tuesday\"}], $day), Text(\"Picked \" + $day)])\n");

        await cut.InvokeAsync(() => cut.FindComponent<MudSelect<string>>().Instance.ValueChanged.InvokeAsync("tue"));

        cut.WaitForAssertion(() => Assert.Contains("Picked tue", cut.Markup));
    }

    [Fact]
    public async Task A_switch_and_a_slider_write_their_variables()
    {
        await using var context = NewContext();
        var cut = Render(context, "$on = false\n$wind = 6\nroot = Stack([Switch(\"On\", $on), Slider(\"Wind\", 0, 25, $wind), Text(($on ? \"yes \" : \"no \") + $wind)])\n");

        await cut.InvokeAsync(() => cut.FindComponent<MudSwitch<bool>>().Instance.ValueChanged.InvokeAsync(true));
        await cut.InvokeAsync(() => cut.FindComponent<MudSlider<double>>().Instance.ValueChanged.InvokeAsync(9));

        cut.WaitForAssertion(() => Assert.Contains("yes 9", cut.Markup));
    }
}
