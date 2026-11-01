using System.Collections.Immutable;
using System.Text.Json;

namespace OpenUI.Lang.Prompting;

public sealed partial record PromptOptions
{
    /// <summary>Reads options from their JSON form, the <c>promptOptions</c> a LibrarySpec file can carry (prompt.md, section 4.2). Null members and unknown keys count as unset.</summary>
    /// <param name="options">A JSON object.</param>
    /// <exception cref="ArgumentException">The value is not an object, or a known option has the wrong JSON type.</exception>
    public static PromptOptions FromJson(JsonElement options)
    {
        if (options.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("'promptOptions' must be an object.", nameof(options));
        }

        return new PromptOptions
        {
            ToolCalls = Bool(options, "toolCalls"),
            Bindings = Bool(options, "bindings"),
            BuiltinFunctions = Bool(options, "builtinFunctions"),
            EditMode = Bool(options, "editMode") ?? false,
            InlineMode = Bool(options, "inlineMode") ?? false,
            Tools = Array(options, "tools", Tool),
            Preamble = Text(options, "preamble"),
            Examples = Array(options, "examples", item => AsText(item, "examples")),
            ToolExamples = Array(options, "toolExamples", item => AsText(item, "toolExamples")),
            AdditionalRules = Array(options, "additionalRules", item => AsText(item, "additionalRules")),
        };
    }

    // A tool is a bare name, or an object with a name and the JSON Schemas of its arguments and result.
    private static ToolDescriptor Tool(JsonElement tool)
    {
        if (tool.ValueKind == JsonValueKind.String)
        {
            return tool.GetString()!;
        }

        if (tool.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("A tool must be a string or an object.");
        }

        return new ToolDescriptor(
            Text(tool, "name") ?? throw new ArgumentException("'tools[].name' is required."),
            Text(tool, "description"),
            Member(tool, "inputSchema"),
            Member(tool, "outputSchema"),
            Member(tool, "annotations"));
    }

    private static JsonElement? Member(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.Clone() : null;

    private static bool? Bool(JsonElement parent, string name) =>
        Member(parent, name) is { } value
            ? value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : throw new ArgumentException($"'{name}' has the wrong JSON type.")
            : null;

    private static string? Text(JsonElement parent, string name) =>
        Member(parent, name) is { } value ? AsText(value, name) : null;

    private static string AsText(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new ArgumentException($"'{name}' has the wrong JSON type.");

    private static ImmutableArray<T> Array<T>(JsonElement parent, string name, Func<JsonElement, T> read) =>
        Member(parent, name) is { } value
            ? value.ValueKind == JsonValueKind.Array
                ? [.. value.EnumerateArray().Select(read)]
                : throw new ArgumentException($"'{name}' must be an array.")
            : [];
}
