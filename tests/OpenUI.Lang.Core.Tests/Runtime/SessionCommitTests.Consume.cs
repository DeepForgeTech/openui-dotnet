using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Runtime;

public partial class SessionCommitTests
{
    [Fact]
    public async Task Consume_finalizes_on_any_failure_and_rethrows()
    {
        static async IAsyncEnumerable<string> Failing()
        {
            yield return "root = Stack([Text(\"partial\")";
            await Task.Yield();
            throw new InvalidOperationException("transport");
        }

        await using var session = OpenUISession.Create(NewLibrary());
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ConsumeAsync(Failing()));

        Assert.False(session.Current.IsStreaming);
        Assert.Equal("partial", TextOf(session.Current, 0));
    }
}
