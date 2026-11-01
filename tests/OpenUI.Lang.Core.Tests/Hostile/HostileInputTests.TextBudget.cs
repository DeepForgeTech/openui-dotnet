using System.Text;

namespace OpenUI.Lang.Core.Tests.Hostile;

public partial class HostileInputTests
{
    private static string BigArray(int count) => "[" + Repeat("1 ", count) + "]";

    private static string Doublings(int count)
    {
        var program = new StringBuilder("s0 = \"x\"\n");
        for (var i = 1; i <= count; i++)
        {
            program.Append('s').Append(i).Append(" = s").Append(i - 1).Append(" + s").Append(i - 1).Append('\n');
        }

        return program.ToString();
    }

    public static TheoryData<string, string> MemoryBombs()
    {
        var big = new string('x', 200_000);
        return new TheoryData<string, string>
        {
            { "references joined", "a = \"" + big + "\"\nb = [" + Repeat("a ", 9000) + "]\nroot = Stack([Text(\"\" + b)])\n" },
            { "each concat", "a = \"" + big + "\"\nn = " + BigArray(100_000) + "\nroot = Stack(@Each(n, \"i\", Text(a + \"y\")))\n" },
            { "each keeps references, then joined", "root = Stack([Text(\"\" + arr)])\nbig = " + BigArray(99_000) + "\narr = @Each(big, \"i\", s20)\n" + Doublings(20) },
            { "each keeps new strings", "root = Stack([Text(@Count(arr) + \"\")])\nbig = " + BigArray(50_000) + "\narr = @Each(big, \"i\", s19 + i)\n" + Doublings(19) },
            { "each keeps plucked arrays", "root = Stack([Text(@Count(arr) + \"\")])\nbig = " + BigArray(50_000) + "\narr = @Each(big, \"i\", big.x)\n" },
        };
    }

    [Theory]
    [MemberData(nameof(MemoryBombs))]
    public async Task A_small_program_cannot_allocate_without_limit(string shape, string program)
    {
        var report = await HostileProbe.SurvivesAsync("evaluate", program);
        Assert.True(HostileProbe.Codes(report).Contains(LimitCode), shape);
    }

    [Fact]
    public async Task A_long_tool_name_is_cut_before_it_reaches_many_error_messages()
    {
        var program = new StringBuilder("root = Stack([Text(\"p\")])\n").Append(Doublings(20));
        for (var i = 0; i < 9000; i++)
        {
            program.Append('q').Append(i).Append(" = Query(s20, {}, {})\n");
        }

        await HostileProbe.SurvivesAsync("session", program.ToString());
    }

    [Theory]
    [InlineData("session")]
    [InlineData("serialize")]
    public async Task Fan_out_through_action_values_counts_against_the_size_budget(string scenario)
    {
        var program = new StringBuilder("$x = 0\na0 = @Set($x, 1)\n");
        for (var i = 1; i <= 24; i++)
        {
            program.Append('a').Append(i).Append(" = @Set($x, [a").Append(i - 1).Append(", a").Append(i - 1).Append("])\n");
        }

        program.Append("q = Query(\"t\", {v: a24})\nroot = Stack([Button(\"go\", a24)])\n");
        var report = await HostileProbe.SurvivesAsync(scenario, program.ToString());
        Assert.Contains(LimitCode, HostileProbe.Codes(report));
    }

    [Fact]
    public async Task Joining_large_strings_stops_before_building_them()
    {
        var program = new StringBuilder("s0 = \"").Append('x', 100).Append("\"\n");
        for (var i = 1; i <= 13; i++)
        {
            program.Append('s').Append(i).Append(" = s").Append(i - 1).Append(" + s").Append(i - 1).Append('\n');
        }

        program.Append("a = [").AppendJoin(',', Enumerable.Repeat("s13", 60_000)).Append("]\nroot = Stack([\"\" + a])\n");
        var report = await HostileProbe.SurvivesAsync("evaluate", program.ToString());
        Assert.Contains(LimitCode, HostileProbe.Codes(report));
    }

    // Under 600 characters: strings at the length limit, shared by reference so the node count stays small.
    private static string JoinBomb(string last)
    {
        var program = new StringBuilder("root = Stack([Text(u)])\ns0 = \"xxxxxxxx\"\n");
        for (var i = 1; i <= 17; i++)
        {
            program.Append('s').Append(i).Append(" = s").Append(i - 1).Append(" + s").Append(i - 1).Append('\n');
        }

        program.Append("a0 = [s17]\n");
        for (var i = 1; i <= 15; i++)
        {
            program.Append('a').Append(i).Append(" = [a").Append(i - 1).Append(", a").Append(i - 1).Append("]\n");
        }

        return program.Append("u = ").Append(last).Append('\n').ToString();
    }

    [Theory]
    [InlineData("a10 + \"\"")]
    [InlineData("a11 + \"\"")]
    [InlineData("a15 == \"x\"")]
    public async Task A_short_program_of_shared_long_strings_stays_within_the_budget_and_the_session_disposes(string last)
    {
        var program = JoinBomb(last);
        Assert.True(program.Length < 600, program.Length.ToString());
        var evaluated = await HostileProbe.SurvivesAsync("evaluate", program);
        Assert.Contains(LimitCode, HostileProbe.Codes(evaluated));
        var report = await HostileProbe.SurvivesAsync("session", program);
        Assert.Contains(LimitCode, HostileProbe.Codes(report));
    }
}
