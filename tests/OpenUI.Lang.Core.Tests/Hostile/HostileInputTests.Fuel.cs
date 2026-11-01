namespace OpenUI.Lang.Core.Tests.Hostile;

public partial class HostileInputTests
{
    private static readonly TimeSpan FuelBudget = TimeSpan.FromSeconds(20);

    public static TheoryData<string, string> UnchargedWork()
    {
        const string Keep = "root = Stack([Text(@Count(arr) + \"\")])\n";

        // Half the value-node limit: a value over the limit stops on size before any loop runs.
        var big = "big = " + BigArray(50_000) + "\n";
        var rows = "c = [" + string.Join(", ", Enumerable.Range(0, 200).Select(i => "[" + i + " " + Repeat("1 ", 400) + "]")) + "]\nn = " + BigArray(10) + "\n";
        var doubled = Doublings(0).Replace("\"x\"", "\"ffffffff\"") + string.Concat(Enumerable.Range(1, 16).Select(i => $"s{i} = s{i - 1} + s{i - 1}\n"));
        return new TheoryData<string, string>
        {
            { "loose equality stringifies", Keep + big + "arr = @Each(big, \"i\", big == \"x\")\n" },
            { "concatenation stringifies", Keep + big + "arr = @Each(big, \"i\", @Count(\"\" + big))\n" },
            { "sum", Keep + big + "arr = @Each(big, \"i\", @Sum(big))\n" },
            { "average", Keep + big + "arr = @Each(big, \"i\", @Avg(big))\n" },
            { "minimum", Keep + big + "arr = @Each(big, \"i\", @Min(big))\n" },
            { "maximum", Keep + big + "arr = @Each(big, \"i\", @Max(big))\n" },
            { "pluck", Keep + big + "arr = @Each(big, \"i\", @Count(big.x))\n" },
            { "prop validation", Keep + big + "t = Text(\"x\")\nkids = @Each(" + BigArray(30_000) + ", \"i\", t)\narr = @Each(big, \"i\", @Count([Stack(kids)]))\n" },
            { "filter", Keep + big + "arr = @Each(big, \"i\", @Count(@Filter(big, \"\", \"~\", 1)))\n" },
            { "filter path", Keep + big + "arr = @Filter(big, \"" + new string('.', 250_000) + "\", \"==\", 1)\n" },
            { "sort stringifies", Keep + rows + "arr = @Each(n, \"i\", @Count(@Sort(c)))\n" },
            { "hex digits in a loop", Keep + big + doubled + "h = \"0x\" + s16\narr = @Each(big, \"i\", h * 1)\n" },
            { "state default walks", Keep + big + "$s = big\narr = @Each(big, \"i\", @Sum($s))\n" },
        };
    }

    [Theory]
    [MemberData(nameof(UnchargedWork))]
    public async Task Work_inside_one_step_is_charged_to_the_fuel_budget(string shape, string program)
    {
        var report = await HostileProbe.SurvivesAsync("evaluate", program, FuelBudget);
        Assert.True(HostileProbe.Messages(report).Contains("ran out of fuel", StringComparison.Ordinal), $"{shape}: {HostileProbe.Messages(report)}");
    }

    [Fact]
    public async Task A_hex_string_of_many_digits_reads_in_bounded_time_and_keeps_its_value()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(double.PositiveInfinity, OpenUI.Lang.Values.Ecma.StringToNumber("0x" + new string('f', 400_000)));
        Assert.Equal(255, OpenUI.Lang.Values.Ecma.StringToNumber("0x" + new string('0', 400_000) + "ff"));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), $"Took {clock.Elapsed}.");
    }

    [Fact]
    public async Task One_click_on_an_action_of_many_steps_finishes_in_bounded_time()
    {
        var program = "root = Stack([go])\nq = Query(\"t\", {}, {})\nbig = " + BigArray(20_000) + "\ngo = Button(\"Go\", Action(@Each(big, \"i\", @Run(q))))\n";
        await HostileProbe.SurvivesAsync("click", program, FuelBudget);
    }
}
