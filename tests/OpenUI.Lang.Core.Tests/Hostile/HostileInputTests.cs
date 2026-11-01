using System.Diagnostics;
using System.Text;
using OpenUI.Lang.Core.Tests.Conformance;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Semantics;
using OpenUI.Lang.Values;
using Xunit;

namespace OpenUI.Lang.Core.Tests.Hostile;

/// <summary>Input built to hurt: each case must end in diagnostics, within a bounded time, with no crash.</summary>
public partial class HostileInputTests
{
    private const string LimitCode = "x-limit-exceeded";
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);
    private static readonly OpenUILimits NoTextLimits = new() { MaxInputChars = int.MaxValue, MaxStatementChars = int.MaxValue };

    private static OpenUILibrary Library { get; } = ConformanceAdapter.CreateLibrary(FixtureCatalog.LoadLibrary());

    private static string Repeat(string text, int count) => new StringBuilder(text.Length * count).Insert(0, text, count).ToString();

    private static IEnumerable<string> Codes(OpenUIErrorList errors) => errors.Select(e => e.Code.Value);

    private static async Task<T> Bounded<T>(Func<T> work)
    {
        var clock = Stopwatch.StartNew();
        var result = await Task.Run(work).WaitAsync(Budget);
        Assert.True(clock.Elapsed < Budget, $"Took {clock.Elapsed}.");
        return result;
    }

    public static TheoryData<string, string> DeepPrograms()
    {
        const int Deep = 100_000;
        return new TheoryData<string, string>
        {
            { "arrays", "root = " + Repeat("[", Deep) + "1" + Repeat("]", Deep) },
            { "unclosed arrays", "root = " + Repeat("[", Deep) },
            { "calls", "root = " + Repeat("Stack([", Deep) + Repeat("])", Deep) },
            { "unclosed calls", "root = " + Repeat("Stack(", Deep) },
            { "at calls", "root = " + Repeat("@Count(", Deep) + "1" + Repeat(")", Deep) },
            { "parens", "root = " + Repeat("(", Deep) + "1" + Repeat(")", Deep) },
            { "unclosed parens", "root = " + Repeat("(", Deep) },
            { "objects", "root = " + Repeat("{a: ", Deep) + "1" + Repeat("}", Deep) },
            { "unary", "root = " + Repeat("!", Deep) + "1" },
            { "negation", "root = " + Repeat("- ", Deep) + "1" },
            { "ternaries", "root = " + Repeat("a ? b : ", Deep) + "c" },
            { "operator chain", "root = 1" + Repeat(" + 1", Deep) },
            { "member chain", "root = a" + Repeat(".b", Deep) },
            { "index chain", "root = a" + Repeat("[0]", Deep) },
        };
    }

    [Theory]
    [MemberData(nameof(DeepPrograms))]
    public async Task Nesting_100k_deep_is_dropped_with_a_limit_diagnostic(string shape, string program)
    {
        // With the default budgets the text limit may answer first; without them the depth guard must.
        foreach (var limits in new[] { OpenUILimits.Default, NoTextLimits })
        {
            var document = await Bounded(() => new OpenUIParser(Library, new OpenUIParserOptions { Limits = limits }).Parse(program));
            Assert.True(Codes(document.Errors).Contains(LimitCode), $"{shape}: {string.Join(", ", Codes(document.Errors))}");
            Assert.Null(document.Entry);
            Assert.Null((await Bounded(() => OpenUIEvaluator.Evaluate(document, limits: limits))).Root);
        }
    }

    [Theory]
    [MemberData(nameof(DeepPrograms))]
    public async Task Nesting_100k_deep_survives_streaming(string shape, string program)
    {
        var document = await Bounded(() =>
        {
            var stream = new OpenUIParser(Library, new OpenUIParserOptions { Limits = NoTextLimits }).CreateStream();
            for (var i = 0; i < program.Length; i += 4096)
            {
                stream.Append(program.AsSpan(i, Math.Min(4096, program.Length - i)));
            }

            return stream.Complete();
        });
        Assert.True(Codes(document.Errors).Contains(LimitCode), shape);
    }

    [Theory]
    [MemberData(nameof(DeepPrograms))]
    public async Task A_host_that_removes_the_depth_limit_gets_diagnostics_and_not_a_stack_overflow(string shape, string program)
    {
        var limits = NoTextLimits with { MaxSyntaxDepth = int.MaxValue };
        var document = await Bounded(() => new OpenUIParser(Library, new OpenUIParserOptions { Limits = limits }).Parse(program));
        var render = await Bounded(() => OpenUIEvaluator.Evaluate(document, limits: limits));
        Assert.True(render.Errors.Count > 0, shape);
    }

    [Fact]
    public async Task Input_of_10_MB_is_cut_at_the_limit_and_reported()
    {
        var statement = "a" + new string('b', 40) + " = Text(\"" + new string('x', 200) + "\")\n";
        var program = "root = Stack([])\n" + Repeat(statement, 10 * 1024 * 1024 / statement.Length);
        Assert.True(program.Length >= 10_000_000);

        var document = await Bounded(() => new OpenUIParser(Library).Parse(program));
        Assert.Contains(LimitCode, Codes(document.Errors));
        Assert.True(document.Incomplete);
        Assert.NotNull((await Bounded(() => OpenUIEvaluator.Evaluate(document))).Root);
    }

    [Fact]
    public async Task One_string_of_10_MB_is_dropped_and_reported()
    {
        var program = "root = Text(\"" + new string('x', 10_000_000) + "\")";
        var document = await Bounded(() => new OpenUIParser(Library).Parse(program));
        Assert.Contains(LimitCode, Codes(document.Errors));
    }

    [Fact]
    public async Task Prose_of_10_MB_is_reported_without_statements()
    {
        var document = await Bounded(() => new OpenUIParser(Library).Parse(Repeat("not a program at all\n", 500_000)));
        Assert.Empty(document.Statements);
        Assert.True(document.Errors.HasFatal);
    }

    [Fact]
    public async Task Statements_past_the_limit_are_dropped_and_reported()
    {
        var program = new StringBuilder("root = Stack([])\n");
        for (var i = 0; i < 20_000; i++)
        {
            program.Append('s').Append(i).Append(" = 1\n");
        }

        var document = await Bounded(() => new OpenUIParser(Library).Parse(program.ToString()));
        Assert.Contains(LimitCode, Codes(document.Errors));
        Assert.Equal(OpenUILimits.Default.MaxStatements, document.Statements.Length);
    }

    private static string DoublingChain(int depth)
    {
        var program = new StringBuilder("root = Stack([Text(\"kept\")])\nkeep = a").Append(depth).Append("\na0 = 1\n");
        for (var i = 1; i <= depth; i++)
        {
            program.Append('a').Append(i).Append(" = [a").Append(i - 1).Append(", a").Append(i - 1).Append("]\n");
        }

        return program.ToString();
    }

    [Fact]
    public async Task A_doubling_chain_40_deep_stays_linear_and_reports_the_size_limit()
    {
        var program = DoublingChain(40) + "use = Text(a40)\nroot = Stack([use])\n";
        var document = await Bounded(() => new OpenUIParser(Library).Parse(program));
        var render = await Bounded(() => OpenUIEvaluator.Evaluate(document));
        Assert.Contains(LimitCode, Codes(render.Errors));
    }

    [Fact]
    public async Task A_chain_below_the_size_limit_evaluates_fully()
    {
        var program = "root = Stack([Text(@Count(a10) + \"\")])\na0 = 1\n"
            + string.Concat(Enumerable.Range(1, 10).Select(i => $"a{i} = [a{i - 1}, a{i - 1}]\n"));
        var render = await Bounded(() => OpenUIEvaluator.Evaluate(new OpenUIParser(Library).Parse(program)));
        Assert.DoesNotContain(LimitCode, Codes(render.Errors));
        Assert.NotNull(render.Root);
    }

    [Fact]
    public async Task String_doubling_stops_at_the_length_limit()
    {
        var program = "root = Stack([Text(s40)])\ns0 = \"xxxxxxxx\"\n"
            + string.Concat(Enumerable.Range(1, 40).Select(i => $"s{i} = s{i - 1} + s{i - 1}\n"));
        var render = await Bounded(() => OpenUIEvaluator.Evaluate(new OpenUIParser(Library).Parse(program)));
        Assert.Contains(LimitCode, Codes(render.Errors));
    }

    [Fact]
    public async Task Nested_each_runs_out_of_fuel_and_reports_it()
    {
        var range = "[" + string.Join(", ", Enumerable.Range(0, 200)) + "]";
        var loops = "@Each(" + range + ", \"a\", @Each(" + range + ", \"b\", @Each(" + range + ", \"c\", @Each(" + range + ", \"d\", 1))))";
        var program = $"root = Stack([Text(@Count({loops}) + \"\")])";
        var render = await Bounded(() => OpenUIEvaluator.Evaluate(new OpenUIParser(Library).Parse(program)));
        Assert.Contains(LimitCode, Codes(render.Errors));
    }

    [Fact]
    public async Task A_long_reference_chain_stops_at_the_evaluation_depth()
    {
        var program = "root = Stack([Text(r5000)])\nr0 = \"end\"\n"
            + string.Concat(Enumerable.Range(1, 5000).Select(i => $"r{i} = r{i - 1}\n"));
        var render = await Bounded(() => OpenUIEvaluator.Evaluate(new OpenUIParser(Library).Parse(program)));
        Assert.Contains(LimitCode, Codes(render.Errors));
    }

    [Fact]
    public async Task A_reference_cycle_is_unresolved_and_terminates()
    {
        var document = new OpenUIParser(Library).Parse("root = Stack([a])\na = Stack([b])\nb = Stack([a])");
        var render = await Bounded(() => OpenUIEvaluator.Evaluate(document));
        Assert.Contains("a", document.Unresolved);
        Assert.NotNull(render.Root);
    }

    [Fact]
    public async Task A_self_referencing_query_terminates_and_keeps_rendering()
    {
        const string Program = "root = Stack([Text(\"up\")])\nq = Query(\"search\", {again: q, more: q.rows}, {rows: q})\n";
        var document = new OpenUIParser(Library).Parse(Program);
        var render = await Bounded(() => OpenUIEvaluator.Evaluate(document));
        Assert.NotNull(render.Root);
        Assert.Contains("q", document.Unresolved);

        var calls = 0;
        var tools = new OpenUITools().Add("search", OpenUIToolEffect.Read, (args, _) =>
        {
            Interlocked.Increment(ref calls);
            return ValueTask.FromResult<OuiValue>(args);
        });
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools });
        session.Load(Program);
        await session.WhenIdleAsync().WaitAsync(Budget);
        Assert.Equal(1, calls);
        Assert.NotNull(session.Current.Root);
    }

    [Theory]
    [InlineData("1e999", 0, true)]
    [InlineData("-1e999", 0, true)]
    [InlineData("1e999 - 1e999", 0, false)]
    [InlineData("1e999 * 0", 0, false)]
    [InlineData("0 / 0", 0, false)]
    [InlineData("1 / 0", 0, false)]
    [InlineData("5 % 0", 0, false)]
    [InlineData("\"abc\" * 2", 0, false)]
    [InlineData("-\"abc\"", 0, false)]
    [InlineData("1e999 + -1e999", 0, false)]
    [InlineData("@Sum([1e999, -1e999])", 0, false)]
    [InlineData("@Avg([1e999, -1e999])", 0, false)]
    [InlineData("@Round(1e999)", 0, true)]
    public async Task NaN_and_infinity_never_reach_a_crash_and_NaN_never_reaches_a_value(string expression, double whenNotInfinite, bool mayBeInfinite)
    {
        var render = await Bounded(() => OpenUIEvaluator.Evaluate(new OpenUIParser(Library).Parse($"root = Stack([Metric(\"m\", {expression})])")));
        var metric = render.Root!["children"].Value.TryGetArray(out var children) ? children[0] : OuiValue.Null;
        Assert.True(metric.TryGetComponent(out var component), expression);
        Assert.True(component["value"].Value.TryGetNumber(out var number), expression);
        Assert.False(double.IsNaN(number), expression);
        if (!mayBeInfinite)
        {
            Assert.Equal(whenNotInfinite, number);
        }

        // The wire form has no NaN or Infinity (section 3.1): JSON writes them as null.
        Assert.DoesNotContain("Infinity", ConformanceAdapter.Summarize(render.Root!)!.ToJsonString());
    }

    [Fact]
    public async Task A_tool_that_throws_becomes_tool_error_on_a_query_and_the_page_renders()
    {
        var tools = new OpenUITools().Add("boom", OpenUIToolEffect.Read, (_, _) => throw new InvalidOperationException("tool blew up"));
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools });
        session.Load("root = Stack([Text(q.title)])\nq = Query(\"boom\", {}, {title: \"default\"})");
        await session.WhenIdleAsync().WaitAsync(Budget);

        var error = Assert.Single(session.Current.Errors, e => e.Code == OpenUIErrorCode.ToolError);
        Assert.Equal(OpenUIErrorSource.Query, error.Source);
        Assert.Equal("boom", error.ToolName);
        Assert.Equal("q", error.StatementId);
        Assert.NotNull(session.Current.Root);
        Assert.Equal((OuiValue)"default", session.Current.Document.Statements.Length > 0 ? FirstText(session.Current.Root!) : OuiValue.Null);
    }

    [Fact]
    public async Task A_tool_that_throws_stops_a_mutation_plan_and_the_error_is_data()
    {
        var tools = new OpenUITools().Add("boom", OpenUIToolEffect.Read, (_, _) => throw new InvalidOperationException("tool blew up"));
        var events = new CountingEvents();
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools, Events = events });
        session.Load("root = Stack([go, Text(m.status)])\nm = Mutation(\"boom\", {})\ngo = Button(\"Go\", Action([@Run(m), @ToAssistant(\"after\")]))");

        var button = FirstChild(session.Current.Root!, 0);
        Assert.True(button["action"].Value.TryGetAction(out var action));
        var outcome = await session.RunActionAsync(action, new ActionOrigin(button, null)).AsTask().WaitAsync(Budget);

        Assert.Equal(ActionOutcomeKind.Stopped, outcome.Kind);
        Assert.Equal(OpenUIErrorCode.ToolError, outcome.Error!.Code);
        Assert.Equal(MutationState.Error, session.GetMutationStatus("m").State);
        Assert.Equal(0, events.Count);
        Assert.Equal((OuiValue)"error", FirstChild(session.Current.Root!, 1)["text"].Value);
    }

    [Fact]
    public async Task A_tool_that_ignores_cancellation_cannot_hold_up_disposal()
    {
        var never = new TaskCompletionSource<OuiValue>();
        var tools = new OpenUITools().Add("hang", OpenUIToolEffect.Read, (_, _) => new ValueTask<OuiValue>(never.Task));
        var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools });
        session.Load("root = Stack([])\nq = Query(\"hang\", {}, {})");
        await session.DisposeAsync().AsTask().WaitAsync(Budget);
        Assert.Throws<ObjectDisposedException>(() => session.BeginStream());
    }

    [Fact]
    public async Task A_host_function_that_throws_becomes_runtime_error_and_a_null_prop()
    {
        var library = OpenUILibrary.FromSpec(Library.ToSpec(), implement => implement.Function("Upper", (in FunctionArguments _) => throw new InvalidOperationException("no")));
        var render = await Bounded(() => OpenUIEvaluator.Evaluate(new OpenUIParser(library).Parse("root = Stack([Tag(\"t\", @Upper(\"x\"))])")));
        Assert.Contains("runtime-error", Codes(render.Errors));
        Assert.NotNull(render.Root);
    }

    [Fact]
    public async Task A_host_event_handler_that_throws_does_not_reach_the_action()
    {
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Events = new ThrowingEvents() });
        session.Load("root = Stack([go])\ngo = Button(\"Go\", @ToAssistant(\"hi\"))");
        var button = FirstChild(session.Current.Root!, 0);
        Assert.True(button["action"].Value.TryGetAction(out var action));
        var outcome = await session.RunActionAsync(action, new ActionOrigin(button, null));
        await session.WhenIdleAsync().WaitAsync(Budget);
        Assert.Equal(ActionOutcomeKind.Completed, outcome.Kind);
    }

    [Fact]
    public async Task A_custom_function_calling_back_into_the_session_gets_runtime_error()
    {
        OpenUISession? session = null;
        var library = OpenUILibrary.FromSpec(Library.ToSpec(), implement => implement.Function("Upper", (in FunctionArguments _) =>
        {
            session!.State.Set("$x", 1.0);
            return "unreachable";
        }));
        await using (session = OpenUISession.Create(library))
        {
            session.Load("$x = 0\nroot = Stack([Tag(\"t\", @Upper(\"x\"))])");
            Assert.Contains("runtime-error", Codes(session.Current.Errors));
            Assert.Equal((OuiValue)0.0, session.State.Get("$x"));
        }
    }

    [Fact]
    public async Task A_collator_that_cannot_run_leaves_the_array_unsorted_and_says_so()
    {
        var document = new OpenUIParser(Library).Parse("root = Stack([Tag(@Sort([\"b\", \"a\"], \"\") + \"\")])");
        var bag = new DiagnosticBag(10);
        var evaluator = new Evaluator(document, _ => OuiValue.Null, null, OpenUILimits.Default, new FailingCollator(new NotSupportedException("no ICU")), bag);
        var result = await Bounded(() => evaluator.EvaluateStatement("root"));
        Assert.False(result.IsAbsent);
        Assert.Contains(bag.Items, d => d.Error.Code == OpenUIErrorCode.CollationDegraded);
    }

    [Fact]
    public async Task A_collator_that_throws_is_a_runtime_error_and_fuel_inside_a_sort_stays_a_limit()
    {
        var document = new OpenUIParser(Library).Parse("root = Stack([Tag(@Sort([\"b\", \"a\"], \"\") + \"\")])");
        var bag = new DiagnosticBag(10);
        var evaluator = new Evaluator(document, _ => OuiValue.Null, null, OpenUILimits.Default, new FailingCollator(new FormatException("bad")), bag);
        await Bounded(() => evaluator.EvaluateStatement("root"));
        Assert.Contains(bag.Items, d => d.Error.Code == OpenUIErrorCode.RuntimeError);

        var words = "[" + string.Join(", ", Enumerable.Range(0, 2000).Select(i => $"\"w{i}\"")) + "]";
        var sorted = new OpenUIParser(Library).Parse($"root = Stack([Tag(@Count(@Sort({words}, \"\")) + \"\")])");
        var render = await Bounded(() => OpenUIEvaluator.Evaluate(sorted, limits: new OpenUILimits { Fuel = 2_000 }));
        Assert.Contains(LimitCode, Codes(render.Errors));
    }

    [Fact]
    public async Task Cancelling_ParseStreamAsync_mid_stream_throws_and_yields_no_final_document()
    {
        using var cancel = new CancellationTokenSource();
        var documents = new List<OpenUIDocument>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var document in new OpenUIParser(Library).ParseStreamAsync(Endless(), cancel.Token).WithCancellation(cancel.Token))
            {
                documents.Add(document);
                if (documents.Count == 3)
                {
                    await cancel.CancelAsync();
                }
            }
        }).WaitAsync(Budget);
        Assert.Equal(3, documents.Count);
        Assert.False(documents[^1].IsFinal);
    }

    [Fact]
    public async Task Cancelling_ConsumeAsync_mid_stream_ends_the_stream_and_the_reply_still_renders()
    {
        using var cancel = new CancellationTokenSource();
        await using var session = OpenUISession.Create(Library);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ConsumeAsync(CancelAfterFirst(cancel), cancel.Token)).WaitAsync(Budget);
        var snapshot = session.Current;
        Assert.False(snapshot.IsStreaming);
        Assert.True(snapshot.Document.IsFinal);
        Assert.NotNull(snapshot.Root);
    }

    [Fact]
    public async Task Cancelling_ConsumeAsync_without_finalizing_leaves_the_stream_open()
    {
        using var cancel = new CancellationTokenSource();
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { FinalizeOnCancel = false });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ConsumeAsync(CancelAfterFirst(cancel), cancel.Token)).WaitAsync(Budget);
        Assert.True(session.Current.IsStreaming);
    }

    [Fact]
    public async Task Cancelling_an_action_during_a_mutation_returns_the_mutation_to_idle()
    {
        var started = new TaskCompletionSource();
        var tools = new OpenUITools().Add("slow", OpenUIToolEffect.Read, async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return OuiValue.Null;
        });
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools });
        session.Load("root = Stack([go])\nm = Mutation(\"slow\", {})\ngo = Button(\"Go\", @Run(m))");
        var button = FirstChild(session.Current.Root!, 0);
        Assert.True(button["action"].Value.TryGetAction(out var action));

        using var cancel = new CancellationTokenSource();
        var run = session.RunActionAsync(action, new ActionOrigin(button, null), cancel.Token).AsTask();
        await started.Task.WaitAsync(Budget);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run).WaitAsync(Budget);
        Assert.Equal(MutationState.Idle, session.GetMutationStatus("m").State);
    }

    [Fact]
    public async Task A_query_with_a_tiny_refresh_interval_is_clamped()
    {
        var time = new ManualTime();
        var tools = new OpenUITools().Add("t", OpenUIToolEffect.Read, (_, _) => ValueTask.FromResult(OuiValue.Null));
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools, Time = time });
        session.Load("root = Stack([])\nq = Query(\"t\", {}, {}, 0.000001)");
        await session.WhenIdleAsync().WaitAsync(Budget);
        Assert.Equal(TimeSpan.FromSeconds(1), time.Due);
    }

    private static OuiComponent FirstChild(OuiComponent root, int index)
    {
        Assert.True(root["children"].Value.TryGetArray(out var children));
        Assert.True(children[index].TryGetComponent(out var child));
        return child;
    }

    private static OuiValue FirstText(OuiComponent root) => FirstChild(root, 0)["text"].Value;

    private static async IAsyncEnumerable<string> Endless()
    {
        for (var i = 0; ; i++)
        {
            await Task.Yield();
            yield return $"s{i} = {i}\n";
        }
    }

    private static async IAsyncEnumerable<string> CancelAfterFirst(CancellationTokenSource cancel)
    {
        yield return "root = Stack([Text(\"cut off\"";
        await cancel.CancelAsync();
        await Task.Yield();
        yield return "never read";
    }

    private sealed class CountingEvents : IOpenUIHostEventHandler
    {
        private int _count;

        public int Count => _count;

        public ValueTask OnEventAsync(OpenUIHostEvent e, CancellationToken ct)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingEvents : IOpenUIHostEventHandler
    {
        public ValueTask OnEventAsync(OpenUIHostEvent e, CancellationToken ct) => throw new InvalidOperationException("host blew up");
    }

    private sealed class FailingCollator(Exception exception) : IOuiCollator
    {
        public int Compare(string left, string right) => throw exception;
    }

    private sealed class ManualTime : TimeProvider
    {
        public TimeSpan Due { get; private set; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Due = dueTime;
            return new Timer(callback, state, Timeout.Infinite, Timeout.Infinite);
        }
    }
}
