using System.Buffers;
using System.Collections.Immutable;
using System.Text.Json;

namespace OpenUI.Lang.Library;

/// <summary>Writes a library's validation schema (prompt.md, sections 2.3 and 2.7).</summary>
internal static class SchemaWriter
{
    public static JsonElement Write(OpenUILibrary library)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteSchema(writer, library);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static void WriteSchema(Utf8JsonWriter writer, OpenUILibrary library)
    {
        var allComponents = library.Components.Select(c => c.Name).ToImmutableArray();
        var actionRefs = new SortedSet<string>(StringComparer.Ordinal);
        var usesActionExpression = false;
        foreach (var prop in library.Components.SelectMany(c => c.Props))
        {
            CollectActions(prop.Type, actionRefs, ref usesActionExpression);
        }

        writer.WriteStartObject();
        writer.WriteStartObject("properties");
        foreach (var component in library.Components)
        {
            writer.WriteStartObject(component.Name);
            writer.WriteString("$ref", "#/$defs/" + component.Name);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();

        writer.WriteStartObject("$defs");
        foreach (var component in library.Components)
        {
            writer.WritePropertyName(component.Name);
            WriteObject(writer, component.Props, component.Description, allComponents);
        }

        if (usesActionExpression)
        {
            writer.WritePropertyName("ActionExpression");
            WriteActionExpression(writer);
        }

        foreach (var name in actionRefs)
        {
            writer.WritePropertyName(name);
            var custom = library.Actions.FirstOrDefault(a => a.Name == name);
            WriteActionDefinition(writer, name, custom, allComponents);
        }

        writer.WriteEndObject();

        if (!library.Functions.IsEmpty)
        {
            writer.WriteStartObject("functions");
            foreach (var function in library.Functions)
            {
                writer.WriteStartObject(function.Name);
                writer.WriteString("description", function.Description);
                writer.WritePropertyName("params");
                WriteObject(writer, function.Params, null, allComponents);
                if (function.Returns is not null)
                {
                    writer.WritePropertyName("returns");
                    WriteShape(writer, function.Returns, allComponents);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        if (!library.Actions.IsEmpty)
        {
            writer.WriteStartObject("actions");
            foreach (var action in library.Actions)
            {
                writer.WriteStartObject(action.Name);
                writer.WriteString("description", action.Description);
                writer.WritePropertyName("params");
                WriteObject(writer, action.Params, null, allComponents);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteObject(Utf8JsonWriter writer, ImmutableArray<PropSpec> props, string? description, ImmutableArray<string> allComponents)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "object");
        if (description is not null)
        {
            writer.WriteString("description", description);
        }

        writer.WriteStartObject("properties");
        foreach (var prop in props)
        {
            writer.WritePropertyName(prop.Name);
            WriteShape(writer, prop.Type, allComponents, prop.Default);
        }

        writer.WriteEndObject();
        writer.WriteStartArray("required");
        foreach (var prop in props.Where(p => p.Required))
        {
            writer.WriteStringValue(prop.Name);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteShape(Utf8JsonWriter writer, TypeShape shape, ImmutableArray<string> allComponents, Values.OuiValue? defaultValue = null)
    {
        writer.WriteStartObject();
        switch (shape)
        {
            case EnumShape e:
                writer.WriteString("type", "string");
                writer.WriteStartArray("enum");
                foreach (var value in e.Values)
                {
                    writer.WriteStringValue(value);
                }

                writer.WriteEndArray();
                break;
            case ArrayShape a:
                writer.WriteString("type", "array");
                if (a.Item.Kind != TypeShapeKind.Any)
                {
                    writer.WritePropertyName("items");
                    WriteShape(writer, a.Item, allComponents);
                }

                break;
            case ObjectShape o:
                writer.WriteString("type", "object");
                if (!o.Fields.IsEmpty)
                {
                    writer.WriteStartObject("properties");
                    foreach (var field in o.Fields)
                    {
                        writer.WritePropertyName(field.Name);
                        WriteShape(writer, field.Type, allComponents, field.Default);
                    }

                    writer.WriteEndObject();
                }

                break;
            case ComponentRefShape refs:
                WriteRefs(writer, refs.Components.IsEmpty ? allComponents : refs.Components);
                break;
            case ActionSlotShape { Actions.IsEmpty: true }:
                writer.WriteString("$ref", "#/$defs/ActionExpression");
                break;
            case ActionSlotShape slot:
                WriteRefs(writer, slot.Actions);
                break;
            case UnionShape union:
                writer.WriteStartArray("anyOf");
                foreach (var option in union.Options)
                {
                    WriteShape(writer, option, allComponents);
                }

                writer.WriteEndArray();
                break;
            default:
                var type = shape.Kind switch
                {
                    TypeShapeKind.String => "string",
                    TypeShapeKind.Number => "number",
                    TypeShapeKind.Boolean => "boolean",
                    _ => null,
                };
                if (type is not null)
                {
                    writer.WriteString("type", type);
                }

                break;
        }

        if (defaultValue is { } d)
        {
            writer.WritePropertyName("default");
            d.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    private static void WriteRefs(Utf8JsonWriter writer, ImmutableArray<string> names)
    {
        if (names.Length == 1)
        {
            writer.WriteString("$ref", "#/$defs/" + names[0]);
            return;
        }

        writer.WriteStartArray("anyOf");
        foreach (var name in names)
        {
            writer.WriteStartObject();
            writer.WriteString("$ref", "#/$defs/" + name);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void CollectActions(TypeShape shape, SortedSet<string> names, ref bool usesActionExpression)
    {
        switch (shape)
        {
            case ActionSlotShape { Actions.IsEmpty: true }:
                usesActionExpression = true;
                break;
            case ActionSlotShape slot:
                names.UnionWith(slot.Actions);
                break;
            case ArrayShape array:
                CollectActions(array.Item, names, ref usesActionExpression);
                break;
            case UnionShape union:
                foreach (var option in union.Options)
                {
                    CollectActions(option, names, ref usesActionExpression);
                }

                break;
        }
    }

    // The legacy action objects ActionExpression describes (prompt.md, section 2).
    private static void WriteActionExpression(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteStartArray("anyOf");
        WriteTypedObject(writer, "open_url", ("url", "string"), required: ["type", "url"]);
        WriteTypedObject(writer, "continue_conversation", ("context", "string"), required: ["type"]);
        writer.WriteStartObject();
        writer.WriteString("type", "object");
        writer.WriteStartObject("properties");
        writer.WriteStartObject("type");
        writer.WriteString("type", "string");
        writer.WriteEndObject();
        writer.WriteStartObject("params");
        writer.WriteString("type", "object");
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteStartArray("required");
        writer.WriteStringValue("type");
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteActionDefinition(Utf8JsonWriter writer, string name, ActionSpec? custom, ImmutableArray<string> allComponents)
    {
        if (custom is null)
        {
            var field = name == "OpenUrl" ? ("url", "string") : ("context", "string");
            WriteTypedObject(writer, name == "OpenUrl" ? "open_url" : "continue_conversation", field, name == "OpenUrl" ? ["type", "url"] : ["type"]);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("type", "object");
        writer.WriteStartObject("properties");
        writer.WriteStartObject("type");
        writer.WriteString("type", "string");
        writer.WriteString("const", name);
        writer.WriteEndObject();
        writer.WritePropertyName("params");
        WriteObject(writer, custom.Params, null, allComponents);
        writer.WriteEndObject();
        writer.WriteStartArray("required");
        writer.WriteStringValue("type");
        writer.WriteStringValue("params");
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteTypedObject(Utf8JsonWriter writer, string constant, (string Name, string Type) field, string[] required)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "object");
        writer.WriteStartObject("properties");
        writer.WriteStartObject("type");
        writer.WriteString("type", "string");
        writer.WriteString("const", constant);
        writer.WriteEndObject();
        writer.WriteStartObject(field.Name);
        writer.WriteString("type", field.Type);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteStartArray("required");
        foreach (var name in required)
        {
            writer.WriteStringValue(name);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
