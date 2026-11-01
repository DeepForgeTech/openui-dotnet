using System.Text;
using System.Text.Json;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Samples.ChatApi.Tests;

/// <summary>
/// The React sample's fixed program (samples/react-chat/src/samples/spray-planner.oui), run on Core
/// against the weather endpoints. The program is written for the react-ui library, so these tests
/// read the same spec file the chat API does.
/// </summary>
public sealed class SprayPlannerProgramTests
{
    private static readonly string ReactChat = Path.Combine(RepositoryRoot(), "samples", "react-chat", "src");
    private static readonly OpenUILibrary Library = OpenUILibrary.FromSpec(LibrarySpec.Parse(File.ReadAllText(Path.Combine(ReactChat, "generated", "spec.json"))));
    private static readonly string Program = File.ReadAllText(Path.Combine(ReactChat, "samples", "spray-planner.oui"));

    [Fact]
    public void The_program_has_no_errors_on_the_react_ui_library()
    {
        var report = new OpenUIValidator(Library).Validate(Program);

        Assert.Empty(report.Errors);
        Assert.Equal("Stack", report.Render.Root?.Type);
    }

    [Fact]
    public async Task Queries_fill_in_and_a_mutation_changes_what_the_next_fetch_returns()
    {
        await using var host = await WeatherHost.StartAsync();
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = WeatherTools(host.Client) });
        session.Load(Program);
        await Eventually(() => session.Current.Queries.Count == 3 && session.Current.Queries.Values.All(query => !query.IsLoading));

        Assert.Equal(7, Items(session, "forecast", "days").Count);
        Assert.Equal(2, Items(session, "jobs", "jobs").Count);

        session.State.Set("$date", host.FirstDay("north-field", sprayable: true));
        var outcome = await session.RunActionAsync(ActionOf(session, "Schedule spray"), new ActionOrigin(null, null));

        Assert.Equal(ActionOutcomeKind.Completed, outcome.Kind);
        Assert.Equal(MutationState.Success, session.GetMutationStatus("schedule").State);
        await Eventually(() => Items(session, "jobs", "jobs").Count == 3);
        Assert.Equal("", session.State.Get("$date").ToString());
    }

    [Fact]
    public async Task A_rejected_job_stops_the_action_and_the_error_reaches_the_program()
    {
        await using var host = await WeatherHost.StartAsync();
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = WeatherTools(host.Client) });
        session.Load(Program);
        await Eventually(() => session.Current.Queries.Count == 3 && session.Current.Queries.Values.All(query => !query.IsLoading));

        var badDay = host.FirstDay("north-field", sprayable: false);
        session.State.Set("$date", badDay);
        var outcome = await session.RunActionAsync(ActionOf(session, "Schedule spray"), new ActionOrigin(null, null));

        Assert.Equal(ActionOutcomeKind.Stopped, outcome.Kind);
        var status = session.GetMutationStatus("schedule");
        Assert.Equal(MutationState.Error, status.State);
        Assert.Contains("not a spraying day", status.Error);
        Assert.Equal(badDay, session.State.Get("$date").ToString());
        Assert.Equal(2, Items(session, "jobs", "jobs").Count);
        Assert.Contains(session.Current.Errors, error => error.Source == OpenUIErrorSource.Mutation);
    }

    // The same four tools weather-tools.js builds for the browser, over the in-process API.
    private static OpenUITools WeatherTools(HttpClient client) => new OpenUITools()
        .Add("listLocations", OpenUIToolEffect.Read, (args, ct) => CallAsync(client, HttpMethod.Get, "/api/locations", args, ct))
        .Add("getForecast", OpenUIToolEffect.Read, (args, ct) => CallAsync(client, HttpMethod.Get, $"/api/forecast?location={Arg(args, "location")}&days={Arg(args, "days")}", args, ct))
        .Add("listSprayJobs", OpenUIToolEffect.Read, (args, ct) => CallAsync(client, HttpMethod.Get, "/api/spray-jobs", args, ct))
        .Add("scheduleSprayJob", (args, ct) => CallAsync(client, HttpMethod.Post, "/api/spray-jobs", args, ct));

    private static string Arg(OuiValue args, string name) =>
        args.TryGetObject(out var obj) ? Uri.EscapeDataString(obj[name].ToString()) : "";

    private static async ValueTask<OuiValue> CallAsync(HttpClient client, HttpMethod method, string path, OuiValue args, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (method == HttpMethod.Post)
        {
            request.Content = new StringContent(JsonText(args), Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request, ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return response.IsSuccessStatusCode
            ? OuiValue.FromJson(body.RootElement)
            : throw new OpenUIToolException(body.RootElement.GetProperty("error").GetString()!, OpenUIErrorCode.ToolError);
    }

    private static string JsonText(OuiValue value)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            value.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static OuiArray Items(OpenUISession session, string query, string field) =>
        session.Current.Queries[query].Data.TryGetObject(out var data) && data[field].TryGetArray(out var items) ? items : OuiArray.Empty;

    // The action of the button with this label, found in the rendered tree.
    private static OuiAction ActionOf(OpenUISession session, string label)
    {
        var button = Walk(session.Current.Root ?? throw new InvalidOperationException("Nothing rendered.")).First(component => component.Type == "Button" && component["label"].Value.ToString() == label);
        Assert.True(button["action"].Value.TryGetAction(out var action));
        return action;
    }

    private static IEnumerable<OuiComponent> Walk(OuiValue value)
    {
        if (value.TryGetComponent(out var component))
        {
            yield return component;
            foreach (var child in component.Props.SelectMany(prop => Walk(prop.Value)))
            {
                yield return child;
            }
        }
        else if (value.TryGetArray(out var items))
        {
            foreach (var child in items.SelectMany(Walk))
            {
                yield return child;
            }
        }
    }

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not hold within 10 seconds.");
            await Task.Delay(20);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenUI.Lang.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("OpenUI.Lang.sln was not found above the test assembly.");
    }
}
