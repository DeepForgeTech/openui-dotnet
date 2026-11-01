using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using OpenUI.Lang.Prompting;

namespace OpenUI.Lang.Library;

/// <summary>One entry of the LibrarySpec's <c>components</c>, <c>functions</c> or <c>actions</c> (prompt.md, section 2).</summary>
/// <param name="Name">The key: the component, function or action name.</param>
/// <param name="Signature">The derived signature line (prompt.md, section 5.1).</param>
/// <param name="Description">The description, or null when the entry has none.</param>
public sealed record LibrarySpecEntry(string Name, string Signature, string? Description);

/// <summary>A named group of components that splits the prompt's component list (prompt.md, section 4.4).</summary>
/// <param name="Name">The group title.</param>
/// <param name="Components">The component names, in order.</param>
/// <param name="Notes">The group's notes, or null when the group has no <c>notes</c> key.</param>
public sealed record ComponentGroup(string Name, ImmutableArray<string> Components, ImmutableArray<string>? Notes = null);

/// <summary>
/// The LibrarySpec document (prompt.md, section 2), read and written without loss. Every ordered map
/// (<c>components</c>, the schema's <c>properties</c>) keeps its key order, because order is meaning:
/// it is the prompt's listing order and the positional order of arguments (section 2.3).
/// </summary>
public sealed record LibrarySpec
{
    /// <summary>The library name (section 2.1), or null.</summary>
    public string? Id { get; init; }

    /// <summary>The library version (section 2.1), or null.</summary>
    public string? Version { get; init; }

    /// <summary>The root component name (section 2.2), or null.</summary>
    public string? Root { get; init; }

    /// <summary>The validation schema (section 2.3), or null when the document has none.</summary>
    public JsonElement? Schema { get; init; }

    /// <summary>The <c>components</c> entries in document order (section 2.4).</summary>
    public ImmutableArray<LibrarySpecEntry> Components { get; init; } = [];

    /// <summary>The <c>componentGroups</c> (section 4.4), or null when the document has no such key.</summary>
    public ImmutableArray<ComponentGroup>? ComponentGroups { get; init; }

    /// <summary>The <c>functions</c> entries in document order (section 2.5).</summary>
    public ImmutableArray<LibrarySpecEntry> Functions { get; init; } = [];

    /// <summary>The <c>actions</c> entries in document order (section 2.5).</summary>
    public ImmutableArray<LibrarySpecEntry> Actions { get; init; } = [];

    /// <summary>Top-level keys this version does not define, kept in order so writing is lossless (prompt.md, section 2).</summary>
    public ImmutableArray<KeyValuePair<string, JsonElement>> AdditionalProperties { get; init; } = [];

    /// <summary>The <c>promptOptions</c> this file carries, or null if it has none (prompt.md, section 4.2).</summary>
    /// <exception cref="ArgumentException">A known option has the wrong JSON type.</exception>
    public PromptOptions? ReadPromptOptions()
    {
        foreach (var (name, value) in AdditionalProperties)
        {
            if (name == "promptOptions")
            {
                return PromptOptions.FromJson(value);
            }
        }

        return null;
    }

    /// <summary>Reads a LibrarySpec from UTF-8 JSON. Throws <see cref="ArgumentException"/> naming the bad path (prompt.md, section 2).</summary>
    public static LibrarySpec Parse(ReadOnlySpan<byte> utf8)
    {
        using var document = JsonDocument.Parse(utf8.ToArray());
        return FromJson(document.RootElement);
    }

    /// <summary>Reads a LibrarySpec from JSON text. Throws <see cref="ArgumentException"/> naming the bad path (prompt.md, section 2).</summary>
    public static LibrarySpec Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Parse(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Reads a LibrarySpec from a parsed JSON element. <c>components</c> is required and every entry
    /// needs a <c>signature</c>; other keys are optional. Throws <see cref="ArgumentException"/> (prompt.md, section 2).
    /// </summary>
    public static LibrarySpec FromJson(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("A LibrarySpec must be a JSON object.", nameof(element));
        }

        string? id = null, version = null, root = null;
        JsonElement? schema = null;
        ImmutableArray<LibrarySpecEntry>? components = null;
        ImmutableArray<ComponentGroup>? groups = null;
        ImmutableArray<LibrarySpecEntry> functions = [], actions = [];
        var extra = ImmutableArray.CreateBuilder<KeyValuePair<string, JsonElement>>();

        foreach (var property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "id":
                    id = ReadString(property.Value, "id");
                    break;
                case "version":
                    version = ReadString(property.Value, "version");
                    break;
                case "root":
                    root = ReadString(property.Value, "root");
                    break;
                case "schema":
                    schema = property.Value.Clone();
                    break;
                case "components":
                    components = ReadEntries(property.Value, "components");
                    break;
                case "componentGroups":
                    groups = ReadGroups(property.Value);
                    break;
                case "functions":
                    functions = ReadEntries(property.Value, "functions");
                    break;
                case "actions":
                    actions = ReadEntries(property.Value, "actions");
                    break;
                default:
                    extra.Add(new(property.Name, property.Value.Clone()));
                    break;
            }
        }

        return new LibrarySpec
        {
            Id = id,
            Version = version,
            Root = root,
            Schema = schema,
            Components = components ?? throw new ArgumentException("A LibrarySpec needs a \"components\" object.", nameof(element)),
            ComponentGroups = groups,
            Functions = functions,
            Actions = actions,
            AdditionalProperties = extra.ToImmutable(),
        };
    }

    /// <summary>Writes the document with every key in the order it was read or built (prompt.md, section 2).</summary>
    public void WriteTo(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStartObject();
        WriteOptional(writer, "id", Id);
        WriteOptional(writer, "version", Version);
        WriteOptional(writer, "root", Root);
        if (Schema is { } schema)
        {
            writer.WritePropertyName("schema");
            schema.WriteTo(writer);
        }

        WriteEntries(writer, "components", Components);
        if (ComponentGroups is { } groups)
        {
            writer.WriteStartArray("componentGroups");
            foreach (var group in groups)
            {
                writer.WriteStartObject();
                writer.WriteString("name", group.Name);
                writer.WriteStartArray("components");
                foreach (var name in group.Components)
                {
                    writer.WriteStringValue(name);
                }

                writer.WriteEndArray();
                if (group.Notes is { } notes)
                {
                    writer.WriteStartArray("notes");
                    foreach (var note in notes)
                    {
                        writer.WriteStringValue(note);
                    }

                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        if (!Functions.IsEmpty)
        {
            WriteEntries(writer, "functions", Functions);
        }

        if (!Actions.IsEmpty)
        {
            WriteEntries(writer, "actions", Actions);
        }

        foreach (var (key, value) in AdditionalProperties)
        {
            writer.WritePropertyName(key);
            value.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    /// <summary>The document as indented JSON text (prompt.md, section 2).</summary>
    public string ToJson()
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string ReadString(JsonElement value, string path) =>
        value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new ArgumentException($"LibrarySpec \"{path}\" must be a string.");

    private static ImmutableArray<LibrarySpecEntry> ReadEntries(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"LibrarySpec \"{path}\" must be an object.");
        }

        var entries = ImmutableArray.CreateBuilder<LibrarySpecEntry>();
        foreach (var entry in value.EnumerateObject())
        {
            var where = $"{path}.{entry.Name}";
            if (entry.Value.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException($"LibrarySpec \"{where}\" must be an object.");
            }

            string? signature = null, description = null;
            foreach (var field in entry.Value.EnumerateObject())
            {
                if (field.Name == "signature")
                {
                    signature = ReadString(field.Value, $"{where}.signature");
                }
                else if (field.Name == "description")
                {
                    description = ReadString(field.Value, $"{where}.description");
                }
            }

            entries.Add(new LibrarySpecEntry(
                entry.Name,
                signature ?? throw new ArgumentException($"LibrarySpec \"{where}\" needs a \"signature\" string."),
                description));
        }

        return entries.ToImmutable();
    }

    private static ImmutableArray<ComponentGroup> ReadGroups(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("LibrarySpec \"componentGroups\" must be an array.");
        }

        var groups = ImmutableArray.CreateBuilder<ComponentGroup>();
        var index = 0;
        foreach (var group in value.EnumerateArray())
        {
            var where = $"componentGroups[{index++}]";
            if (group.ValueKind != JsonValueKind.Object
                || !group.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
                || !group.TryGetProperty("components", out var names) || names.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException($"LibrarySpec \"{where}\" needs a \"name\" string and a \"components\" array.");
            }

            ImmutableArray<string>? notes = group.TryGetProperty("notes", out var notesValue)
                ? ReadStrings(notesValue, $"{where}.notes")
                : null;
            groups.Add(new ComponentGroup(name.GetString()!, ReadStrings(names, $"{where}.components"), notes));
        }

        return groups.ToImmutable();
    }

    private static ImmutableArray<string> ReadStrings(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException($"LibrarySpec \"{path}\" must be an array of strings.");
        }

        return [.. value.EnumerateArray().Select(item => ReadString(item, path))];
    }

    private static void WriteOptional(Utf8JsonWriter writer, string key, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(key, value);
        }
    }

    private static void WriteEntries(Utf8JsonWriter writer, string key, ImmutableArray<LibrarySpecEntry> entries)
    {
        writer.WriteStartObject(key);
        foreach (var entry in entries)
        {
            writer.WriteStartObject(entry.Name);
            writer.WriteString("signature", entry.Signature);
            if (entry.Description is not null)
            {
                writer.WriteString("description", entry.Description);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }
}
