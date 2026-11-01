using System.Text.Json;
using OpenUI.Lang.Core.Tests.Conformance;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;
using Xunit;

namespace OpenUI.Lang.Core.Tests.Runtime;

public class OpenUISessionTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private static OpenUILibrary Library { get; } = ConformanceAdapter.CreateLibrary(FixtureCatalog.LoadLibrary());

    private static OuiComponent Child(RenderSnapshot snapshot, int index)
    {
        Assert.True(snapshot.Root!["children"].Value.TryGetArray(out var children));
        Assert.True(children[index].TryGetComponent(out var child));
        return child;
    }

    private static Task Run(OpenUISession session, OuiComponent button, string? formName = null)
    {
        Assert.True(button["action"].Value.TryGetAction(out var action));
        return session.RunActionAsync(action, new ActionOrigin(button, formName)).AsTask();
    }

    private static (OpenUITools Tools, List<string> Calls) RecordingTools(params string[] names)
    {
        var calls = new List<string>();
        var tools = new OpenUITools();
        foreach (var name in names)
        {
            tools.Add(name, OpenUIToolEffect.Read, (args, _) =>
            {
                lock (calls)
                {
                    calls.Add(name + ConformanceAdapter.Summarize(args)!.ToJsonString());
                }

                return ValueTask.FromResult<OuiValue>(args);
            });
        }

        return (tools, calls);
    }

    [Fact]
    public async Task A_default_from_a_truncated_statement_is_replaced_when_the_full_one_arrives()
    {
        await using var session = OpenUISession.Create(Library);
        session.BeginStream();
        session.Append("$title = \"Hel");
        Assert.Equal((OuiValue)"Hel", session.State.Get("$title"));
        session.Append("lo\"\nroot = Stack([])\n");
        Assert.Equal((OuiValue)"Hello", session.State.Get("$title"));
        session.EndStream();
        Assert.Equal((OuiValue)"Hello", session.State.Get("$title"));
    }

    [Fact]
    public async Task A_value_the_user_produced_survives_streaming_and_parsing_again()
    {
        await using var session = OpenUISession.Create(Library);
        session.BeginStream();
        session.Append("$title = \"Hel");
        session.State.Set("$title", "mine");
        session.Append("lo\"\nroot = Stack([])\n");
        session.EndStream();
        Assert.Equal((OuiValue)"mine", session.State.Get("$title"));

        session.Load("$title = \"other\"\nroot = Stack([])\n");
        Assert.Equal((OuiValue)"mine", session.State.Get("$title"));
    }

    [Fact]
    public async Task The_last_definition_of_a_state_variable_is_its_default_as_in_a_batch_parse()
    {
        await using var session = OpenUISession.Create(Library);
        session.Load("$x = 1\n$x = 2\nroot = Stack([])\n");
        Assert.Equal((OuiValue)2.0, session.State.Get("$x"));
    }

    [Fact]
    public async Task Persisted_state_and_forms_apply_over_the_defaults()
    {
        var forms = FormStateSnapshot.Empty.With("contact", "email", new FormField("a@b.co", "input"));
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions
        {
            PersistedState = new Dictionary<string, OuiValue> { ["$x"] = 9.0 },
            PersistedForms = forms,
        });
        session.Load("$x = 1\nroot = Stack([])\n");
        Assert.Equal((OuiValue)9.0, session.State.Get("$x"));
        Assert.Equal((OuiValue)"a@b.co", session.Forms.Get("contact", "email"));
        Assert.Equal(forms, session.Forms.Snapshot());
    }

    [Fact]
    public async Task State_keys_without_a_dollar_are_refused()
    {
        await using var session = OpenUISession.Create(Library);
        Assert.Throws<ArgumentException>(() => session.State.Set("x", 1.0));
    }

    [Fact]
    public async Task Reset_restores_the_default_and_null_when_there_is_none()
    {
        await using var session = OpenUISession.Create(Library);
        session.Load("$x = 1\nroot = Stack([Text($y + \"\")])\n");
        session.State.Set("$x", 5.0);
        session.State.Set("$y", 5.0);
        session.State.Reset("$x", "$y");
        Assert.Equal((OuiValue)1.0, session.State.Get("$x"));
        Assert.True(session.State.Get("$y").IsNull);
    }

    [Fact]
    public async Task Queries_wait_for_the_end_of_the_stream()
    {
        var (tools, calls) = RecordingTools("list");
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools });
        session.BeginStream();
        session.Append("root = Stack([])\nq = Query(\"list\", {}, {})\n\n");
        await session.WhenIdleAsync();
        Assert.Empty(calls);
        session.EndStream();
        await session.WhenIdleAsync();
        Assert.Single(calls);
    }

    [Fact]
    public async Task A_query_refetches_for_a_literal_variable_and_not_through_a_reference()
    {
        var (tools, calls) = RecordingTools("list");
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools });
        session.Load("$a = 1\n$b = 1\nviaB = $b\nroot = Stack([])\nq = Query(\"list\", {a: $a, b: viaB}, {})\n");
        await session.WhenIdleAsync();
        Assert.Single(calls);

        session.State.Set("$b", 2.0);
        await session.WhenIdleAsync();
        Assert.Single(calls);

        session.State.Set("$a", 2.0);
        await session.WhenIdleAsync();
        Assert.Equal(2, calls.Count);
        Assert.Contains("\"b\":2", calls[1]);

        session.State.Set("$a", 2.0);
        await session.WhenIdleAsync();
        Assert.Equal(2, calls.Count);
    }

    [Fact]
    public async Task A_result_for_stale_arguments_is_discarded_and_the_previous_one_stays_visible()
    {
        var gates = new Dictionary<string, TaskCompletionSource<OuiValue>>
        {
            ["1"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ["2"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ["3"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var tools = new OpenUITools().Add("list", OpenUIToolEffect.Read, (args, _) =>
        {
            Assert.True(args.TryGetObject(out var fields));
            return new ValueTask<OuiValue>(gates[OpenUI.Lang.Values.Ecma.ToString(fields["v"])].Task);
        });
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools });
        session.Load("$v = 1\nroot = Stack([Text(q.title)])\nq = Query(\"list\", {v: $v}, {title: \"default\"})\n");

        session.State.Set("$v", 2.0);
        Assert.Equal((OuiValue)"default", Child(session.Current, 0)["text"].Value);

        gates["2"].SetResult(new OuiObject.Builder().Set("title", "second").ToObject());
        await session.WhenIdleAsync().WaitAsync(Budget);
        Assert.Equal((OuiValue)"second", Child(session.Current, 0)["text"].Value);

        // The first fetch was cancelled by the second, so its late result has nowhere to land.
        gates["1"].SetResult(new OuiObject.Builder().Set("title", "first").ToObject());
        await session.WhenIdleAsync().WaitAsync(Budget);
        Assert.Equal((OuiValue)"second", Child(session.Current, 0)["text"].Value);

        session.State.Set("$v", 3.0);
        var status = session.GetQueryStatus("q");
        Assert.True(status.IsRefetching);
        Assert.False(status.IsLoading);
        Assert.Equal((OuiValue)"second", Child(session.Current, 0)["text"].Value);
        gates["3"].SetResult(OuiValue.Null);
        await session.WhenIdleAsync().WaitAsync(Budget);
    }

    [Fact]
    public async Task A_missing_tool_keeps_the_defaults_and_the_hint_lists_the_tools()
    {
        var (tools, _) = RecordingTools("b", "a");
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools });
        session.Load("root = Stack([Text(q.title)])\nq = Query(\"List\", {}, {title: \"default\"})\n");
        await session.WhenIdleAsync();

        var error = Assert.Single(session.Current.Errors);
        Assert.Equal(OpenUIErrorCode.ToolNotFound, error.Code);
        Assert.Equal(OpenUIErrorSource.Query, error.Source);
        Assert.Equal("List", error.ToolName);
        Assert.Equal("Available tools: a, b", error.Hint);
        Assert.Equal((OuiValue)"default", Child(session.Current, 0)["text"].Value);
    }

    [Fact]
    public async Task A_provider_can_choose_mcp_error_and_a_query_keeps_its_last_good_data()
    {
        var fail = false;
        var tools = new OpenUITools().Add("list", OpenUIToolEffect.Read, (args, _) => fail
            ? throw new OpenUIToolException("gone", OpenUIErrorCode.McpError)
            : ValueTask.FromResult<OuiValue>(new OuiObject.Builder().Set("title", "good").ToObject()));
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools });
        session.Load("$v = 1\nroot = Stack([Text(q.title)])\nq = Query(\"list\", {v: $v}, {title: \"default\"})\n");
        await session.WhenIdleAsync();

        fail = true;
        session.State.Set("$v", 2.0);
        await session.WhenIdleAsync();
        Assert.Contains(session.Current.Errors, e => e.Code == OpenUIErrorCode.McpError);
        Assert.Equal((OuiValue)"good", Child(session.Current, 0)["text"].Value);

        fail = false;
        session.State.Set("$v", 3.0);
        await session.WhenIdleAsync();
        Assert.Empty(session.Current.Errors);
    }

    [Fact]
    public async Task A_refresh_interval_runs_the_query_again_and_stops_on_disposal()
    {
        var time = new ManualTime();
        var (tools, calls) = RecordingTools("list");
        var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools, Time = time });
        session.Load("root = Stack([])\nq = Query(\"list\", {}, {}, 30)\n");
        await session.WhenIdleAsync();
        Assert.Single(calls);
        Assert.Equal(TimeSpan.FromSeconds(30), time.Due);

        time.Fire();
        await session.WhenIdleAsync();
        Assert.Equal(2, calls.Count);

        await session.DisposeAsync();
        time.Fire();
        Assert.Equal(2, calls.Count);
    }

    [Fact]
    public async Task Reserved_fetch_keys_reach_the_host_and_never_an_expression()
    {
        var (tools, _) = RecordingTools("list");
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools });
        session.Load("root = Stack([Text(q.__openui_loading + \"\")])\nq = Query(\"list\", {a: 1}, {})\n");
        await session.WhenIdleAsync();
        Assert.Equal((OuiValue)"", Child(session.Current, 0)["text"].Value);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            session.GetQueryStatus("q").WriteHostJson(writer);
        }

        using var json = JsonDocument.Parse(buffer.ToArray());
        Assert.Equal(1, json.RootElement.GetProperty("a").GetInt32());
        Assert.False(json.RootElement.GetProperty("__openui_loading").GetBoolean());
        Assert.False(json.RootElement.GetProperty("__openui_refetching").GetBoolean());
        Assert.Equal(0, json.RootElement.GetProperty("__openui_errors").GetArrayLength());
    }

    [Fact]
    public async Task A_mutation_never_runs_on_load_rejects_a_second_run_and_reports_its_status()
    {
        var gate = new TaskCompletionSource<OuiValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var tools = new OpenUITools().Add("save", OpenUIToolEffect.Read, (_, _) =>
        {
            calls++;
            return new ValueTask<OuiValue>(gate.Task);
        });
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools });
        session.Load("root = Stack([go, Text(m.status)])\nm = Mutation(\"save\", {})\ngo = Button(\"Go\", @Run(m))\n");
        await session.WhenIdleAsync();
        Assert.Equal(0, calls);
        Assert.Equal((OuiValue)"idle", Child(session.Current, 1)["text"].Value);

        var button = Child(session.Current, 0);
        Assert.True(button["action"].Value.TryGetAction(out var action));
        var first = session.RunActionAsync(action, new ActionOrigin(button, null)).AsTask();
        Assert.Equal((OuiValue)"loading", Child(session.Current, 1)["text"].Value);

        var second = await session.RunActionAsync(action, new ActionOrigin(button, null));
        Assert.Equal(ActionOutcomeKind.Rejected, second.Kind);
        Assert.Equal(1, calls);

        gate.SetResult("done");
        Assert.Equal(ActionOutcomeKind.Completed, (await first.WaitAsync(Budget)).Kind);
        Assert.Equal((OuiValue)"success", Child(session.Current, 1)["text"].Value);
        Assert.Equal((OuiValue)"done", session.GetMutationStatus("m").Data);
    }

    [Fact]
    public async Task A_mutation_reads_the_state_set_earlier_in_its_plan()
    {
        var (tools, calls) = RecordingTools("save");
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools });
        session.Load("$x = 1\nroot = Stack([go])\nm = Mutation(\"save\", {x: $x})\ngo = Button(\"Go\", Action([@Set($x, 2), @Run(m)]))\n");
        await Run(session, Child(session.Current, 0));
        Assert.Equal("save{\"x\":2}", Assert.Single(calls));
    }

    [Fact]
    public async Task Set_evaluates_its_value_when_it_runs_with_the_loop_values_it_captured()
    {
        await using var session = OpenUISession.Create(Library);
        session.Load("$picked = 0\n$n = 1\nroot = Stack(@Each([10, 20], \"v\", Button(\"Pick\", @Set($picked, v + $n))))\n");
        var second = Child(session.Current, 1);

        session.State.Set("$n", 2.0);
        await Run(session, second);
        Assert.Equal((OuiValue)22.0, session.State.Get("$picked"));
    }

    [Fact]
    public async Task A_custom_action_with_an_invalid_argument_does_nothing()
    {
        var events = new List<OpenUIHostEvent>();
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Events = new Recording(events) });
        session.Load("$n = 5\nroot = Stack([bad, good])\nbad = Button(\"Bad\", @CopyToClipboard($n))\ngood = Button(\"Good\", @CopyToClipboard(\"ok\"))\n");
        await Run(session, Child(session.Current, 0));
        Assert.Empty(events);
        Assert.Contains(session.Current.Errors, e => e.Code == OpenUIErrorCode.RuntimeError);
        await Run(session, Child(session.Current, 1));
        Assert.Equal("CopyToClipboard", Assert.Single(events).Type);
    }

    [Fact]
    public async Task Append_only_invalidates_and_Current_evaluates_once_per_revision()
    {
        var evaluations = 0;
        var library = OpenUILibrary.FromSpec(Library.ToSpec(), implement => implement.Function("Upper", (in FunctionArguments args) =>
        {
            Interlocked.Increment(ref evaluations);
            return "X";
        }));
        await using var session = OpenUISession.Create(library);
        var invalidated = 0;
        session.Invalidated += (_, _) => invalidated++;
        session.BeginStream();
        session.Append("x = @Upper(\"a\")\nroot = Stack([Text(x), Text(x)])\n");
        session.Append("\n");
        Assert.Equal(0, evaluations);
        Assert.True(invalidated >= 2);

        var snapshot = session.Current;
        Assert.Equal(1, evaluations);
        Assert.Same(snapshot, session.Current);
        Assert.Equal(1, evaluations);
    }

    [Fact]
    public async Task A_snapshot_equals_a_fresh_full_evaluation_after_every_change()
    {
        await using var session = OpenUISession.Create(Library);
        var parser = new OpenUIParser(Library);
        const string Program = "$a = 1\n$b = \"x\"\nsum = $a + 1\nroot = Stack([Text($b + sum), Metric(\"m\", sum)])\n";
        session.Load(Program);
        foreach (var (key, value) in new (string, OuiValue)[] { ("$a", 2.0), ("$b", "y"), ("$a", 7.0) })
        {
            session.State.Set(key, value);
            var oracle = OpenUIEvaluator.Evaluate(parser.Parse(Program), new EvaluationInputs(session.State.Snapshot()));
            Assert.True(((OuiValue)oracle.Root).Equals(session.Current.Root));
        }
    }

    [Fact]
    public async Task ErrorsChanged_follows_the_emission_rules()
    {
        await using var session = OpenUISession.Create(Library);
        var lists = new List<OpenUIErrorList>();
        session.ErrorsChanged += (_, errors) => lists.Add(errors);

        session.BeginStream();
        Assert.Empty(lists);

        session.Append("root = Stack([Nope(\"x\")])\n");
        Assert.Empty(lists);

        session.EndStream();
        Assert.Equal("unknown-component", Assert.Single(Assert.Single(lists)).Code.Value);

        session.State.Set("$x", 1.0);
        Assert.Single(lists);

        session.BeginStream();
        Assert.Equal(2, lists.Count);
        Assert.Empty(lists[1]);

        session.Append("root = Stack([])\n");
        session.EndStream();
        Assert.Equal(2, lists.Count);
    }

    [Fact]
    public async Task A_handler_that_throws_does_not_reach_the_commit()
    {
        await using var session = OpenUISession.Create(Library);
        var reached = false;
        session.Invalidated += (_, _) => throw new InvalidOperationException("host");
        session.Invalidated += (_, _) => reached = true;
        session.Load("root = Stack([])\n");
        Assert.True(reached);
    }

    [Fact]
    public async Task A_render_error_is_reported_once_and_cleared_by_the_next_stream()
    {
        await using var session = OpenUISession.Create(Library);
        session.Load("root = Stack([])\n");
        session.ReportRenderError(session.Current.Root!, new InvalidOperationException("bad"));
        session.ReportRenderError(session.Current.Root!, new InvalidOperationException("bad"));
        var error = Assert.Single(session.Current.Errors);
        Assert.Equal(OpenUIErrorCode.RenderError, error.Code);
        Assert.Equal("Stack", error.Component);

        session.BeginStream();
        Assert.Empty(session.Current.Errors);
    }

    [Fact]
    public async Task ApplyEdit_keeps_state_and_does_not_refetch_an_unchanged_query()
    {
        var (tools, calls) = RecordingTools("list");
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools });
        session.Load("$x = 1\nroot = Stack([a])\na = Text(\"A\")\nq = Query(\"list\", {v: 1}, {})\n");
        await session.WhenIdleAsync();
        session.State.Set("$x", 5.0);

        var merged = session.ApplyEdit("a = Text(\"B\")\n$x = null\n");
        await session.WhenIdleAsync();

        Assert.Equal((OuiValue)"B", Child(session.Current, 0)["text"].Value);
        Assert.Equal("$x", Assert.Single(merged.Deleted));
        Assert.Equal((OuiValue)5.0, session.State.Get("$x"));
        Assert.Single(calls);

        session.ApplyEdit("q = Query(\"list\", {v: 2}, {})\n");
        await session.WhenIdleAsync();
        Assert.Equal(2, calls.Count);
    }

    [Fact]
    public async Task Load_reads_a_stored_message_and_ApplyEdit_merges_into_its_content()
    {
        await using var session = OpenUISession.Create(Library);
        session.Load("]]>openui:content?library=a%401\nroot = Stack([a])\na = Text(\"A\")\n]]>openui:end\n");
        Assert.Equal((OuiValue)"A", Child(session.Current, 0)["text"].Value);
        session.ApplyEdit("]]>openui:content\na = Text(\"B\")\n]]>openui:end");
        Assert.Equal((OuiValue)"B", Child(session.Current, 0)["text"].Value);
    }

    [Fact]
    public async Task WatchAsync_yields_the_latest_view_and_completes_on_disposal()
    {
        var session = OpenUISession.Create(Library);
        var seen = new List<long>();
        var watching = Task.Run(async () =>
        {
            await foreach (var snapshot in session.WatchAsync())
            {
                lock (seen)
                {
                    seen.Add(snapshot.Revision);
                }
            }
        });

        session.Load("root = Stack([])\n");
        session.State.Set("$x", 1.0);
        var last = session.Current.Revision;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < Budget)
        {
            lock (seen)
            {
                if (seen.Count > 0 && seen[^1] == last)
                {
                    break;
                }
            }

            await Task.Delay(5);
        }

        await session.DisposeAsync();
        await watching.WaitAsync(Budget);
        Assert.Equal(last, seen[^1]);
        Assert.Equal(seen.Order(), seen);
    }

    [Fact]
    public async Task After_disposal_Current_returns_the_last_view_and_everything_else_throws()
    {
        var session = OpenUISession.Create(Library);
        session.Load("root = Stack([])\n");
        var last = session.Current;
        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.Same(last, session.Current);
        Assert.Throws<ObjectDisposedException>(() => session.BeginStream());
        Assert.Throws<ObjectDisposedException>(() => session.Append("x"));
        Assert.Throws<ObjectDisposedException>(() => session.State.Set("$x", 1.0));
    }

    [Fact]
    public async Task Concurrent_writers_and_readers_leave_a_consistent_session()
    {
        await using var session = OpenUISession.Create(Library);
        session.Load("$n = 0\nroot = Stack([Text($n + \"\")])\n");
        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                session.State.Set("$w" + worker, i);
                _ = session.Current.Root;
            }
        })));
        for (var worker = 0; worker < 8; worker++)
        {
            Assert.Equal((OuiValue)199.0, session.State.Get("$w" + worker));
        }
    }

    private sealed class Recording(List<OpenUIHostEvent> events) : IOpenUIHostEventHandler
    {
        public ValueTask OnEventAsync(OpenUIHostEvent e, CancellationToken ct)
        {
            events.Add(e);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ManualTime : TimeProvider
    {
        private TimerCallback? _callback;

        public TimeSpan Due { get; private set; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callback = callback;
            Due = dueTime;
            return new Timer(_ => { }, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Fire() => _callback?.Invoke(null);
    }
}
