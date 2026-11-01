using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Runtime;

public partial class SessionCommitTests
{
    [Fact]
    public async Task A_second_parse_retries_a_query_that_failed_and_keeps_its_error()
    {
        var calls = 0;
        var tools = new OpenUITools().Add("flaky", OpenUIToolEffect.Read, (_, _) =>
        {
            Interlocked.Increment(ref calls);
            throw new OpenUIToolException("no", OpenUIErrorCode.ToolError);
        });
        const string Program = "q = Query(\"nope\", {})\nr = Query(\"flaky\", {})\nroot = Stack([Text(1)])\n";
        await using var session = OpenUISession.Create(NewLibrary(), new OpenUISessionOptions { Tools = tools });
        session.Load(Program);
        await session.WhenIdleAsync().WaitAsync(Budget);
        Assert.Equal(2, session.Current.Errors.Count);

        session.Load(Program);
        await session.WhenIdleAsync().WaitAsync(Budget);

        Assert.Equal(2, session.Current.Errors.Count);
        Assert.Equal(2, Volatile.Read(ref calls));
    }
}
