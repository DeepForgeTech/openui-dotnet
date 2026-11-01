using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Runtime;

public partial class SessionCommitTests
{
    [Fact]
    public async Task The_last_error_list_delivered_is_the_current_one_when_commits_race()
    {
        await using var session = OpenUISession.Create(NewLibrary());
        session.Load("$x = 1\nroot = Stack([Text(\"page\"), Metric($x)])\n");
        Assert.Empty(session.Current.Errors);

        var blocked = false;
        var last = OpenUIErrorList.Empty;
        session.ErrorsChanged += (_, list) =>
        {
            if (list.Count > 0 && !blocked)
            {
                // Holds this delivery while another thread commits a recovery and tries to deliver it.
                blocked = true;
                Assert.True(Task.Run(() => session.State.Set("$x", 2.0)).Wait(Budget));
            }
        };
        session.ErrorsChanged += (_, list) => last = list;

        session.State.Set("$x", "not a number");

        await session.WhenIdleAsync().WaitAsync(Budget);
        Assert.True(blocked);
        Assert.Empty(session.Current.Errors);
        Assert.Empty(last);
    }
}
