using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenUI.Lang.Prompting;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Blazor;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Samples.WidgetPlayground.Data;

/// <summary>
/// The tools custom widgets query, defined once: the handlers the runtime calls and the
/// descriptions the prompt shows the model. Every call waits <see cref="Latency"/> so the loading
/// state is visible.
/// </summary>
public static class PlaygroundTools
{
    /// <summary>The pretend network delay of every call.</summary>
    public static readonly TimeSpan Latency = TimeSpan.FromMilliseconds(400);

    /// <summary>The handlers, by tool name.</summary>
    public static OpenUITools Tools { get; } = new OpenUITools()
        .Add("getSeries", OpenUIToolEffect.Read, GetSeriesAsync)
        .Add("listLocations", OpenUIToolEffect.Read, ListLocationsAsync);

    /// <summary>The same tools as the prompt describes them.</summary>
    public static ImmutableArray<ToolDescriptor> PromptTools { get; } =
    [
        new ToolDescriptor(
            "getSeries",
            "Daily readings for one location and metric, oldest first, ending today.",
            Schema(new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["location"] = new JsonObject { ["type"] = "string" },
                    ["metric"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. SeriesData.Metrics.Select(m => (JsonNode?)m.Id)]) },
                    ["days"] = new JsonObject { ["type"] = "number" },
                },
                ["required"] = new JsonArray("location", "metric", "days"),
            }),
            Schema(new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["unit"] = new JsonObject { ["type"] = "string" },
                    ["labels"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                    ["values"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "number" } },
                    ["points"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject { ["day"] = new JsonObject { ["type"] = "string" }, ["value"] = new JsonObject { ["type"] = "number" } },
                        },
                    },
                    ["latest"] = new JsonObject { ["type"] = "number" },
                    ["min"] = new JsonObject { ["type"] = "number" },
                    ["max"] = new JsonObject { ["type"] = "number" },
                    ["avg"] = new JsonObject { ["type"] = "number" },
                },
            })),
        new ToolDescriptor(
            "listLocations",
            "The location ids readings exist for.",
            Schema(new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }),
            Schema(new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } })),
    ];

    private static JsonElement Schema(JsonObject schema) => JsonSerializer.SerializeToElement(schema);

    /// <exception cref="ArgumentException">An argument is missing or has the wrong type; the query reports a <c>tool-error</c>.</exception>
    private static async ValueTask<OuiValue> GetSeriesAsync(OuiValue args, CancellationToken ct)
    {
        var arguments = args.TryGetObject(out var obj) ? obj : OuiObject.Empty;
        var location = arguments["location"].TryGetString(out var l) ? l : null;
        var metric = arguments["metric"].TryGetString(out var m) ? m : null;
        var days = RuntimeValues.AsNumber(arguments["days"]);
        if (string.IsNullOrEmpty(location) || string.IsNullOrEmpty(metric) || days is null)
        {
            throw new ArgumentException("getSeries needs location (string), metric (string) and days (number).");
        }

        await Task.Delay(Latency, ct);
        var series = SeriesData.Generate(location, metric, (int)days.Value);
        return JsonValues.FromJson(new JsonObject
        {
            ["location"] = series.Location,
            ["metric"] = series.Metric,
            ["unit"] = series.Unit,
            ["labels"] = new JsonArray([.. series.Readings.Select(r => (JsonNode?)r.Day)]),
            ["values"] = new JsonArray([.. series.Readings.Select(r => (JsonNode?)r.Value)]),
            ["points"] = new JsonArray([.. series.Readings.Select(r => (JsonNode?)new JsonObject { ["day"] = r.Day, ["value"] = r.Value })]),
            ["latest"] = series.Latest,
            ["min"] = series.Min,
            ["max"] = series.Max,
            ["avg"] = series.Average,
        });
    }

    private static async ValueTask<OuiValue> ListLocationsAsync(OuiValue args, CancellationToken ct)
    {
        await Task.Delay(Latency, ct);
        return OuiArray.Create(SeriesData.Locations.Select(location => (OuiValue)location.Id).ToImmutableArray());
    }
}
