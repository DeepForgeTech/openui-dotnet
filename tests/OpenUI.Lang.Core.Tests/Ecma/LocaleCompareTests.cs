using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Ecma;

/// <summary>
/// localeCompare against Node's ICU. locale-compare.json holds the sign of every pair, and the order
/// a JS sort with localeCompare produces. .NET resolves collation through the system ICU, so a host
/// whose ICU differs from Node's CLDR version can fail here; that is a real gap, not a test problem.
/// </summary>
public class LocaleCompareTests
{
    private static readonly System.Text.Json.Nodes.JsonObject Data = ReferenceData.Read("Ecma", "locale-compare.json");

    private static readonly List<string> Strings = Data["strings"]!.AsArray().Select(node => (string)node!).ToList();

    private static char Sign(int comparison) => comparison < 0 ? '<' : comparison > 0 ? '>' : '=';

    [Fact]
    public void Collation_is_available_on_this_host() => Assert.True(OuiCollator.IsLinguistic);

    [Fact]
    public void LocaleCompare_matches_JavaScript_for_every_pair()
    {
        var rows = Data["matrix"]!.AsArray().Select(node => (string)node!).ToList();
        var failures = new List<string>();

        for (var i = 0; i < Strings.Count; i++)
        {
            for (var j = 0; j < Strings.Count; j++)
            {
                var actual = Sign(OuiCollator.Default.Compare(Strings[i], Strings[j]));
                if (actual != rows[i][j])
                {
                    failures.Add($"{Escape(Strings[i])} vs {Escape(Strings[j])}: JS '{rows[i][j]}', got '{actual}'");
                }
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} mismatches:\n{string.Join('\n', failures.Take(20))}");
    }

    [Fact]
    public void A_stable_sort_gives_the_JavaScript_order()
    {
        // List.Sort is unstable, and JS sort is stable, so ties would otherwise come out in a different order.
        var expected = Data["sorted"]!.AsArray().Select(node => (string)node!).ToList();
        var actual = Strings.OrderBy(s => s, Comparer<string>.Create(OuiCollator.Default.Compare)).ToList();

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("a", "B", -1)]
    [InlineData("B", "a", 1)]
    [InlineData("a", "A", -1)]
    [InlineData("e", "é", -1)]
    [InlineData("10", "9", -1)]
    [InlineData("", "a", -1)]
    [InlineData("a", "a", 0)]
    public void LocaleCompare_orders_the_common_cases(string left, string right, int expected) =>
        Assert.Equal(expected, OuiCollator.Default.Compare(left, right));

    private static string Escape(string text) =>
        string.Concat(text.Select(c => c < 0x20 || c > 0x7e ? $"\\u{(int)c:x4}" : c.ToString()));
}
