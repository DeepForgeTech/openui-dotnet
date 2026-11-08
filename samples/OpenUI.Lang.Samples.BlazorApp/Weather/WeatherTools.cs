using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Prompting;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Samples.BlazorApp.Weather;

/// <summary>
/// The tools a program's <c>Query</c> and <c>Mutation</c> call, each one an endpoint of the sample API
/// over HTTP. The arguments become the query string or the body, the result is the response body, and
/// an error response fails the call with its <c>error</c> sentence, which the program can show.
/// </summary>
public sealed class WeatherTools : IOpenUIToolProvider
{
    private readonly HttpClient _http;
    private readonly ToolCallLog _log;
    private readonly OpenUITools _tools;

    /// <summary>Creates the tools.</summary>
    /// <param name="http">A client whose base address is the sample API.</param>
    /// <param name="log">Hears about every call.</param>
    public WeatherTools(HttpClient http, ToolCallLog log)
    {
        _http = http;
        _log = log;
        _tools = new OpenUITools()
            .Add("listLocations", OpenUIToolEffect.Read, (args, ct) => GetAsync("listLocations", "/api/locations", args, [], ct))
            .Add("getForecast", OpenUIToolEffect.Read, (args, ct) => GetAsync("getForecast", "/api/forecast", args, ["location", "days"], ct))
            .Add("listSprayJobs", OpenUIToolEffect.Read, (args, ct) => GetAsync("listSprayJobs", "/api/spray-jobs", args, ["status"], ct))
            .Add("scheduleSprayJob", OpenUIToolEffect.Mutating, (args, ct) => PostAsync("scheduleSprayJob", "/api/spray-jobs", args, ct));
    }

    /// <summary>The same tools as the system prompt describes them.</summary>
    public static ImmutableArray<ToolDescriptor> Descriptors { get; } =
    [
        new("listLocations", "The fields there is a forecast for. Their ids are north-field, river-bend and east-ridge.",
            Schema("""{"type":"object","properties":{}}"""),
            Schema("""{"type":"object","properties":{"locations":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"name":{"type":"string"},"crop":{"type":"string"}}}}}}""")),
        new("getForecast", "A field's forecast, today first. A day is sprayable when wind and rain are both low enough.",
            Schema("""{"type":"object","properties":{"location":{"type":"string"},"days":{"type":"number"}},"required":["location"]}"""),
            Schema("""{"type":"object","properties":{"location":{"type":"string"},"days":{"type":"array","items":{"type":"object","properties":{"date":{"type":"string"},"highF":{"type":"number"},"lowF":{"type":"number"},"rainChance":{"type":"number"},"windMph":{"type":"number"},"sprayable":{"type":"boolean"}}}}}}""")),
        new("listSprayJobs", "The spray jobs on the calendar. The only status is scheduled; a null status lists every job.",
            Schema("""{"type":"object","properties":{"status":{"type":"string"}}}"""),
            Schema("""{"type":"object","properties":{"jobs":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"location":{"type":"string"},"product":{"type":"string"},"date":{"type":"string"},"status":{"type":"string"}}}}}}""")),
        new("scheduleSprayJob", "Schedules a job. Fails on a day with too much wind or rain, or when the field already has a job that day.",
            Schema("""{"type":"object","properties":{"location":{"type":"string"},"product":{"type":"string"},"date":{"type":"string"}},"required":["location","product","date"]}"""),
            Schema("""{"type":"object","properties":{"id":{"type":"string"},"location":{"type":"string"},"product":{"type":"string"},"date":{"type":"string"},"status":{"type":"string"}}}""")),
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<string> ToolNames => _tools.ToolNames;

    /// <inheritdoc />
    public bool Contains(string name) => _tools.Contains(name);

    /// <inheritdoc />
    public OpenUIToolEffect GetEffect(string name) => _tools.GetEffect(name);

    /// <inheritdoc />
    public ValueTask<OuiValue> InvokeAsync(string name, OuiValue args, CancellationToken ct) => _tools.InvokeAsync(name, args, ct);

    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement;

    private ValueTask<OuiValue> GetAsync(string tool, string path, OuiValue args, string[] query, CancellationToken ct)
    {
        var parts = args.TryGetObject(out var fields)
            ? query.Where(name => !fields[name].IsNull).Select(name => $"{name}={Uri.EscapeDataString(fields[name].ToString())}").ToList()
            : [];
        var target = parts.Count > 0 ? $"{path}?{string.Join('&', parts)}" : path;
        return SendAsync(tool, new HttpRequestMessage(HttpMethod.Get, target), args, ct);
    }

    private ValueTask<OuiValue> PostAsync(string tool, string path, OuiValue args, CancellationToken ct) =>
        SendAsync(tool, new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(ToJson(args), Encoding.UTF8, "application/json") }, args, ct);

    private async ValueTask<OuiValue> SendAsync(string tool, HttpRequestMessage request, OuiValue args, CancellationToken ct)
    {
        using (request)
        {
            var line = $"{request.Method} {request.RequestUri}";
            try
            {
                using var response = await _http.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(body);
                    Record(tool, line, args, (int)response.StatusCode, error: null);
                    return OuiValue.FromJson(document.RootElement);
                }

                var message = ErrorSentence(body) ?? $"{line} failed with {(int)response.StatusCode}.";
                Record(tool, line, args, (int)response.StatusCode, message);
                throw new OpenUIToolException(message, OpenUIErrorCode.ToolError);
            }
            catch (Exception error) when (error is HttpRequestException or JsonException)
            {
                var message = $"The weather API at {_http.BaseAddress} did not answer properly. Is it running?";
                Record(tool, line, args, status: null, message);
                throw new OpenUIToolException(message, OpenUIErrorCode.ToolError);
            }
        }
    }

    private void Record(string tool, string line, OuiValue args, int? status, string? error) =>
        _log.Add(new ToolCall(DateTimeOffset.Now, tool, line, ToJson(args), status, error));

    // Errors are shaped { error, fields }; anything else on an error status is not the API talking.
    private static string? ErrorSentence(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("error", out var error) ? error.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ToJson(OuiValue value)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            value.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
