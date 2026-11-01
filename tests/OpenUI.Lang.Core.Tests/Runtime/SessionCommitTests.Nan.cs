using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Runtime;

public partial class SessionCommitTests
{
    [Fact]
    public async Task A_custom_function_and_a_state_write_cannot_produce_nan()
    {
        await using var session = OpenUISession.Create(NewLibrary());
        session.Load("$x = 0\nroot = Stack([Text(\"fn:\" + @Nan()), Text(\"state:\" + $x)])\n");
        session.State.Set("$x", double.NaN);

        Assert.Equal("fn:0", TextOf(session.Current, 0));
        Assert.Equal("state:0", TextOf(session.Current, 1));
        Assert.True(OuiValue.Number(double.NaN).TryGetNumber(out var zero));
        Assert.Equal(0, zero);
    }
}
