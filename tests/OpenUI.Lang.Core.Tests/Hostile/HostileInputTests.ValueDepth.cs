using System.Text;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Hostile;

public partial class HostileInputTests
{
    // Each statement wraps the previous in brackets; prep forces them in ascending order, so every
    // step reads a memoized result and evaluation depth stays flat while value depth grows.
    private static string DeepChain(int count, int perStatement)
    {
        var program = new StringBuilder("x0 = 1\n");
        for (var i = 1; i <= count; i++)
        {
            program.Append('x').Append(i).Append(" = ").Append(Repeat("[", perStatement)).Append('x').Append(i - 1).Append(Repeat("]", perStatement)).Append('\n');
        }

        program.Append("prep = [");
        for (var i = 1; i <= count; i++)
        {
            program.Append("@Count(x").Append(i).Append("), ");
        }

        return program.Append("]\n").ToString();
    }

    public static TheoryData<string, string> DeepValuePrograms() => new()
    {
        { "evaluate", DeepChain(1900, 50) + "root = Stack([Tag(\"a\", @Count(prep)), Text(\"\" + x1900)])\n" },
        { "serialize", DeepChain(1900, 50) + "root = Stack([prep, x1900])\n" },
        { "session", DeepChain(1900, 50) + "$s = x1900\nroot = Stack([prep, Text(\"\" + $s)])\n" },
        { "click", DeepChain(1900, 50) + "$s = null\nroot = Stack([prep, go])\ngo = Button(\"Go\", @Set($s, x1900))\n" },
        { "evaluate", DeepChain(1900, 50) + "root = Stack([prep, x1900 == x1900])\n" },
    };

    [Theory]
    [MemberData(nameof(DeepValuePrograms))]
    public async Task A_value_built_past_the_depth_limit_is_dropped_before_a_walk_can_overflow(string scenario, string program)
    {
        var report = await HostileProbe.SurvivesAsync(scenario, program);
        Assert.Contains(LimitCode, HostileProbe.Codes(report));
    }

    [Fact]
    public async Task A_component_tree_built_past_the_depth_limit_is_dropped()
    {
        var program = new StringBuilder("c0 = Text(\"leaf\")\n");
        for (var i = 1; i <= 5001; i++)
        {
            program.Append('c').Append(i).Append(" = Stack([c").Append(i - 1).Append("])\n");
        }

        program.Append("prep = [");
        for (var i = 1; i <= 5001; i++)
        {
            program.Append("c").Append(i).Append(", ");
        }

        program.Append("]\nroot = Stack([prep, c5001])\n");
        var report = await HostileProbe.SurvivesAsync("evaluate", program.ToString());
        Assert.Contains(LimitCode, HostileProbe.Codes(report));
    }

    [Fact]
    public async Task A_tool_result_past_the_depth_limit_fails_the_query_and_never_reaches_a_walk()
    {
        var report = await HostileProbe.SurvivesAsync("deep-tool-result", "q = Query(\"t\", {}, {})\nroot = Stack([Text(\"\" + q), Text(\"page\")])\n");
        Assert.Contains("tool-error", HostileProbe.Codes(report));
        Assert.True(report["root"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Writing_a_state_value_into_itself_stops_at_the_depth_limit()
    {
        await using var session = OpenUISession.Create(Library);
        session.Load("$x = 1\nroot = Stack([go])\ngo = Button(\"Grow\", @Set($x, [$x]))\n");
        Assert.True(session.Current.Root!["children"].Value.TryGetArray(out var children));
        Assert.True(children[0].TryGetComponent(out var go));
        Assert.True(go["action"].Value.TryGetAction(out var action));

        for (var i = 0; i < 200; i++)
        {
            await session.RunActionAsync(action, new ActionOrigin(go, null));
        }

        Assert.True(DepthOf(session.State.Get("$x")) <= OpenUILimits.Default.MaxValueDepth);
        Assert.Contains(LimitCode, Codes(session.Current.Errors));
    }

    [Fact]
    public async Task Deep_query_arguments_do_not_escape_Load_or_a_click_or_a_tick()
    {
        var program = DeepChain(40, 50) + "$deep = null\nroot = Stack([go])\nq = Query(\"t\", {a: $deep}, {}, 1)\ngo = Button(\"Go\", @Set($deep, [@Count(prep), x40]))\n";
        await HostileProbe.SurvivesAsync("click-timer", program);
        await HostileProbe.SurvivesAsync("session", "$s = " + Repeat("[", 120) + "1" + Repeat("]", 120) + "\n" + "$t = [$s, [$s, [$s, [$s, [$s, [$s, [$s, [$s, $s]]]]]]]]\nq = Query(\"t\", {a: $t})\nroot = Stack([])\n");
    }

    [Fact]
    public async Task Query_arguments_that_cannot_become_json_fail_that_query_only()
    {
        var limits = OpenUILimits.Default with { MaxValueDepth = int.MaxValue, MaxEvalDepth = 5000 };
        var tools = new OpenUITools().Add("t", OpenUIToolEffect.Read, (_, _) => ValueTask.FromResult(Values.OuiValue.Null));
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools, Limits = limits });
        var statements = DeepChain(40, 50);
        session.Load(statements + "$deep = x40\nroot = Stack([Text(\"page\")])\nbad = Query(\"t\", {a: $deep})\ngood = Query(\"t\", {})\n");
        await session.WhenIdleAsync().WaitAsync(Budget);

        Assert.Contains("tool-error", Codes(session.GetQueryStatus("bad").Errors));
        Assert.Empty(session.GetQueryStatus("good").Errors);
    }

    private static int DepthOf(OuiValue value)
    {
        var depth = 0;
        while (value.TryGetArray(out var array) && array.Count > 0)
        {
            depth++;
            value = array[0];
        }

        return depth;
    }
}
