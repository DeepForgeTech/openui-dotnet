using System.Text.Json.Nodes;

namespace OpenUI.Lang.Core.Tests.Conformance;

/// <summary>
/// Level 2 comparison of what a replayed interactions.json produced against its <c>expected</c>
/// object (tests/fixtures/1.0/README.md, "Action and form cases").
/// </summary>
internal static class InteractionComparer
{
    public static IReadOnlyList<Difference> Compare(JsonObject expected, JsonObject actual)
    {
        var differences = new List<Difference>();
        CompareEvents(ArrayOf(expected, "events"), ArrayOf(actual, "events"), differences);
        CompareState(expected["state"] as JsonObject, actual["state"] as JsonObject, differences);

        // toolCalls extends Appendix B, so an expected without it says nothing about tool calls.
        if (expected["toolCalls"] is JsonArray wantCalls)
        {
            CompareToolCalls(wantCalls, ArrayOf(actual, "toolCalls"), differences);
        }

        return differences;
    }

    private static JsonArray ArrayOf(JsonObject owner, string key) => owner[key] as JsonArray ?? [];

    // An expected key of null equals an absent or null actual key, so "formName": null means no form.
    private static void CompareEvents(JsonArray expected, JsonArray actual, List<Difference> differences)
    {
        if (expected.Count != actual.Count)
        {
            differences.Add(new Difference(
                "events", $"event count differs ({expected.Count} expected, {actual.Count} actual)", Json.Render(expected), Json.Render(actual)));
        }

        for (var i = 0; i < Math.Min(expected.Count, actual.Count); i++)
        {
            if (expected[i] is JsonObject want && actual[i] is JsonObject got)
            {
                Json.DiffListedKeys($"events[{i}]", want, got, differences);
            }
            else
            {
                Json.Diff($"events[{i}]", expected[i], actual[i], differences);
            }
        }
    }

    private static void CompareState(JsonObject? expected, JsonObject? actual, List<Difference> differences)
    {
        if (expected is not null)
        {
            Json.DiffListedKeys("state", expected, actual ?? [], differences);
        }
    }

    private static void CompareToolCalls(JsonArray expected, JsonArray actual, List<Difference> differences)
    {
        var distinct = new JsonArray();
        foreach (var call in actual)
        {
            if (distinct.Count == 0 || !Json.Equal(call, distinct[^1]))
            {
                distinct.Add(call?.DeepClone());
            }
        }

        Json.Diff("toolCalls", expected, distinct, differences);
    }
}
