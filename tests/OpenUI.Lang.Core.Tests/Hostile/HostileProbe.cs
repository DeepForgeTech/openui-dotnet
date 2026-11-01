using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenUI.Lang.Core.Tests.Conformance;
using OpenUI.Lang.Editing;
using OpenUI.Lang.Library;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Hostile;

/// <summary>
/// Runs one hostile scenario in a child process, so a regression that overflows the stack, exhausts
/// memory or throws on a timer thread fails its test instead of taking down the test host.
/// </summary>
internal static class HostileProbe
{
    private static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(20);

    private static OpenUILibrary Library => ConformanceAdapter.CreateLibrary(FixtureCatalog.LoadLibrary());

    /// <summary>The child: reads the program from stdin, runs the scenario on a pool thread, prints a JSON report.</summary>
    public static int Main(string[] args)
    {
        var program = Console.In.ReadToEnd();
        var report = Task.Run(() => RunScenarioAsync(args[0], program)).GetAwaiter().GetResult();
        Console.Out.WriteLine(report.ToJsonString());
        return 0;
    }

    /// <summary>The parent: fails the calling test unless the child exits cleanly within the budget.</summary>
    public static async Task<JsonObject> SurvivesAsync(string scenario, string program, TimeSpan? budget = null)
    {
        var limit = budget ?? DefaultBudget;
        var start = new ProcessStartInfo(HostPath(), [typeof(HostileProbe).Assembly.Location, scenario])
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        start.Environment["DOTNET_GCHeapHardLimit"] = "40000000";
        start.Environment["DOTNET_DbgEnableMiniDump"] = "0";

        using var child = Process.Start(start)!;
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        await child.StandardInput.WriteAsync(program);
        child.StandardInput.Close();

        using var timeout = new CancellationTokenSource(limit);
        try
        {
            await child.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            child.Kill(entireProcessTree: true);
            Assert.Fail($"{scenario}: still running after {limit.TotalSeconds} s.");
        }

        var stderr = await error;
        Assert.True(child.ExitCode == 0, $"{scenario}: exit {child.ExitCode}: {stderr[..Math.Min(stderr.Length, 400)]}");
        return JsonNode.Parse(await output)!.AsObject();
    }

    public static IEnumerable<string> Codes(JsonObject report) =>
        report["errors"]!.AsArray().Select(code => code!.GetValue<string>());

    public static string Messages(JsonObject report) =>
        string.Join(" | ", report["messages"]!.AsArray().Select(message => message!.GetValue<string>()));

    private static string HostPath()
    {
        var current = Environment.ProcessPath;
        return current is not null && Path.GetFileNameWithoutExtension(current) == "dotnet" ? current : "dotnet";
    }

    private static async Task<JsonObject> RunScenarioAsync(string scenario, string program)
    {
        switch (scenario)
        {
            case "parse":
                var document = new OpenUIParser(Library).Parse(program);
                return Report(document.Errors, document.Statements.Select(s => s.Name), root: null);
            case "evaluate":
                return Evaluate(program);
            case "serialize":
                var parsed = new OpenUIParser(Library).Parse(program);
                var render = OpenUIEvaluator.Evaluate(parsed);
                var text = render.Root is { } tree ? OpenUISerializer.Serialize(tree) : "";
                return Report(render.Errors, [], render.Root, ("length", text.Length));
            case "merge":
                var merged = ProgramMerger.Merge("root = Stack([])", program);
                ProgramMerger.Merge(program, "extra = 1");
                return Report(OpenUI.Lang.Diagnostics.OpenUIErrorList.Empty, merged.Statements, root: null);
            case "edit":
                return await InSessionAsync(program, async session =>
                {
                    session.Load("root = Stack([Text(\"page\")])");
                    var edit = session.ApplyEdit(program);
                    return edit.Statements.AsEnumerable();
                });
            case "session":
                return await InSessionAsync(program, session => LoadAsync(session, program));
            case "timer":
                return await InSessionAsync(program, async session =>
                {
                    var names = await LoadAsync(session, program);
                    await Task.Delay(TimeSpan.FromSeconds(3));
                    return names;
                });
            case "click":
                return await InSessionAsync(program, async session =>
                {
                    var names = await LoadAsync(session, program);
                    await ClickFirstButtonAsync(session);
                    await session.WhenIdleAsync();
                    return names;
                });
            case "click-reader":
                return await InSessionAsync(program, async session =>
                {
                    session.Invalidated += (_, _) => _ = session.Current;
                    var names = await LoadAsync(session, program);
                    await ClickFirstButtonAsync(session);
                    return names;
                });
            case "click-errors":
                return await InSessionAsync(program, async session =>
                {
                    session.ErrorsChanged += (_, _) => { };
                    var names = await LoadAsync(session, program);
                    await ClickFirstButtonAsync(session);
                    return names;
                });
            case "click-events":
                return await InSessionAsync(program, async session =>
                {
                    var names = await LoadAsync(session, program);
                    await ClickFirstButtonAsync(session);
                    return names;
                });
            case "click-all":
                return await InSessionAsync(program, async session =>
                {
                    var names = await LoadAsync(session, program);
                    foreach (var item in session.Current.Root!["children"].Value.TryGetArray(out var items) ? items.Items : [])
                    {
                        if (item.TryGetComponent(out var button) && FindAction(button) is { } found)
                        {
                            await session.RunActionAsync(found.Action, new ActionOrigin(found.Component, null));
                        }
                    }

                    return names;
                });
            case "click-timer":
                return await InSessionAsync(program, async session =>
                {
                    var names = await LoadAsync(session, program);
                    await ClickFirstButtonAsync(session);
                    await Task.Delay(TimeSpan.FromSeconds(3));
                    return names;
                });
            case "deep-tool-result":
                return await InSessionAsync(program, session => LoadAsync(session, program), DeepResultTools());
            case "throwing-contains-timer":
                return await InSessionAsync(
                    program,
                    async session =>
                    {
                        var names = await LoadAsync(session, program);
                        await Task.Delay(TimeSpan.FromSeconds(3));
                        return names;
                    },
                    new ThrowingContainsTools());
            case "throwing-cancel-timer":
                return await InSessionAsync(
                    program,
                    async session =>
                    {
                        var names = await LoadAsync(session, program);
                        await Task.Delay(TimeSpan.FromSeconds(3));
                        return names;
                    },
                    ThrowingCancelTools());
            case "validate-memory":
                return await ValidateRoundsAsync(program);
            case "schema":
                var library = OpenUILibrary.FromSpec(LibrarySpec.Parse(program));
                return new JsonObject { ["errors"] = new JsonArray(), ["components"] = library.ToSpec().Components.Length };
            default:
                throw new ArgumentException($"Unknown scenario {scenario}.");
        }
    }

    private static JsonObject Evaluate(string program)
    {
        var library = Library;
        var document = new OpenUIParser(library).Parse(program);
        var render = OpenUIEvaluator.Evaluate(document);
        if (render.Root is { } root)
        {
            // The walks a host runs on the tree: JSON, a string form and structural equality.
            var again = OpenUIEvaluator.Evaluate(new OpenUIParser(library).Parse(program)).Root;
            _ = ((OuiValue)root).Equals(again);
            _ = Json(root);
        }

        return Report(render.Errors, document.Statements.Select(s => s.Name), render.Root);
    }

    // Each round is a program of its own, separated by a line of dashes, in a session that is disposed
    // before the next. What stays alive after a collection, with the last session still open, is what
    // a host keeps paying for.
    private static async Task<JsonObject> ValidateRoundsAsync(string rounds)
    {
        OpenUISession? last = null;
        foreach (var round in rounds.Split("\n---\n"))
        {
            if (last is not null)
            {
                await last.DisposeAsync();
            }

            last = OpenUISession.Create(Library);
            last.Load(round);
            last.ValidateForm("F");
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var retained = (int)(GC.GetTotalMemory(true) / (1024 * 1024));
        return Report(last!.Current.Errors, [], null, ("retainedMb", retained));
    }

    private static async Task<IEnumerable<string>> LoadAsync(OpenUISession session, string program)
    {
        session.Load(program);
        _ = session.State.Snapshot();
        await Task.Yield();
        return session.Current.Document.Statements.Select(s => s.Name);
    }

    private static async Task ClickFirstButtonAsync(OpenUISession session)
    {
        if (FindAction(session.Current.Root) is { } found)
        {
            await session.RunActionAsync(found.Action, new ActionOrigin(found.Component, null));
        }
    }

    private static (OuiComponent Component, OuiAction Action)? FindAction(OuiComponent? component)
    {
        if (component is null)
        {
            return null;
        }

        foreach (var prop in component.Props)
        {
            if (prop.Value.TryGetAction(out var action))
            {
                return (component, action);
            }

            if (prop.Value.TryGetComponent(out var child) && FindAction(child) is { } inChild)
            {
                return inChild;
            }

            if (prop.Value.TryGetArray(out var items))
            {
                foreach (var item in items)
                {
                    if (item.TryGetComponent(out var element) && FindAction(element) is { } inItem)
                    {
                        return inItem;
                    }
                }
            }
        }

        return null;
    }

    private static async Task<JsonObject> InSessionAsync(string program, Func<OpenUISession, Task<IEnumerable<string>>> work, IOpenUIToolProvider? tools = null)
    {
        var calls = 0;
        ValueTask<OuiValue> Counted(OuiValue args, CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            return ValueTask.FromResult(OuiValue.Null);
        }

        var provider = tools ?? new OpenUITools().Add("t", OpenUIToolEffect.Read, Counted).Add("write", Counted);
        var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = provider, Events = new IgnoredEvents() });
        var names = await work(session);

        // A tool may run until it is cancelled, which disposal below does.
        await Task.WhenAny(session.WhenIdleAsync(), Task.Delay(TimeSpan.FromSeconds(2)));
        var snapshot = session.Current;
        var report = Report(snapshot.Errors, names, snapshot.Root, ("calls", calls));
        await session.DisposeAsync();
        return report;
    }

    private static IOpenUIToolProvider DeepResultTools() => new OpenUITools().Add("t", OpenUIToolEffect.Read, (_, _) =>
    {
        var value = (OuiValue)1.0;
        for (var i = 0; i < 200_000; i++)
        {
            value = OuiArray.Create(value);
        }

        return ValueTask.FromResult(value);
    });

    private static IOpenUIToolProvider ThrowingCancelTools() => new OpenUITools().Add("t", OpenUIToolEffect.Read, async (_, ct) =>
    {
        ct.Register(() => throw new InvalidOperationException("tool callback"));
        await Task.Delay(Timeout.Infinite, ct);
        return OuiValue.Null;
    });

    private static JsonObject Report(OpenUI.Lang.Diagnostics.OpenUIErrorList errors, IEnumerable<string> statements, OuiComponent? root, params (string Key, int Value)[] extra)
    {
        var report = new JsonObject
        {
            ["errors"] = new JsonArray([.. errors.Select(e => (JsonNode)e.Code.Value)]),
            ["errorCount"] = errors.Count,
            ["messages"] = new JsonArray([.. errors.Select(e => (JsonNode)e.Message)]),
            ["statements"] = new JsonArray([.. statements.Select(s => (JsonNode)s)]),
            ["root"] = root is not null,
        };
        foreach (var (key, value) in extra)
        {
            report[key] = value;
        }

        return report;
    }

    private static string Json(OuiComponent root)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            ((OuiValue)root).WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    internal sealed class IgnoredEvents : IOpenUIHostEventHandler
    {
        public ValueTask OnEventAsync(OpenUIHostEvent e, CancellationToken ct) => ValueTask.CompletedTask;
    }

    // A host tool provider that throws from its own lookup, which runs on the refresh timer's thread.
    private sealed class ThrowingContainsTools : IOpenUIToolProvider
    {
        private int _calls;

        public IReadOnlyCollection<string> ToolNames => ["t"];

        public bool Contains(string name) => Interlocked.Increment(ref _calls) == 1 ? true : throw new InvalidOperationException("lookup failed");

        public ValueTask<OuiValue> InvokeAsync(string name, OuiValue args, CancellationToken ct) => ValueTask.FromResult(OuiValue.Null);
    }
}
