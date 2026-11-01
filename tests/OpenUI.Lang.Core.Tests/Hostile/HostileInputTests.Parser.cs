using System.Text;
using OpenUI.Lang.Runtime;

namespace OpenUI.Lang.Core.Tests.Hostile;

public partial class HostileInputTests
{
    // Each paren level nests chains at six precedence levels, so a per-level counter lets the tree
    // grow far past the limit while every counter stays under it.
    private static string WideSyntaxBomb(int levels)
    {
        var expression = "a";
        string[] operators = ["*", "+", "<", "==", "&&", "||"];
        for (var depth = levels - 1; depth >= 0; depth--)
        {
            var allowed = 126 - depth;
            var next = new StringBuilder("(").Append(expression).Append(')').Append(Repeat("*a", allowed)).ToString();
            for (var i = 1; i < operators.Length; i++)
            {
                next = "a" + operators[i] + next + Repeat(operators[i] + "a", allowed - 1);
            }

            expression = next;
        }

        return expression;
    }

    [Fact]
    public async Task A_statement_too_deep_for_the_tree_walkers_costs_only_itself()
    {
        var program = "first = Text(\"one\")\nroot = Stack([first, later])\nbad = " + WideSyntaxBomb(60) + "\nlater = Text(\"two\")\nlast = Text(\"three\")\n";
        var report = await HostileProbe.SurvivesAsync("parse", program);
        var names = report["statements"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        Assert.Contains("later", names);
        Assert.Contains("last", names);
        Assert.DoesNotContain("bad", names);
        Assert.Contains(LimitCode, HostileProbe.Codes(report));
    }

    [Theory]
    [InlineData("merge")]
    [InlineData("edit")]
    public async Task A_statement_too_deep_for_the_tree_walkers_does_not_escape_an_edit(string scenario)
    {
        var report = await HostileProbe.SurvivesAsync(scenario, "root = Stack([bad])\nbad = " + WideSyntaxBomb(60) + "\nlater = 1\n");
        Assert.Contains("root", report["statements"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Theory]
    [InlineData("Tag(TALL, 1)")]
    [InlineData("Tag(1, TALL)")]
    [InlineData("[TALL, 1]")]
    [InlineData("[1, TALL]")]
    [InlineData("{a: TALL, b: 1}")]
    [InlineData("{a: 1, b: TALL}")]
    [InlineData("-(TALL)")]
    [InlineData("((TALL) ? 1 : 2)")]
    [InlineData("(c ? TALL : 2)")]
    [InlineData("(c ? 1 : TALL)")]
    [InlineData("(TALL).x")]
    [InlineData("(TALL)[0]")]
    [InlineData("x[TALL]")]
    [InlineData("($s = TALL)")]
    [InlineData("(TALL)")]
    public void A_statements_height_does_not_depend_on_where_its_tall_part_sits(string shape)
    {
        static bool Kept(string shape, int chain)
        {
            var left = shape.Replace("TALL", "a" + Repeat("+a", chain), StringComparison.Ordinal);
            var document = new OpenUIParser(Library).Parse("root = Stack([])\nbad = " + left + Repeat("+a", chain) + "\n");
            return document.Statements.Any(s => s.Name == "bad");
        }

        Assert.True(Kept(shape, 50));
        Assert.False(Kept(shape, 100));
    }

    [Fact]
    public async Task An_edit_uses_the_session_limits_for_its_statements()
    {
        var limits = OpenUILimits.Default with { MaxSyntaxDepth = 400 };
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Limits = limits });
        session.Load("root = Stack([Text(\"page\")])");

        var merged = session.ApplyEdit("root = Stack([Text(\"page\"), deep])\ndeep = " + Repeat("[", 200) + "1" + Repeat("]", 200));

        Assert.Contains("deep", merged.Statements);
    }
}
