using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace OpenUI.Lang.Values;

// On the type, so serializing a value can't fall back to its members and silently lose what it holds.
[JsonConverter(typeof(OuiValueJsonConverter))]
public readonly partial struct OuiValue
{
    /// <summary>The deepest JSON nesting <see cref="FromJson(JsonElement)"/> and <see cref="ReadFrom"/> accept (language.md, section 3.1).</summary>
    public const int MaxJsonDepth = 64;

    /// <summary>
    /// Reads a JSON value: objects keep ECMAScript key order, numbers become doubles (language.md,
    /// section 3.1). Throws <see cref="JsonException"/> past <see cref="MaxJsonDepth"/> levels.
    /// </summary>
    public static OuiValue FromJson(JsonElement element) => FromJson(element, 0);

    /// <summary>Reads one JSON value from <paramref name="reader"/>, with the same rules as <see cref="FromJson(JsonElement)"/> (language.md, section 3.1).</summary>
    public static OuiValue ReadFrom(ref Utf8JsonReader reader)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return FromJson(document.RootElement);
    }

    /// <summary>
    /// Converts a host value through its JSON form, with no reflection (language.md, section 3.1).
    /// </summary>
    public static OuiValue From<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        var element = JsonSerializer.SerializeToElement(value, typeInfo);
        return FromJson(element);
    }

    /// <summary>Converts this value to a host type through its JSON form, with no reflection (language.md, section 3.1).</summary>
    public T? As<T>(JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteTo(writer);
        }

        return JsonSerializer.Deserialize(buffer.WrittenSpan, typeInfo);
    }

    /// <summary>
    /// Writes this value as JSON, the way <c>JSON.stringify</c> would for scalars: non-finite numbers
    /// become <c>null</c> and -0 becomes 0. A component is written as <c>{ "type", "props" }</c> (null props
    /// left out, a binding as <c>{ "$binding": "$name" }</c>), and an action as
    /// <c>{ "actions": [{ "action", "args" }] }</c>, the summary form of language.md Appendix B.
    /// </summary>
    public void WriteTo(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        switch (_payload)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case OuiArray array:
                writer.WriteStartArray();
                foreach (var item in array)
                {
                    item.WriteTo(writer);
                }

                writer.WriteEndArray();
                break;
            case OuiObject obj:
                writer.WriteStartObject();
                foreach (var (key, value) in obj)
                {
                    writer.WritePropertyName(key);
                    value.WriteTo(writer);
                }

                writer.WriteEndObject();
                break;
            case OuiComponent component:
                WriteComponent(writer, component);
                break;
            case OuiAction action:
                WriteAction(writer, action);
                break;
            default:
                if (TryGetNumber(out var number))
                {
                    WriteNumber(writer, number);
                }
                else
                {
                    writer.WriteBooleanValue(ReferenceEquals(_payload, TrueTag));
                }

                break;
        }
    }

    /// <summary>A number as <c>JSON.stringify</c> writes it: non-finite numbers are null, -0 is 0.</summary>
    internal static void WriteNumber(Utf8JsonWriter writer, double number)
    {
        if (!double.IsFinite(number))
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteNumberValue(number == 0 ? 0 : number);
        }
    }

    private static OuiValue FromJson(JsonElement element, int depth)
    {
        if (depth > MaxJsonDepth)
        {
            throw new JsonException($"JSON nests deeper than {MaxJsonDepth} levels.");
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString()!;
            case JsonValueKind.Number:
                return element.GetDouble();
            case JsonValueKind.True:
                return True;
            case JsonValueKind.False:
                return False;
            case JsonValueKind.Array:
                var items = new OuiValue[element.GetArrayLength()];
                var i = 0;
                foreach (var item in element.EnumerateArray())
                {
                    items[i++] = FromJson(item, depth + 1);
                }

                return OuiArray.Create(System.Collections.Immutable.ImmutableArray.Create(items));
            case JsonValueKind.Object:
                var builder = new OuiObject.Builder();
                foreach (var property in element.EnumerateObject())
                {
                    builder.Set(property.Name, FromJson(property.Value, depth + 1));
                }

                return builder.ToObject();
            default:
                return Null;
        }
    }

    private static void WriteComponent(Utf8JsonWriter writer, OuiComponent component)
    {
        writer.WriteStartObject();
        writer.WriteString("type", component.Type);
        writer.WriteStartObject("props");
        foreach (var prop in component.Props)
        {
            if (prop.Binding is { } binding)
            {
                writer.WriteStartObject(prop.Name);
                writer.WriteString("$binding", binding.Name);
                writer.WriteEndObject();
            }
            else if (!prop.Value.IsNull)
            {
                writer.WritePropertyName(prop.Name);
                prop.Value.WriteTo(writer);
            }
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteAction(Utf8JsonWriter writer, OuiAction action)
    {
        writer.WriteStartObject();
        writer.WriteStartArray("actions");
        foreach (var step in action.Steps)
        {
            writer.WriteStartObject();
            switch (step)
            {
                case SetStep set:
                    writer.WriteString("action", "Set");
                    writer.WriteStartArray("args");
                    writer.WriteStringValue(set.Target.Name);
                    set.Value.WriteTo(writer);
                    writer.WriteEndArray();
                    break;
                case ResetStep reset:
                    writer.WriteString("action", "Reset");
                    writer.WriteStartArray("args");
                    foreach (var target in reset.Targets)
                    {
                        writer.WriteStringValue(target.Name);
                    }

                    writer.WriteEndArray();
                    break;
                case RunStep run:
                    writer.WriteString("action", "Run");
                    writer.WriteStartArray("args");
                    writer.WriteStringValue(run.StatementId);
                    writer.WriteEndArray();
                    break;
                case ToAssistantStep say:
                    writer.WriteString("action", "ToAssistant");
                    writer.WriteStartArray("args");
                    say.Message.WriteTo(writer);
                    say.Context?.WriteTo(writer);
                    writer.WriteEndArray();
                    break;
                case OpenUrlStep open:
                    writer.WriteString("action", "OpenUrl");
                    writer.WriteStartArray("args");
                    open.Url.WriteTo(writer);
                    writer.WriteEndArray();
                    break;
                case CustomActionStep custom:
                    writer.WriteString("action", custom.Name);
                    writer.WriteStartArray("args");
                    foreach (var (_, value) in custom.Parameters)
                    {
                        value.WriteTo(writer);
                    }

                    writer.WriteEndArray();
                    break;
                default:
                    throw new InvalidOperationException($"Unhandled action step {step.Kind}.");
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}

/// <summary>Lets an <see cref="OuiValue"/> sit inside host DTOs serialized with System.Text.Json (language.md, section 3.1).</summary>
public sealed class OuiValueJsonConverter : JsonConverter<OuiValue>
{
    /// <summary>The converter reads null tokens itself, so a JSON null becomes <see cref="OuiValue.Null"/> (language.md, section 3.1).</summary>
    public override bool HandleNull => true;

    /// <summary>Reads one JSON value as an <see cref="OuiValue"/> (language.md, section 3.1).</summary>
    public override OuiValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        OuiValue.ReadFrom(ref reader);

    /// <summary>Writes an <see cref="OuiValue"/> with <see cref="OuiValue.WriteTo"/> (language.md, section 3.1).</summary>
    public override void Write(Utf8JsonWriter writer, OuiValue value, JsonSerializerOptions options) => value.WriteTo(writer);
}
