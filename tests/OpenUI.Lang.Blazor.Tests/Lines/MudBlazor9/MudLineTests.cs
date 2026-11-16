using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Services;
using OpenUI.Lang.Blazor.Mud;

namespace OpenUI.Lang.Blazor.Tests.Mud;

/// <summary>What the MudBlazor 9 line draws with MudChart, and the parameters MudBlazor 9 names its own way.</summary>
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

        var browser = context.JSInterop.SetupModule(ThemeModeState.ModulePath);
        browser.Setup<StoredThemeMode?>("start", _ => true).SetResult(new StoredThemeMode(null, false));
        browser.SetupVoid("paint", _ => true).SetVoidResult();
        var provider = context.Render<ThemeModeProvider>();

        provider.WaitForAssertion(() => provider.FindComponent<MudThemeProvider>());

        Assert.False(provider.FindComponent<MudThemeProvider>().Instance.GetState(mud => mud.ObserveSystemDarkModeChange));
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

    [Theory]
    [InlineData("PieChart", ChartType.Pie, "")]
    [InlineData("PieChart", ChartType.Donut, ", \"donut\"")]
    public async Task A_pie_draws_a_slice_for_each_label(string component, ChartType type, string variant)
    {
        await using var context = NewContext();
        var cut = Render(context, $"root = {component}([\"Corn\", \"Wheat\", \"Soy\"], [50, 30, 20]{variant})\n");

        var chart = cut.FindComponent<MudChart<double>>().Instance;
        Assert.Equal(type, chart.ChartType);
        Assert.Equal(["Corn", "Wheat", "Soy"], chart.ChartLabels);
        Assert.Equal([50d, 30, 20], chart.ChartSeries.Single().Data.Values);
        Assert.Equal(3, cut.FindAll("path.mud-chart-serie").Count);
    }

    [Fact]
    public async Task A_radar_draws_each_series_around_the_labels()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = RadarChart([\"Speed\", \"Reach\", \"Cost\"], [Series(\"Us\", [4, 3, 5]), Series(\"Them\", [2, 5, 3])])\n");

        var chart = cut.FindComponent<MudChart<double>>().Instance;
        Assert.Equal(ChartType.Radar, chart.ChartType);
        Assert.Equal(["Speed", "Reach", "Cost"], chart.ChartLabels);
        Assert.Equal(["Us", "Them"], chart.ChartSeries.Select(series => series.Name));
        Assert.Contains("Cost", cut.Markup);
    }

    [Fact]
    public async Task A_scatter_draws_each_dataset_and_leaves_out_a_point_that_is_not_a_number()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = ScatterChart([ScatterSeries(\"Plot A\", [Point(1, 2), Point(3, 4, 9), Point(2e308, 1)]), ScatterSeries(\"Plot B\", [Point(2, 5)])], \"Rain\", \"Yield\")\n");

        var chart = cut.FindComponent<MudChart<double>>().Instance;
        Assert.Equal(ChartType.ScatterPlot, chart.ChartType);
        Assert.Equal(["Plot A", "Plot B"], chart.ChartSeries.Select(series => series.Name));
        Assert.Equal([2, 1], chart.ChartSeries.Select(series => series.Data.Count));
        Assert.Equal(3, cut.FindAll("circle.mud-chart-point[opacity!='0'], circle.mud-chart-point:not([opacity])").Count);
        Assert.Contains("Rain", cut.Markup);
        Assert.Contains("Yield", cut.Markup);
    }

    [Fact]
    public async Task An_area_chart_draws_its_series_as_lines()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = AreaChart([\"Mon\", \"Tue\"], [Series(\"Rain\", [3, 5])], \"natural\")\n");

        var chart = cut.FindComponent<MudChart<double>>().Instance;
        Assert.Equal(ChartType.Line, chart.ChartType);
        Assert.Equal(["Rain"], chart.ChartSeries.Select(series => series.Name));
    }
}
