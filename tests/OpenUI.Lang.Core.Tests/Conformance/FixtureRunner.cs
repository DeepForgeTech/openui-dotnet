using System.Text.Json.Nodes;

namespace OpenUI.Lang.Core.Tests.Conformance;

/// <summary>Runs one fixture case through the adapter and compares the result with what the case expects.</summary>
internal static class FixtureRunner
{
    /// <summary>Level 1. A streaming case must match both when streamed and when parsed in one go.</summary>
    public static IReadOnlyList<Difference> Program(FixtureCase fixture)
    {
        if (fixture.IsValidation)
        {
            return Validation(fixture);
        }

        var expected = FixtureCatalog.ReadObject(fixture.PathOf("expected.json"));
        var direct = ProgramComparer.Compare(expected, ConformanceAdapter.RunProgram(fixture.Folder, FixtureCatalog.LoadLibrary()));
        if (fixture.IsStreaming)
        {
            var joined = string.Concat(ReadChunks(fixture));
            var batch = ProgramComparer.Compare(expected, ConformanceAdapter.RunSource(joined, FixtureCatalog.LoadLibrary()));
            return [.. direct.Select(d => d.Under("streamed")), .. batch.Select(d => d.Under("batch"))];
        }

        return fixture.Name.Contains("-roundtrip-") ? [.. direct, .. RoundTrip(fixture, expected)] : direct;
    }

    // The serialized tree must parse back to the same root, on top of the normal checks.
    private static IReadOnlyList<Difference> RoundTrip(FixtureCase fixture, JsonObject expected)
    {
        var source = File.ReadAllText(fixture.PathOf("input.oui"));
        var again = ConformanceAdapter.RunRoundTrip(source, FixtureCatalog.LoadLibrary());
        return ProgramComparer.CompareRoot(expected, again).Select(d => d.Under("roundtrip")).ToList();
    }

    /// <summary>Level 2.</summary>
    public static IReadOnlyList<Difference> Interactions(FixtureCase fixture)
    {
        var expected = FixtureCatalog.ReadObject(fixture.PathOf("interactions.json"))["expected"] as JsonObject
            ?? throw new InvalidDataException($"{fixture.Id}: interactions.json has no \"expected\" object.");
        return InteractionComparer.Compare(expected, ConformanceAdapter.RunInteractions(fixture.Folder, FixtureCatalog.LoadLibrary()));
    }

    /// <summary>Language.md section 4: streaming equals parsing, tested with the worst chunking.</summary>
    public static IReadOnlyList<Difference> CharByChar(FixtureCase fixture)
    {
        var source = ProgramTextOf(fixture);
        var whole = ConformanceAdapter.RunSource(source, FixtureCatalog.LoadLibrary());

        // Runes, not UTF-16 units: a chunk boundary inside a surrogate pair is not valid text.
        var chunks = source.EnumerateRunes().Select(r => r.ToString()).ToList();
        var streamed = ConformanceAdapter.RunChunks(chunks, FixtureCatalog.LoadLibrary());

        return ProgramComparer.Compare(WithoutMessageText(whole), streamed);
    }

    // The text a case parses: the joined chunks of a streaming case, the merged text of an editing case.
    internal static string ProgramTextOf(FixtureCase fixture)
    {
        if (fixture.IsStreaming)
        {
            return string.Concat(ReadChunks(fixture));
        }

        var input = File.ReadAllText(fixture.PathOf("input.oui"));
        return fixture.IsEditing ? ConformanceAdapter.MergedText(input, File.ReadAllText(fixture.PathOf("patch.oui"))) : input;
    }

    // message and hint are text for the model; the README says no runner compares them.
    private static JsonObject WithoutMessageText(JsonObject result)
    {
        var copy = (JsonObject)result.DeepClone();
        if (copy["errorObjects"] is JsonArray errorObjects)
        {
            foreach (var error in errorObjects.OfType<JsonObject>())
            {
                error.Remove("message");
                error.Remove("hint");
            }
        }

        return copy;
    }

    private static IReadOnlyList<Difference> Validation(FixtureCase fixture)
    {
        var cases = FixtureCatalog.ReadObject(fixture.PathOf("validation.json"))["cases"] as JsonArray
            ?? throw new InvalidDataException($"{fixture.Id}: validation.json has no \"cases\" array.");

        var differences = new List<Difference>();
        for (var i = 0; i < cases.Count; i++)
        {
            var entry = cases[i] as JsonObject ?? throw new InvalidDataException($"{fixture.Id}: cases[{i}] is not an object.");
            var rules = (JsonObject)entry["rules"]!.DeepClone();
            var value = entry["value"]?.DeepClone();
            var valid = entry["valid"]!.GetValue<bool>();

            var actual = ConformanceAdapter.RunValidation(rules, value);
            if (actual != valid)
            {
                differences.Add(new Difference(
                    $"cases[{i}]",
                    $"pass/fail differs for rules {Json.Render(rules)} on value {Json.Render(value)}",
                    Json.Render(JsonValue.Create(valid)),
                    Json.Render(JsonValue.Create(actual))));
            }
        }

        return differences;
    }

    private static List<string> ReadChunks(FixtureCase fixture) =>
        (JsonNode.Parse(File.ReadAllText(fixture.PathOf("chunks.json"))) as JsonArray
            ?? throw new InvalidDataException($"{fixture.Id}: chunks.json is not an array."))
        .Select(chunk => chunk!.GetValue<string>())
        .ToList();
}
