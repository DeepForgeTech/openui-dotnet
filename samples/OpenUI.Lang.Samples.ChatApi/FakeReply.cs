namespace OpenUI.Lang.Samples.ChatApi;

/// <summary>What the fake provider answers every chat message with, in the react-ui library.</summary>
internal static class FakeReply
{
    public const string Text = """
        root = Card([header, canned, metrics, chart, note])
        header = CardHeader("Weekly summary", "Made-up numbers")
        canned = TextCallout("warning", "Canned reply", "The fake model did not read your message.")
        metrics = Stack([visits, signups, revenue], "row", "m")
        visits = MetricIndicatorInline("12,480", "Visits", {direction: "up", value: 8})
        signups = MetricIndicatorInline("342", "Sign-ups", {direction: "up", value: 12})
        revenue = MetricIndicatorInline("$18.2K", "Revenue", {direction: "down", value: 3})
        chart = BarChart(["Mon", "Tue", "Wed", "Thu", "Fri"], [Series("Visits", [2100, 2350, 2600, 2700, 2730])])
        note = TextCallout("success", "Streaming works", "This card was parsed by OpenUI.Lang.Core on the server and rendered by react-ui in the browser.")
        """;
}
