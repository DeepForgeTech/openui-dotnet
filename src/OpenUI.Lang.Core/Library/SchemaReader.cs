using System.Collections.Immutable;
using System.Text.Json;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Library;

/// <summary>
/// Reads component, function and action declarations from a LibrarySpec (prompt.md, section 2.3).
/// The schema decides positional order and types; the signature line adds only <c>$binding</c>
/// (section 2.4), and stands in for the schema when a document has none.
/// </summary>
internal sealed class SchemaReader
{
    private const int MaxSchemaDepth = 32;

    // Far past any real library; a schema over it reads the rest as "any".
    private const int MaxSchemaNodes = 100_000;

    // Built-in action schema names a $ref may point at (prompt.md, section 2.3).
    private static readonly string[] BuiltinActionRefs = ["ActionExpression", "OpenUrl", "ToAssistant"];

    private readonly JsonElement? _schema;
    private readonly HashSet<string> _components;
    private readonly HashSet<string> _actions;
    private readonly Dictionary<string, TypeShape> _definitions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reading = new(StringComparer.Ordinal);
    private int _nodes;

    private SchemaReader(LibrarySpec spec)
    {
        _schema = spec.Schema is { ValueKind: JsonValueKind.Object } schema ? schema : null;
        _components = new HashSet<string>(spec.Components.Select(c => c.Name), StringComparer.Ordinal);
        if (Section("properties") is { } properties)
        {
            _components.UnionWith(properties.EnumerateObject().Select(p => p.Name));
        }

        _actions = new HashSet<string>(spec.Actions.Select(a => a.Name), StringComparer.Ordinal);
        _actions.UnionWith(BuiltinActionRefs);
        if (Section("actions") is { } actions)
        {
            _actions.UnionWith(actions.EnumerateObject().Select(a => a.Name));
        }
    }

    public static (ImmutableArray<ComponentSpec> Components, ImmutableArray<FunctionSpec> Functions, ImmutableArray<ActionSpec> Actions) Read(LibrarySpec spec)
    {
        var reader = new SchemaReader(spec);
        var components = spec.Components.Select(reader.ReadComponent).ToImmutableArray();
        var functions = spec.Functions.Select(reader.ReadFunction).ToImmutableArray();
        var actions = spec.Actions.Select(reader.ReadAction).ToImmutableArray();
        return (components, functions, actions);
    }

    private ComponentSpec ReadComponent(LibrarySpecEntry entry)
    {
        var definition = ComponentDefinition(entry.Name);
        var props = definition is { } schema ? ReadProps(schema, entry.Signature) : SignatureText.ReadProps(entry.Signature);
        return new ComponentSpec(entry.Name, entry.Description ?? "", props, default, entry.Signature);
    }

    private FunctionSpec ReadFunction(LibrarySpecEntry entry)
    {
        var definition = Section("functions") is { } functions && functions.TryGetProperty(entry.Name, out var found) ? found : (JsonElement?)null;
        var props = definition is { } d && d.TryGetProperty("params", out var parameters)
            ? ReadProps(parameters, entry.Signature)
            : SignatureText.ReadProps(entry.Signature);
        TypeShape? returns = definition is { } r && r.TryGetProperty("returns", out var returnSchema) ? ReadShape(returnSchema, 0) : null;
        return new FunctionSpec(entry.Name, entry.Description ?? "", props, returns, entry.Signature);
    }

    private ActionSpec ReadAction(LibrarySpecEntry entry)
    {
        var definition = Section("actions") is { } actions && actions.TryGetProperty(entry.Name, out var found) ? found : (JsonElement?)null;
        var props = definition is { } d && d.TryGetProperty("params", out var parameters)
            ? ReadProps(parameters, entry.Signature)
            : SignatureText.ReadProps(entry.Signature);
        return new ActionSpec(entry.Name, entry.Description ?? "", props, entry.Signature);
    }

    private JsonElement? Section(string name) =>
        _schema is { } schema && schema.TryGetProperty(name, out var section) && section.ValueKind == JsonValueKind.Object ? section : null;

    private JsonElement? ComponentDefinition(string name)
    {
        if (Section("properties") is { } properties && properties.TryGetProperty(name, out var entry)
            && RefName(entry) is { } target && Definition(target) is { } viaRef)
        {
            return viaRef;
        }

        return Definition(name);
    }

    private JsonElement? Definition(string name) =>
        Section("$defs") is { } defs && defs.TryGetProperty(name, out var definition) ? definition : null;

    private ImmutableArray<PropSpec> ReadProps(JsonElement objectSchema, string signature) => ReadFields(objectSchema, SignatureText.BindableProps(signature), 0);

    private ImmutableArray<PropSpec> ReadFields(JsonElement objectSchema, IReadOnlySet<string> bindable, int depth)
    {
        if (objectSchema.ValueKind != JsonValueKind.Object
            || !objectSchema.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var required = objectSchema.TryGetProperty("required", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Where(n => n.ValueKind == JsonValueKind.String).Select(n => n.GetString()!).ToHashSet(StringComparer.Ordinal)
            : [];

        var props = ImmutableArray.CreateBuilder<PropSpec>();
        foreach (var property in properties.EnumerateObject())
        {
            OuiValue? defaultValue = property.Value.ValueKind == JsonValueKind.Object && property.Value.TryGetProperty("default", out var d)
                ? OuiValue.FromJson(d)
                : (OuiValue?)null;
            props.Add(new PropSpec(
                property.Name,
                ReadShape(property.Value, depth + 1),
                required.Contains(property.Name),
                defaultValue,
                bindable.Contains(property.Name)));
        }

        return props.ToImmutable();
    }

    private TypeShape ReadShape(JsonElement schema, int depth)
    {
        if (schema.ValueKind != JsonValueKind.Object || depth > MaxSchemaDepth || ++_nodes > MaxSchemaNodes)
        {
            return TypeShape.Any;
        }

        if (RefName(schema) is { } target)
        {
            return RefShape(target, depth);
        }

        if (schema.TryGetProperty("anyOf", out var anyOf) && anyOf.ValueKind == JsonValueKind.Array)
        {
            return UnionOf([.. anyOf.EnumerateArray()], depth);
        }

        if (schema.TryGetProperty("enum", out var values) && values.ValueKind == JsonValueKind.Array)
        {
            return new EnumShape([.. values.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!)]);
        }

        if (schema.TryGetProperty("const", out var constant) && constant.ValueKind == JsonValueKind.String)
        {
            return new EnumShape([constant.GetString()!]);
        }

        if (!schema.TryGetProperty("type", out var type))
        {
            return TypeShape.Any;
        }

        if (type.ValueKind == JsonValueKind.Array)
        {
            var options = type.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString()!).ToList();
            return options.Contains("null") ? TypeShape.Any : new UnionShape([.. options.Select(t => Primitive(t, schema, depth))]);
        }

        return type.ValueKind == JsonValueKind.String ? Primitive(type.GetString()!, schema, depth) : TypeShape.Any;
    }

    private TypeShape Primitive(string type, JsonElement schema, int depth) => type switch
    {
        "string" => TypeShape.String,
        "number" or "integer" => TypeShape.Number,
        "boolean" => TypeShape.Boolean,
        "array" => new ArrayShape(schema.TryGetProperty("items", out var items) ? ReadShape(items, depth + 1) : TypeShape.Any),
        "object" => new ObjectShape(ReadFields(schema, new HashSet<string>(), depth + 1)),
        _ => TypeShape.Any,
    };

    private TypeShape RefShape(string target, int depth)
    {
        if (_components.Contains(target))
        {
            return new ComponentRefShape([target]);
        }

        if (_actions.Contains(target))
        {
            return new ActionSlotShape(target == "ActionExpression" ? [] : [target]);
        }

        return Definition(target) is { } shared ? DefinitionShape(target, shared, depth) : TypeShape.Any;
    }

    // Each definition is read once, and a reference back into one being read is "any". Otherwise a
    // definition with several references to itself expands exponentially up to the depth limit.
    private TypeShape DefinitionShape(string name, JsonElement definition, int depth)
    {
        if (_definitions.TryGetValue(name, out var known))
        {
            return known;
        }

        if (!_reading.Add(name))
        {
            return TypeShape.Any;
        }

        var shape = ReadShape(definition, depth + 1);
        _reading.Remove(name);
        _definitions[name] = shape;
        return shape;
    }

    private TypeShape UnionOf(IReadOnlyList<JsonElement> options, int depth)
    {
        var refs = options.Select(RefName).ToList();
        if (refs.Count > 0 && refs.All(r => r is not null && _components.Contains(r)))
        {
            return new ComponentRefShape([.. refs!]);
        }

        if (refs.Count > 0 && refs.All(r => r is not null && _actions.Contains(r)))
        {
            return refs.Contains("ActionExpression") ? new ActionSlotShape([]) : new ActionSlotShape([.. refs!]);
        }

        return new UnionShape([.. options.Select(option => ReadShape(option, depth + 1))]);
    }

    private static string? RefName(JsonElement schema)
    {
        const string prefix = "#/$defs/";
        return schema.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("$ref", out var reference)
            && reference.ValueKind == JsonValueKind.String
            && reference.GetString() is { } text
            && text.StartsWith(prefix, StringComparison.Ordinal)
                ? text[prefix.Length..]
                : null;
    }
}
