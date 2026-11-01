using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Hostile;

public partial class HostileInputTests
{
    private static OpenUITools ToolThatRegisters(Action<CancellationToken> onCall) => new OpenUITools().Add("t", OpenUIToolEffect.Read, async (_, ct) =>
    {
        onCall(ct);
        await Task.Delay(Timeout.Infinite, ct);
        return OuiValue.Null;
    });

    [Fact]
    public async Task A_throwing_cancellation_callback_does_not_lose_the_commit_that_cancelled_it()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tools = ToolThatRegisters(ct =>
        {
            ct.Register(() => throw new InvalidOperationException("tool callback"));
            entered.TrySetResult();
        });
        var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools });
        session.Load("$x = 1\nroot = Stack([])\nq = Query(\"t\", {a: $x})\n");
        await entered.Task.WaitAsync(Budget);

        var invalidated = 0;
        session.Invalidated += (_, _) => invalidated++;
        session.State.Set("$x", 2.0);

        Assert.Equal((OuiValue)2.0, session.State.Get("$x"));
        Assert.True(invalidated > 0);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task A_throwing_cancellation_callback_does_not_kill_the_refresh_timer()
    {
        await HostileProbe.SurvivesAsync("throwing-cancel-timer", "root = Stack([])\nq = Query(\"t\", {}, null, 1)\n");
    }

    [Fact]
    public async Task A_cancellation_callback_runs_outside_the_session_lock()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        OpenUISession? session = null;
        var readWhileCancelling = false;
        var tools = ToolThatRegisters(ct =>
        {
            ct.Register(() => readWhileCancelling = Task.Run(() => session!.State.Get("$x")).Wait(TimeSpan.FromSeconds(5)));
            entered.TrySetResult();
        });
        await using (session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools }))
        {
            session.Load("$x = 1\nroot = Stack([])\nq = Query(\"t\", {a: $x})\n");
            await entered.Task.WaitAsync(Budget);
            await Task.Run(() => session.State.Set("$x", 2.0)).WaitAsync(Budget);
            Assert.True(readWhileCancelling);
        }
    }

    [Fact]
    public async Task Disposing_right_after_load_or_writes_never_throws()
    {
        var tools = new OpenUITools().Add("t", OpenUIToolEffect.Read, (_, _) => ValueTask.FromResult(OuiValue.Null));
        for (var i = 0; i < 400; i++)
        {
            var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools });
            session.Load("$q = \"\"\nroot = Stack([Text(\"p\")])\ns = Query(\"t\", {q: $q}, {})");
            for (var k = 0; k < 5; k++)
            {
                session.State.Set("$q", "k" + k);
            }

            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task Disposal_finishes_its_cleanup_when_a_cancellation_callback_throws()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tools = new OpenUITools().Add("m", OpenUIToolEffect.Read, async (_, ct) =>
        {
            ct.Register(() => throw new InvalidOperationException("tool callback"));
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return OuiValue.Null;
        });
        var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools });
        session.Load("m = Mutation(\"m\", {})\nroot = Stack([go])\ngo = Button(\"Go\", @Run(m))\n");
        Assert.True(session.Current.Root!["children"].Value.TryGetArray(out var children));
        Assert.True(children[0].TryGetComponent(out var go));
        Assert.True(go["action"].Value.TryGetAction(out var action));
        var run = session.RunActionAsync(action, new ActionOrigin(go, null)).AsTask();
        await entered.Task.WaitAsync(Budget);

        await session.DisposeAsync().AsTask().WaitAsync(Budget);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Throws<ObjectDisposedException>(() => session.State.Set("$x", 1.0));
    }

    [Fact]
    public async Task Load_then_dispose_never_faults_when_repeated()
    {
        var tools = new OpenUITools().Add("t", OpenUIToolEffect.Read, (_, _) => ValueTask.FromResult(OuiValue.Null));
        for (var i = 0; i < 3000; i++)
        {
            var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools });
            session.Load("root = Stack([])\nq = Query(\"t\", {})\n");
            await session.DisposeAsync();
        }
    }
}
