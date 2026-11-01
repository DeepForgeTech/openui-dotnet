using System.Text;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Runtime;

/// <summary>
/// The commit path: a snapshot is computed only from a whole commit, one revision identifies one
/// snapshot, and every evaluator run is guarded against re-entry (language.md, sections 3.6, 4, 5.1 and 8.3).
/// </summary>
public partial class SessionCommitTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private Func<OuiValue> _probe = () => OuiValue.Null;
    private int _ticks;

    private OpenUILibrary NewLibrary() => OpenUILibrary.CreateBuilder("p").Root("Stack")
        .Add(new ComponentSpec("Stack", "", [new PropSpec("children", TypeShape.Any, false)], default, "Stack(children?: any)"))
        .Add(new ComponentSpec("Text", "", [new PropSpec("text", TypeShape.Any, false)], default, "Text(text?: any)"))
        .Add(new ComponentSpec("Metric", "", [new PropSpec("value", TypeShape.Number, true)], default, "Metric(value: number)"))
        .Add(new ComponentSpec("Button", "", [new PropSpec("action", new ActionSlotShape([]), false)], default, "Button(action?: ActionExpression)"))
        .AddFunctions(
            new FunctionDefinition(new FunctionSpec("Probe", "", [], null, "Probe()"), (in FunctionArguments _) => _probe()),
            new FunctionDefinition(new FunctionSpec("Tick", "", [], null, "Tick()"), (in FunctionArguments _) =>
            {
                _ticks++;
                return 1;
            }),
            new FunctionDefinition(new FunctionSpec("Nan", "", [], null, "Nan()"), (in FunctionArguments _) => OuiValue.Number(double.NaN)))
        .Build();

    private static OpenUITools EchoTool() => new OpenUITools().Add("t", OpenUIToolEffect.Read, (args, _) => ValueTask.FromResult(args));

    private static string TextOf(RenderSnapshot snapshot, int index)
    {
        Assert.True(snapshot.Root!["children"].Value.TryGetArray(out var children));
        Assert.True(children[index].TryGetComponent(out var text));
        return OpenUI.Lang.Values.Ecma.ToString(text["text"].Value);
    }

    private static OuiComponent ChildAt(RenderSnapshot snapshot, int index)
    {
        Assert.True(snapshot.Root!["children"].Value.TryGetArray(out var children));
        Assert.True(children[index].TryGetComponent(out var child));
        return child;
    }

    [Fact]
    public async Task A_reader_during_a_reset_sees_the_state_before_or_after_never_between()
    {
        await using var session = OpenUISession.Create(NewLibrary(), new OpenUISessionOptions { Tools = EchoTool() });
        session.Load("$a = 1\n$b = 1\nq = Query(\"t\", {a: $a, p: @Probe()})\nroot = Stack([Text(\"\" + $a + \",\" + $b)])\n");
        await session.WhenIdleAsync().WaitAsync(Budget);
        session.State.Set("$a", 5.0);
        session.State.Set("$b", 5.0);
        Assert.Equal("5,5", TextOf(session.Current, 0));
        await session.WhenIdleAsync().WaitAsync(Budget);
        _ = session.Current;

        string? seen = null;
        _probe = () =>
        {
            var read = Task.Run(() => session.Current);
            if (seen is null && read.Wait(TimeSpan.FromMilliseconds(500)))
            {
                seen = TextOf(read.Result, 0);
            }

            return OuiValue.Null;
        };
        session.State.Reset("$a", "$b");
        _probe = () => OuiValue.Null;

        Assert.Contains(seen, new[] { null, "5,5", "1,1" });
        Assert.Equal("1,1", TextOf(session.Current, 0));
    }

    [Fact]
    public async Task Writes_evaluate_nothing_until_someone_reads_the_snapshot()
    {
        await using var session = OpenUISession.Create(NewLibrary());
        session.Load("$a = 1\n$b = 1\n$c = 1\nroot = Stack([Text(@Tick())])\n");
        _ = session.Current;
        _ticks = 0;

        session.State.Reset("$a", "$b", "$c");
        session.State.Set("$a", 2.0);
        session.State.Set("$a", 3.0);
        session.State.Set("$a", 4.0);

        Assert.Equal(0, _ticks);
        _ = session.Current;
        Assert.Equal(1, _ticks);
    }

    [Fact]
    public async Task A_state_default_is_evaluated_once_per_document()
    {
        await using var session = OpenUISession.Create(NewLibrary());
        session.Load("$a = @Tick()\n$b = 1\nroot = Stack([])\n");
        _ticks = 0;
        session.State.Reset("$a");
        session.State.Set("$b", 2.0);
        session.State.Reset("$b");
        _ = session.State.Snapshot();
        _ = session.Current;
        Assert.True(_ticks <= 1, $"{_ticks} evaluations.");
    }

    [Fact]
    public async Task An_edit_is_one_commit_and_no_reader_sees_an_empty_page()
    {
        await using var session = OpenUISession.Create(NewLibrary());
        session.Load("root = Stack([Text(\"one\")])\n");
        var roots = new List<bool>();
        var revisions = new List<long>();
        session.Invalidated += (_, e) =>
        {
            roots.Add(session.Current.Root is not null);
            revisions.Add(e.Revision);
        };
        var before = session.Current.Revision;

        session.ApplyEdit("extra = Text(\"two\")\nroot = Stack([extra])\n");

        Assert.Equal(new[] { true }, roots);
        Assert.Equal(before + 1, session.Current.Revision);
    }

    [Fact]
    public async Task A_custom_function_that_writes_during_query_arguments_gets_a_runtime_error()
    {
        await using var session = OpenUISession.Create(NewLibrary(), new OpenUISessionOptions { Tools = EchoTool() });
        string? outcome = null;
        _probe = () =>
        {
            if (outcome is null)
            {
                outcome = "accepted";
                session.State.Set("$y", 42.0);
            }

            return OuiValue.Null;
        };
        session.Load("$y = 0\nq = Query(\"t\", {p: @Probe()})\nroot = Stack([Text(1)])\n");
        _probe = () => OuiValue.Null;
        await session.WhenIdleAsync().WaitAsync(Budget);

        Assert.Equal((OuiValue)0.0, session.State.Get("$y"));
        Assert.Contains(session.Current.Errors, e => e.Code == OpenUIErrorCode.RuntimeError);
    }

    [Fact]
    public async Task A_custom_function_that_writes_during_a_deferred_action_argument_gets_a_runtime_error()
    {
        await using var session = OpenUISession.Create(NewLibrary());
        session.Load("$y = 0\n$z = 0\nroot = Stack([go])\ngo = Button(@Set($z, @Probe()))\n");
        var button = ChildAt(session.Current, 0);
        Assert.True(button["action"].Value.TryGetAction(out var action));
        _probe = () =>
        {
            session.State.Set("$y", 42.0);
            return OuiValue.Null;
        };

        await session.RunActionAsync(action, new ActionOrigin(button, null));

        Assert.Equal((OuiValue)0.0, session.State.Get("$y"));
        _probe = () => OuiValue.Null;
        Assert.Contains(session.Current.Errors, e => e.Code == OpenUIErrorCode.RuntimeError);
    }

    [Fact]
    public async Task A_snapshot_read_during_stream_end_never_shares_a_revision_with_a_different_one()
    {
        await using var session = OpenUISession.Create(NewLibrary(), new OpenUISessionOptions { Tools = new OpenUITools() });
        RenderSnapshot? seen = null;
        _probe = () =>
        {
            var read = Task.Run(() => session.Current);
            if (seen is null && read.Wait(TimeSpan.FromMilliseconds(500)))
            {
                seen = read.Result;
            }

            return OuiValue.Null;
        };

        session.Load("q = Query(\"nope\", {p: @Probe()})\nroot = Stack([Text(1)])\n");
        _probe = () => OuiValue.Null;
        var after = session.Current;

        Assert.True(seen is null || seen.Revision != after.Revision || seen.Errors.Count == after.Errors.Count, $"Revision {after.Revision} has {seen?.Errors.Count} and {after.Errors.Count} errors.");
    }

    [Fact]
    public async Task A_missing_mutation_tool_tells_the_host()
    {
        await using var session = OpenUISession.Create(NewLibrary(), new OpenUISessionOptions { Tools = new OpenUITools() });
        session.Load("m = Mutation(\"nope\", {})\nroot = Stack([Button(@Run(m))])\n");
        var button = ChildAt(session.Current, 0);
        Assert.True(button["action"].Value.TryGetAction(out var action));
        var invalidated = 0;
        var lists = new List<OpenUIErrorList>();
        session.Invalidated += (_, _) => invalidated++;
        session.ErrorsChanged += (_, list) => lists.Add(list);

        var outcome = await session.RunActionAsync(action, new ActionOrigin(button, null));

        Assert.Equal(ActionOutcomeKind.Stopped, outcome.Kind);
        Assert.True(invalidated > 0);
        Assert.Contains(lists[^1], e => e.Code == OpenUIErrorCode.ToolNotFound);
    }
}
