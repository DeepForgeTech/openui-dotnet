using System.Text.Json.Nodes;

namespace OpenUI.Lang.Core.Tests.Conformance;

/// <summary>
/// Level 1 comparison of a program case's actual result against its expected.json
/// (tests/fixtures/1.0/README.md, "Comparison").
/// </summary>
internal static class ProgramComparer
{
    private static readonly string[] AlwaysCompared = ["root", "errors", "unresolved", "orphans", "incomplete"];

    // Compared only when expected.json carries them, so most cases need not spell them out.
    private static readonly string[] CompareWhenExpected = ["state", "statements", "entry", "errorObjects"];

    public static IReadOnlyList<Difference> Compare(JsonObject expected, JsonObject actual)
    {
        var differences = new List<Difference>();
        foreach (var key in AlwaysCompared.Concat(CompareWhenExpected.Where(expected.ContainsKey)))
        {
            var want = expected[key];
            if (!actual.TryGetPropertyValue(key, out var got))
            {
                differences.Add(new Difference(key, "missing in actual", Json.Render(want), null));
                continue;
            }

            switch (key)
            {
                case "errors":
                    CompareAsMultiset(key, want, got, requireEveryActualClaimed: true, differences);
                    break;
                case "errorObjects":
                    // The reference runner does not require equal counts here, only that every
                    // expected object is found; the README says no more than "listed keys only".
                    CompareAsMultiset(key, want, got, requireEveryActualClaimed: false, differences);
                    break;
                case "unresolved" or "orphans":
                    CompareAsSet(key, want, got, differences);
                    break;
                default:
                    Json.Diff(key, want, got, differences);
                    break;
            }
        }

        return differences;
    }

    /// <summary>Compares only <c>root</c>, exactly. Used for the tree that survives a serialize and re-parse.</summary>
    public static IReadOnlyList<Difference> CompareRoot(JsonObject expected, JsonObject actual)
    {
        var differences = new List<Difference>();
        if (actual.TryGetPropertyValue("root", out var got))
        {
            Json.Diff("root", expected["root"], got, differences);
        }
        else
        {
            differences.Add(new Difference("root", "missing in actual", Json.Render(expected["root"]), null));
        }

        return differences;
    }

    /// <summary>
    /// Each expected entry must claim its own actual entry that agrees on the keys it lists, so an
    /// entry without <c>statementId</c> matches any statement. This is a maximum bipartite
    /// matching rather than a greedy pass: a wildcard entry must not starve a more specific one.
    /// </summary>
    private static void CompareAsMultiset(
        string path, JsonNode? want, JsonNode? got, bool requireEveryActualClaimed, List<Difference> differences)
    {
        if (want is not JsonArray expected || got is not JsonArray actual)
        {
            Json.Diff(path, want, got, differences);
            return;
        }

        var claimedBy = new int[actual.Count];
        Array.Fill(claimedBy, -1);

        bool Claim(int e, bool[] visited)
        {
            for (var a = 0; a < actual.Count; a++)
            {
                if (visited[a] || !Agrees(expected[e], actual[a]))
                {
                    continue;
                }

                visited[a] = true;
                if (claimedBy[a] < 0 || Claim(claimedBy[a], visited))
                {
                    claimedBy[a] = e;
                    return true;
                }
            }

            return false;
        }

        var claimed = Enumerable.Range(0, expected.Count).Where(e => Claim(e, new bool[actual.Count])).ToHashSet();

        foreach (var e in Enumerable.Range(0, expected.Count).Where(e => !claimed.Contains(e)))
        {
            differences.Add(new Difference($"{path}[{e}]", "no actual entry matches", Json.Render(expected[e]), Json.Render(actual)));
        }

        if (!requireEveryActualClaimed)
        {
            return;
        }

        foreach (var a in Enumerable.Range(0, actual.Count).Where(a => claimedBy[a] < 0))
        {
            differences.Add(new Difference($"{path}[actual {a}]", "unexpected entry", Json.Render(expected), Json.Render(actual[a])));
        }
    }

    private static bool Agrees(JsonNode? expected, JsonNode? actual) =>
        expected is JsonObject e && actual is JsonObject a ? Json.ListedKeysEqual(e, a) : Json.Equal(expected, actual);

    // Elements are names, so rendering each one is a faithful identity.
    private static void CompareAsSet(string path, JsonNode? want, JsonNode? got, List<Difference> differences)
    {
        if (want is not JsonArray expected || got is not JsonArray actual)
        {
            Json.Diff(path, want, got, differences);
            return;
        }

        var wanted = expected.Select(Json.Render).Distinct().ToList();
        var present = actual.Select(Json.Render).Distinct().ToList();
        foreach (var missing in wanted.Except(present))
        {
            differences.Add(new Difference(path, "expected member missing from actual", missing, Json.Render(actual)));
        }

        foreach (var extra in present.Except(wanted))
        {
            differences.Add(new Difference(path, "unexpected member in actual", Json.Render(expected), extra));
        }
    }
}
