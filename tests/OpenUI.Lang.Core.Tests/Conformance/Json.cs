using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenUI.Lang.Core.Tests.Conformance;

/// <summary>
/// Structural JSON comparison with the semantics of the reference runners' isDeepStrictEqual:
/// object key order is ignored, array order is not, and numbers compare by value.
/// A missing key and a null value are different here; callers that want them equal say so.
/// </summary>
internal static class Json
{
    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Render(JsonNode? node) => node is null ? "null" : node.ToJsonString(Compact);

    public static bool Equal(JsonNode? a, JsonNode? b) => (a, b) switch
    {
        (null, null) => true,
        (null, _) or (_, null) => false,
        (JsonObject x, JsonObject y) =>
            x.Count == y.Count && x.All(p => y.TryGetPropertyValue(p.Key, out var other) && Equal(p.Value, other)),
        (JsonArray x, JsonArray y) => x.Count == y.Count && x.Zip(y).All(pair => Equal(pair.First, pair.Second)),
        (JsonValue x, JsonValue y) => ScalarsEqual(x, y),
        _ => false,
    };

    /// <summary>
    /// True when every key listed in <paramref name="expected"/> matches the same key in
    /// <paramref name="actual"/>, where an absent key counts as null. Extra actual keys are ignored.
    /// </summary>
    public static bool ListedKeysEqual(JsonObject expected, JsonObject actual) =>
        expected.All(p => Equal(p.Value, actual[p.Key]));

    public static void Diff(string path, JsonNode? expected, JsonNode? actual, List<Difference> sink)
    {
        switch (expected, actual)
        {
            case (JsonObject e, JsonObject a):
                DiffObjects(path, e, a, sink);
                break;
            case (JsonArray e, JsonArray a):
                for (var i = 0; i < Math.Min(e.Count, a.Count); i++)
                {
                    Diff($"{path}[{i}]", e[i], a[i], sink);
                }

                if (e.Count != a.Count)
                {
                    sink.Add(new Difference(path, $"length differs ({e.Count} expected, {a.Count} actual)", Render(e), Render(a)));
                }

                break;
            default:
                if (!Equal(expected, actual))
                {
                    sink.Add(new Difference(path, "differs", Render(expected), Render(actual)));
                }

                break;
        }
    }

    /// <summary>Diffs only the keys listed in <paramref name="expected"/>; an absent actual key reads as null.</summary>
    public static void DiffListedKeys(string path, JsonObject expected, JsonObject actual, List<Difference> sink)
    {
        foreach (var (key, value) in expected)
        {
            Diff(Child(path, key), value, actual[key], sink);
        }
    }

    private static void DiffObjects(string path, JsonObject expected, JsonObject actual, List<Difference> sink)
    {
        foreach (var (key, value) in expected)
        {
            if (actual.TryGetPropertyValue(key, out var other))
            {
                Diff(Child(path, key), value, other, sink);
            }
            else
            {
                sink.Add(new Difference(Child(path, key), "missing in actual", Render(value), null));
            }
        }

        foreach (var (key, value) in actual.Where(p => !expected.ContainsKey(p.Key)))
        {
            sink.Add(new Difference(Child(path, key), "unexpected in actual", null, Render(value)));
        }
    }

    private static string Child(string path, string key) => path.Length == 0 ? key : $"{path}.{key}";

    private static bool ScalarsEqual(JsonValue x, JsonValue y)
    {
        var kind = x.GetValueKind();
        if (kind != y.GetValueKind())
        {
            return false;
        }

        return kind switch
        {
            JsonValueKind.String => x.GetValue<string>() == y.GetValue<string>(),
            JsonValueKind.Number => AsDouble(x) == AsDouble(y),
            _ => true, // true, false and null carry nothing beyond their kind
        };
    }

    // Going through text avoids guessing which CLR type backs the node (int, long, double, JsonElement).
    private static double AsDouble(JsonValue number) =>
        double.Parse(number.ToJsonString(), CultureInfo.InvariantCulture);
}
