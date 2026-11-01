using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Runtime;

public partial class SessionCommitTests
{
    [Fact]
    public async Task A_refresh_tick_leaves_a_running_fetch_alone()
    {
        var clock = new ManualClock();
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var tools = new OpenUITools().Add("slow", OpenUIToolEffect.Read, async (_, ct) =>
        {
            Interlocked.Increment(ref calls);
            await finish.Task.WaitAsync(ct);
            return "done";
        });
        await using var session = OpenUISession.Create(NewLibrary(), new OpenUISessionOptions { Tools = tools, Time = clock });
        session.Load("q = Query(\"slow\", {}, null, 1)\nroot = Stack([Text(\"\" + q)])\n");

        for (var i = 0; i < 5; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(10);
        }

        Assert.Equal(1, Volatile.Read(ref calls));
        finish.SetResult();
        await session.WhenIdleAsync().WaitAsync(Budget);
        Assert.Equal((OuiValue)"done", session.GetQueryStatus("q").Data);
    }
}
