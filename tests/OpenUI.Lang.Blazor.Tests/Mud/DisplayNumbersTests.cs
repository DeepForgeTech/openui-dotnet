using System.Text.Json;
using OpenUI.Lang.Values;

using OpenUI.Lang.Blazor.Mud;

namespace OpenUI.Lang.Blazor.Tests.Mud;

/// <summary>What any kit's charts, metrics and icons read from a program, with no kit involved.</summary>
public sealed class DisplayNumbersTests
{
    private static OuiValue Json(string json) => OuiValue.FromJson(JsonDocument.Parse(json).RootElement);

    private static IReadOnlyList<OuiValue> Items(string json) => Json(json).TryGetArray(out var items) ? items : OuiArray.Empty;

    [Theory]
    [InlineData("3", 3d)]
    [InlineData("\"4.5\"", 4.5d)]
    [InlineData("\"Infinity\"", 0d)]
    [InlineData("\"NaN\"", 0d)]
    [InlineData("\"1e999\"", 0d)]
    [InlineData("null", 0d)]
    [InlineData("\"wet\"", 0d)]
    public void A_chart_number_is_finite_or_zero(string json, double expected) =>
        Assert.Equal(expected, ChartValues.Finite(Json(json)));

    [Fact]
    public void A_series_has_one_value_per_label()
    {
        Assert.Equal([1d, 0d, 0d], ChartValues.Values(Items("[1]"), 3));
        Assert.Equal([1d], ChartValues.Values(Items("[1, 2, 3]"), 1));
        Assert.Equal([0d, 2d], ChartValues.Magnitudes(Items("[-1, 2]"), 2));
    }

    [Fact]
    public void A_whole_is_never_negative_or_infinite()
    {
        Assert.Equal(0, ChartValues.Peak([]));
        Assert.Equal(0, ChartValues.Peak([-5]));
        Assert.Equal(7, ChartValues.Peak([-5, 7, 2]));
        Assert.Equal(0, ChartValues.Total([double.MaxValue, double.MaxValue]));
        Assert.Equal(0, ChartValues.StackPeak([[double.MaxValue], [double.MaxValue]], 1));
        Assert.Equal(5, ChartValues.StackPeak([[1, 4], [-3, 1]], 2));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 1)]
    [InlineData(12, 3)]
    [InlineData(100, 20)]
    [InlineData(1e308, int.MaxValue)]
    public void A_tick_step_gives_about_five_grid_lines_and_never_less_than_one(double extent, int step)
    {
        Assert.Equal(step, ChartValues.TickStep(extent));
    }

    [Theory]
    [InlineData(1, 4, "25%")]
    [InlineData(9, 4, "100%")]
    [InlineData(-1, 4, "0%")]
    [InlineData(1, 0, "0%")]
    [InlineData(1, double.PositiveInfinity, "0%")]
    public void A_percent_stays_between_nothing_and_everything(double value, double whole, string expected) =>
        Assert.Equal(expected, ChartValues.Percent(value, whole));

    [Theory]
    [InlineData("{\"direction\": \"up\", \"value\": 12}", true, "+12%")]
    [InlineData("{\"direction\": \"down\", \"value\": 3.5}", false, "-3.5%")]
    public void A_trend_says_which_way_and_how_much(string json, bool up, string text) =>
        Assert.Equal(new MetricTrend(up, text), DisplayValues.Trend(Json(json)));

    [Theory]
    [InlineData("{\"direction\": \"sideways\", \"value\": 1}")]
    [InlineData("{\"direction\": \"up\", \"value\": \"Infinity\"}")]
    [InlineData("{\"direction\": \"up\"}")]
    [InlineData("null")]
    public void A_malformed_trend_is_nothing(string json) => Assert.Null(DisplayValues.Trend(Json(json)));

    [Fact]
    public void An_image_with_no_object_is_skipped_and_one_with_no_alt_is_named_for_its_place() =>
        Assert.Equal(
            [new GalleryImage("a.png", "Gallery image 1", ""), new GalleryImage("b.png", "Field", "North")],
            DisplayValues.GalleryImages(Items("""["not an image", {"src": "a.png"}, {"src": "b.png", "alt": "Field", "details": "North"}]""")));
}
