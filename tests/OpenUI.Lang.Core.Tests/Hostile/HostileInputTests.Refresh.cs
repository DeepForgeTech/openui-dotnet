using OpenUI.Lang.Runtime;

namespace OpenUI.Lang.Core.Tests.Hostile;

public partial class HostileInputTests
{
    [Fact]
    public async Task A_refresh_interval_past_the_timer_range_does_not_escape_Load()
    {
        await HostileProbe.SurvivesAsync("session", "root = Stack([])\nq = Query(\"t\", {}, null, 5000000)\n");
    }

    [Fact]
    public async Task A_refresh_interval_set_past_the_timer_range_does_not_kill_the_timer_thread()
    {
        await HostileProbe.SurvivesAsync("click-timer", "$i = 1\nroot = Stack([b])\nb = Button(\"go\", @Set($i, 5000000))\nq = Query(\"t\", {}, null, $i)\n");
    }

    [Fact]
    public async Task A_refresh_tick_survives_a_tool_provider_that_throws()
    {
        var report = await HostileProbe.SurvivesAsync("throwing-contains-timer", "root = Stack([])\nq = Query(\"t\", {}, null, 1)\n");
        Assert.Contains("tool-error", HostileProbe.Codes(report));
    }
}
