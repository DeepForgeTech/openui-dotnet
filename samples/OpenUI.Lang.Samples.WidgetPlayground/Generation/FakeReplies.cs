using Microsoft.Extensions.AI;
using OpenUI.Lang.Samples.WidgetPlayground.Catalog;

namespace OpenUI.Lang.Samples.WidgetPlayground.Generation;

/// <summary>
/// What the fake provider answers, so the prompt loop can be tried without a model: a whole program
/// for a new prompt, and a patch for a refine. A refine that mentions humidity adds a humidity tile;
/// any other refine turns the chart into bars.
/// </summary>
public static class FakeReplies
{
    private const string Widget = """
        root = Card("Conditions at " + $location, [heading, metrics, chart])
        heading = Text("Last " + $days + " days of " + $metric, "heading")
        metrics = Stack([latest, average], "row")
        latest = Metric("Latest", series.latest, series.unit)
        average = Metric("Average", series.avg, series.unit)
        chart = Chart(series.values, series.labels, "line")
        series = Query("getSeries", {location: $location, metric: $metric, days: $days}, {unit: "", labels: [], values: [], latest: 0, avg: 0})
        """;

    private const string WidgetBars = """
        chart = Chart(series.values, series.labels, "bar")
        """;

    private const string WidgetHumidity = """
        metrics = Stack([latest, average, humidity], "row")
        humidity = Metric("Humidity", humiditySeries.latest, humiditySeries.unit)
        humiditySeries = Query("getSeries", {location: $location, metric: "humidity", days: $days}, {unit: "", latest: 0})
        """;

    private const string Page = """
        root = Page("Generated overview", [picker, range, tile, chart])
        $location = "north"
        $metric = "temperature"
        $days = 14
        picker = LocationPicker($location, 4, 1)
        range = DateRangePicker($days, 8, 1)
        tile = MetricTile($location, $metric, $days, 4, 2)
        chart = TimeSeriesChart($location, $metric, $days, 8, 2)
        """;

    private const string PageMap = """
        root = Page("Generated overview", [picker, range, tile, chart, map])
        map = MapView($location, 12, 3)
        """;

    /// <summary>Picks the canned reply for a conversation from either library.</summary>
    public static string For(IReadOnlyList<ChatMessage> messages, PlaygroundLibraries libraries)
    {
        var message = messages.LastOrDefault()?.Text ?? "";
        var systemPrompt = messages.FirstOrDefault(item => item.Role == ChatRole.System)?.Text;
        var refine = message.StartsWith(ProgramDesigner.RefineLead, StringComparison.Ordinal);
        if (systemPrompt == libraries.Page.Prompt || systemPrompt == libraries.Page.EditPrompt)
        {
            return refine ? PageMap : Page;
        }

        if (!refine) return Widget;
        return Request(message).Contains("humidity", StringComparison.OrdinalIgnoreCase) ? WidgetHumidity : WidgetBars;
    }

    // Only the request after the program counts, or every program that mentions humidity would match.
    private static string Request(string message)
    {
        var at = message.LastIndexOf(ProgramDesigner.RequestLead, StringComparison.Ordinal);
        return at < 0 ? message : message[at..];
    }
}
