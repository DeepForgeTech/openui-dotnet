using System.Text.Json;
using System.Text.Json.Nodes;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Editing;
using OpenUI.Lang.Library;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Conformance;

/// <summary>
/// The seam between the fixture runner and the library. The runner loads cases and compares
/// results; this class turns one case into the canonical JSON of tests/fixtures/1.0/README.md.
/// </summary>
internal static partial class ConformanceAdapter
{
    /// <summary>
    /// Level 1. Parses the case's input.oui (or streams chunks.json, or merges patch.oui for editing
    /// cases) and returns root, errors, unresolved, orphans and incomplete, plus state, statements,
    /// entry and errorObjects.
    /// </summary>
    public static JsonObject RunProgram(string caseDirectory, JsonObject library)
    {
        var chunks = Path.Combine(caseDirectory, "chunks.json");
        if (File.Exists(chunks))
        {
            return RunChunks([.. JsonNode.Parse(File.ReadAllText(chunks))!.AsArray().Select(c => c!.GetValue<string>())], library);
        }

        var input = File.ReadAllText(Path.Combine(caseDirectory, "input.oui"));
        var patch = Path.Combine(caseDirectory, "patch.oui");
        if (!File.Exists(patch))
        {
            return RunSource(input, library);
        }

        var merged = ProgramMerger.Merge(input, File.ReadAllText(patch));
        var result = RunSource(merged.Text, library);
        result["statements"] = Strings(merged.Statements);
        return result;
    }

    /// <summary>The text an editing case parses: <paramref name="patch"/> merged into <paramref name="input"/>.</summary>
    public static string MergedText(string input, string patch) => ProgramMerger.Merge(input, patch).Text;

    /// <summary>Parses <paramref name="source"/> in one go and returns the canonical result.</summary>
    public static JsonObject RunSource(string source, JsonObject library) =>
        Canonical(new OpenUIParser(CreateLibrary(library)).Parse(source));

    /// <summary>Streams each chunk, ends the stream, and returns the canonical result.</summary>
    public static JsonObject RunChunks(IReadOnlyList<string> chunks, JsonObject library)
    {
        var stream = new OpenUIParser(CreateLibrary(library)).CreateStream();
        foreach (var chunk in chunks)
        {
            stream.Append(chunk);
        }

        return Canonical(stream.Complete());
    }

    /// <summary>
    /// Parses <paramref name="source"/>, serializes the rendered tree (language.md, section 9.2),
    /// parses that again and returns the canonical result of the second parse.
    /// </summary>
    public static JsonObject RunRoundTrip(string source, JsonObject library)
    {
        var root = OpenUIEvaluator.Evaluate(new OpenUIParser(CreateLibrary(library)).Parse(source)).Root;
        return RunSource(root is null ? "" : OpenUISerializer.Serialize(root), library);
    }

    /// <summary>The test library: library.json plus its one function, <c>@Upper</c>, and the reference form traits.</summary>
    internal static OpenUILibrary CreateLibrary(JsonObject spec) =>
        OpenUILibrary.FromSpec(LibrarySpec.FromJson(JsonSerializer.SerializeToElement(spec)), implement => implement
            .Function("Upper", (in FunctionArguments args) => OpenUI.Lang.Values.Ecma.ToUpperCase(OpenUI.Lang.Values.Ecma.ToString(args["text"])))
            .Traits("Form", new ComponentTraits(FormNameProp: "name"))
            .Traits("Input", new ComponentTraits(FieldNameProp: "name", FieldComponentType: "input", FieldRulesProp: "rules")));

    /// <summary>The canonical result at stream end: no query has returned, so queries read their defaults and mutations are idle.</summary>
    private static JsonObject Canonical(OpenUIDocument document)
    {
        var render = OpenUIEvaluator.Evaluate(document);
        var errorObjects = render.Errors.Select(ErrorJson).ToList();
        return new JsonObject
        {
            ["root"] = Summarize(render.Root),
            ["errors"] = new JsonArray([.. errorObjects.Select(e => Pick(e, "code", "statementId"))]),
            ["errorObjects"] = new JsonArray([.. errorObjects]),
            ["unresolved"] = Strings(document.Unresolved),
            ["orphans"] = Strings(document.Orphans),
            ["incomplete"] = document.Incomplete,
            ["state"] = new JsonObject([.. render.StateDefaults.Select(p => KeyValuePair.Create(p.Key, Summarize(p.Value)))]),
            ["statements"] = Strings(document.Statements.Select(s => s.Name)),
            ["entry"] = document.Entry?.Name,
        };
    }

    private static JsonArray Strings(IEnumerable<string> names) => new([.. names.Select(n => (JsonNode?)JsonValue.Create(n))]);

    private static JsonObject Pick(JsonObject source, params string[] keys)
    {
        var picked = new JsonObject();
        foreach (var key in keys.Where(source.ContainsKey))
        {
            picked[key] = source[key]?.DeepClone();
        }

        return picked;
    }

    private static JsonObject ErrorJson(OpenUIError error)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            error.WriteTo(writer);
        }

        return JsonNode.Parse(buffer.ToArray())!.AsObject();
    }

    /// <summary>A value in the summary form of the README, which <see cref="OuiValue.WriteTo"/> writes.</summary>
    internal static JsonNode? Summarize(OuiValue value)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            value.WriteTo(writer);
        }

        return JsonNode.Parse(buffer.WrittenSpan);
    }
}
