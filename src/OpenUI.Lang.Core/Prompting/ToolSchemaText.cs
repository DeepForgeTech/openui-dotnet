using System.Globalization;
using System.Text;
using System.Text.Json;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Prompting;

/// <summary>
/// Prints a tool's JSON Schemas for the "Available Tools" section (prompt.md, section 4.2): a type
/// string per schema and a default value per output schema. The reference generator is JavaScript, so
/// this follows JavaScript's rules where they show: property order (<c>Object.keys</c>), value text
/// (template literals) and string escaping (<c>JSON.stringify</c>).
/// </summary>
internal static class ToolSchemaText
{
    /// <summary>The parameter list of a tool, such as <c>status: string, limit?: number</c>.</summary>
    public static string Params(JsonElement? inputSchema) =>
        inputSchema is { } schema ? string.Join(", ", Fields(schema)) : "";

    /// <summary>The printed type of a schema, such as <c>{id: string, tags?: string[]}</c>.</summary>
    public static string TypeString(JsonElement schema)
    {
        var type = schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("type", out var value) ? value : default;
        if (type.ValueKind != JsonValueKind.String)
        {
            return "any";
        }

        return type.GetString() switch
        {
            "string" when schema.TryGetProperty("enum", out var values) && values.ValueKind == JsonValueKind.Array =>
                string.Join(" | ", values.EnumerateArray().Select(v => $"\"{TemplateText(v)}\"")),
            "string" => "string",
            "number" or "integer" => "number",
            "boolean" => "boolean",
            "array" => (schema.TryGetProperty("items", out var items) ? TypeString(items) : "any") + "[]",
            "object" when Properties(schema).Any() => "{" + string.Join(", ", Fields(schema)) + "}",
            "object" => "object",
            _ => "any",
        };
    }

    /// <summary>The minimal default value of a schema as compact JSON, such as <c>{"total":0,"rows":[]}</c>.</summary>
    public static string DefaultJson(JsonElement schema)
    {
        var json = new StringBuilder();
        WriteDefault(json, schema);
        return json.ToString();
    }

    /// <summary>
    /// The properties of an object schema in <c>Object.keys</c> order: array-index keys ascending by
    /// value, then the other keys in document order.
    /// </summary>
    public static IEnumerable<JsonProperty> JsOrder(JsonElement properties)
    {
        var all = properties.EnumerateObject().ToList();
        var indices = all.Where(p => OuiObject.IsArrayIndex(p.Name, out _)).OrderBy(p => ArrayIndex(p.Name));
        return indices.Concat(all.Where(p => !OuiObject.IsArrayIndex(p.Name, out _)));
    }

    /// <summary>A string as <c>JSON.stringify</c> writes it, quotes included.</summary>
    public static string Quote(string text)
    {
        var json = new StringBuilder("\"");
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var pairedSurrogate = char.IsHighSurrogate(c) ? i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])
                : char.IsLowSurrogate(c) && i > 0 && char.IsHighSurrogate(text[i - 1]);
            json.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                < ' ' => Unicode(c),
                _ when char.IsSurrogate(c) && !pairedSurrogate => Unicode(c),
                _ => c.ToString(),
            });
        }

        return json.Append('"').ToString();

        static string Unicode(char c) => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture);
    }

    private static IEnumerable<string> Fields(JsonElement schema)
    {
        var required = schema.TryGetProperty("required", out var names) && names.ValueKind == JsonValueKind.Array
            ? names.EnumerateArray().Where(n => n.ValueKind == JsonValueKind.String).Select(n => n.GetString()!).ToHashSet(StringComparer.Ordinal)
            : [];
        return Properties(schema).Select(p => $"{p.Name}{(required.Contains(p.Name) ? "" : "?")}: {TypeString(p.Value)}");
    }

    private static IEnumerable<JsonProperty> Properties(JsonElement schema) =>
        schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out var properties)
            && properties.ValueKind == JsonValueKind.Object
            ? JsOrder(properties)
            : [];

    private static void WriteDefault(StringBuilder json, JsonElement schema)
    {
        var type = schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("type", out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        switch (type)
        {
            case "string":
                json.Append("\"\"");
                break;
            case "number" or "integer":
                json.Append('0');
                break;
            case "boolean":
                json.Append("false");
                break;
            case "array":
                json.Append("[]");
                break;
            case "object":
                WriteDefaultObject(json, schema);
                break;
            default:
                json.Append("null");
                break;
        }
    }

    // JavaScript drops "__proto__" when it builds the default object by assignment.
    private static void WriteDefaultObject(StringBuilder json, JsonElement schema)
    {
        json.Append('{');
        var first = true;
        foreach (var property in Properties(schema).Where(p => p.Name != "__proto__"))
        {
            json.Append(first ? "" : ",").Append(Quote(property.Name)).Append(':');
            WriteDefault(json, property.Value);
            first = false;
        }

        json.Append('}');
    }

    // A JSON value as a JavaScript template literal prints it.
    private static string TemplateText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Number => Ecma.NumberToString(value.GetDouble()),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        _ => value.GetRawText(),
    };

    private static uint ArrayIndex(string key)
    {
        OuiObject.IsArrayIndex(key, out var index);
        return index;
    }
}
