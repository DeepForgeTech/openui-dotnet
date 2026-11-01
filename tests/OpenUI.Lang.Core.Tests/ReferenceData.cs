using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenUI.Lang.Core.Tests;

/// <summary>
/// Loads the data tools/ecma-reference/generate.ts committed under the Ecma test folder. The files
/// are read from the source tree, so the tests do not need bun or node.
/// </summary>
internal static class ReferenceData
{
    private static readonly Lazy<string> TestsRoot = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "OpenUI.Lang.sln")))
            {
                return Path.Combine(dir.FullName, "tests", "OpenUI.Lang.Core.Tests");
            }
        }

        throw new InvalidOperationException("OpenUI.Lang.sln was not found above the test output directory.");
    });

    /// <summary>Reads <c>{area}/Data/{file}</c> as a JSON object.</summary>
    public static JsonObject Read(string area, string file) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(TestsRoot.Value, area, "Data", file)))!.AsObject();

    public static IEnumerable<JsonNode> Cases(string area, string file) =>
        Read(area, file)["cases"]!.AsArray().Select(node => node!);

    /// <summary>Decodes the 0x-prefixed IEEE 754 bit pattern the generator writes, so -0 and NaN survive JSON.</summary>
    public static double Double(string bits) =>
        BitConverter.UInt64BitsToDouble(ulong.Parse(bits.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    /// <summary>The bit pattern as the generator writes it. Every NaN maps to one pattern, since JS cannot tell NaNs apart.</summary>
    public static string Bits(double value)
    {
        const ulong canonicalNaN = 0x7ff8000000000000UL;
        var bits = double.IsNaN(value) ? canonicalNaN : BitConverter.DoubleToUInt64Bits(value);
        return "0x" + bits.ToString("x16", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Builds a runtime value from a generator descriptor (<c>{t:"array", items:[...]}</c> and so on).
    /// </summary>
    public static object? Value(JsonNode descriptor) => (string)descriptor["t"]! switch
    {
        "null" => null,
        "string" => (string)descriptor["v"]!,
        "number" => Double((string)descriptor["bits"]!),
        "bool" => (bool)descriptor["v"]!,
        "array" => descriptor["items"]!.AsArray().Select(item => Value(item!)).ToList(),
        "object" => ToObject(descriptor["fields"]?.AsObject()),
        var other => throw new InvalidDataException($"Unknown descriptor type {other}."),
    };

    private static OrderedDictionary<string, object?> ToObject(JsonObject? fields)
    {
        var result = new OrderedDictionary<string, object?>();
        foreach (var (key, value) in fields ?? [])
        {
            result[key] = Value(value!);
        }

        return result;
    }
}
