using System.Globalization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Services;
using OpenUI.Lang.Blazor.Mud;

namespace OpenUI.Lang.Blazor.Tests.Mud;

/// <summary>What the MudBlazor 8 line draws in place of MudBlazor 9's charts, and the parameters MudBlazor 8 names its own way.</summary>
public sealed class MudLineTests
{
    private static BunitContext NewContext()
    {
        var context = TestLibrary.NewContext(services => services.AddMudServices().AddMudThemeMode().AddOpenUIComponents(TestLibrary.Mud));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Render<MudPopoverProvider>();
        return context;
    }

    private static IRenderedComponent<OpenUIProgram> Render(BunitContext context, string program) =>
        context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, program));

    [Fact]
    public async Task The_theme_provider_leaves_following_the_system_to_the_theme_mode_state()
    {
        await using var context = NewContext();

        // MudBlazor 9's name for this throws on 8, so the provider would not draw at all.
        var browser = context.JSInterop.SetupModule(ThemeModeState.ModulePath);
        browser.Setup<StoredThemeMode?>("start", _ => true).SetResult(new StoredThemeMode(null, false));
        browser.SetupVoid("paint", _ => true).SetVoidResult();
        var provider = context.Render<ThemeModeProvider>();

        provider.WaitForAssertion(() => provider.FindComponent<MudThemeProvider>());

        Assert.False(provider.FindComponent<MudThemeProvider>().Instance.GetState(mud => mud.ObserveSystemThemeChange));
    }

    [Theory]
    [InlineData("BarChart", ChartType.Bar)]
    [InlineData("LineChart", ChartType.Line)]
    public async Task A_chart_draws_each_series_with_its_labels(string component, ChartType type)
    {
        await using var context = NewContext();
        var cut = Render(context, $"root = {component}([\"Apple\", \"Cherry\"], [Series(\"Votes\", [3, 5]), Series(\"Slices\", [2, 4])])\n");

        var chart = cut.FindComponent<MudChart>().Instance;
        Assert.Equal(type, chart.ChartType);
        Assert.Equal(["Apple", "Cherry"], chart.XAxisLabels);
        Assert.Equal(["Votes", "Slices"], chart.ChartSeries.Select(series => series.Name));
        Assert.Equal([[3d, 5], [2d, 4]], chart.ChartSeries.Select(series => series.Data));
        Assert.Contains("Cherry", cut.Markup);
        Assert.Contains("Slices", cut.Markup);
        Assert.NotEmpty(cut.FindAll("svg path"));
    }

    [Theory]
    [InlineData("BarChart")]
    [InlineData("LineChart")]
    public async Task A_chart_prints_its_axis_titles_which_mud_8_cannot_draw(string component)
    {
        await using var context = NewContext();
        var cut = Render(context, $"root = {component}([\"Mon\"], [Series(\"Rain\", [3])], null, \"Day\", \"Inches\")\n");

        Assert.Equal(["Inches", "Day"], cut.FindAll(".mud-typography-caption").Select(caption => caption.TextContent));
    }

    [Fact]
    public async Task A_stacked_bar_chart_stacks()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = BarChart([\"Apple\"], [Series(\"Votes\", [3])], \"stacked\")\n");

        Assert.Equal(ChartType.StackedBar, cut.FindComponent<MudChart>().Instance.ChartType);
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

        Assert.NotNull(cut.FindComponent<MudChart>().Instance);
    }

    [Theory]
    [InlineData(ChartType.Pie, "")]
    [InlineData(ChartType.Donut, ", \"donut\"")]
    public async Task A_pie_draws_a_slice_for_each_label(ChartType type, string variant)
    {
        await using var context = NewContext();
        var cut = Render(context, $"root = PieChart([\"Corn\", \"Wheat\", \"Soy\"], [50, 30, 20]{variant})\n");

        var chart = cut.FindComponent<MudChart>().Instance;
        Assert.Equal(type, chart.ChartType);
        Assert.Equal(["Corn", "Wheat", "Soy"], chart.InputLabels);
        Assert.Equal([50d, 30, 20], chart.InputData!);
        Assert.Equal(3, cut.FindAll("path.mud-chart-serie, circle.mud-chart-serie").Count);
    }

    [Fact]
    public async Task A_pie_with_nothing_to_divide_says_so()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = PieChart([\"Corn\"], [0])\n");

        Assert.Empty(cut.FindComponents<MudChart>());
        Assert.Contains("No data.", cut.Markup);
    }

    [Fact]
    public async Task A_radar_draws_each_series_as_a_line_over_the_labels_since_mud_8_has_no_radar()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = RadarChart([\"Speed\", \"Reach\", \"Cost\"], [Series(\"Us\", [4, 3, 5]), Series(\"Them\", [2, 5, 3])])\n");

        var chart = cut.FindComponent<MudChart>().Instance;
        Assert.Equal(ChartType.Line, chart.ChartType);
        Assert.Equal(["Speed", "Reach", "Cost"], chart.XAxisLabels);
        Assert.Equal(["Us", "Them"], chart.ChartSeries.Select(series => series.Name));
    }

    [Theory]
    [InlineData(2, InterpolationOption.Straight)]
    [InlineData(4, InterpolationOption.NaturalSpline)]
    public async Task A_natural_line_is_drawn_straight_through_fewer_points_than_mud_8_can_curve(int points, InterpolationOption drawn)
    {
        await using var context = NewContext();
        var values = string.Join(", ", Enumerable.Range(1, points));
        var labels = string.Join(", ", Enumerable.Range(1, points).Select(index => $"\"{index}\""));
        var cut = Render(context, $"root = LineChart([{labels}], [Series(\"Rain\", [{values}])], \"natural\")\n");

        Assert.Equal(drawn, cut.FindComponent<MudChart>().Instance.ChartOptions.InterpolationOption);
    }

    [Fact]
    public async Task An_area_chart_draws_its_series_as_lines()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = AreaChart([\"Mon\", \"Tue\"], [Series(\"Rain\", [3, 5])], \"natural\")\n");

        var chart = cut.FindComponent<MudChart>().Instance;
        Assert.Equal(ChartType.Line, chart.ChartType);
        Assert.Equal(["Rain"], chart.ChartSeries.Select(series => series.Name));
    }

    [Fact]
    public async Task A_scatter_draws_each_dataset_and_leaves_out_a_point_that_is_not_a_number()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = ScatterChart([ScatterSeries(\"Plot A\", [Point(1, 2), Point(3, 4, 9), Point(2e308, 1)]), ScatterSeries(\"Plot B\", [Point(2, 5)])], \"Rain\", \"Yield\")\n");

        Assert.Equal(["Plot A: (1, 2)", "Plot A: (3, 4)", "Plot B: (2, 5)"], cut.FindAll("circle title").Select(title => title.TextContent));
        Assert.Contains("Rain", cut.Markup);
        Assert.Contains("Yield", cut.Markup);
    }

    [Fact]
    public async Task A_scatter_places_each_point_by_its_x_and_y_from_zero()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = ScatterChart([ScatterSeries(\"A\", [Point(0, 0), Point(10, 5), Point(5, 10)])])\n");

        // The plot runs from x 40 to 310 and from y 196 up to 10, and both axes run from 0 to 10.
        Assert.Equal(
            [("40", "196"), ("310", "103"), ("175", "10")],
            cut.FindAll("circle").Select(point => (point.GetAttribute("cx"), point.GetAttribute("cy"))));
    }

    [Theory]
    [InlineData("""root = ScatterChart([])""")]
    [InlineData("""root = ScatterChart([ScatterSeries("A", [])])""")]
    public async Task A_scatter_with_no_points_says_so(string program)
    {
        await using var context = NewContext();
        var cut = Render(context, program + "\n");

        Assert.Contains("No data.", cut.Markup);
    }

    [Fact]
    public async Task A_scatter_keeps_points_near_the_limits_of_a_number_inside_the_plot()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = ScatterChart([ScatterSeries(\"A\", [Point(1e308, -1e308), Point(-1e308, 1e308)])])\n");

        var places = cut.FindAll("circle").SelectMany(point => new[] { point.GetAttribute("cx"), point.GetAttribute("cy") }).ToList();
        Assert.Equal(4, places.Count);
        Assert.All(places, place => Assert.InRange(double.Parse(place!, CultureInfo.InvariantCulture), 0, 320));
    }
}
