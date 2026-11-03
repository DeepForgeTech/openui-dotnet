using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Runtime;

/// <summary>Converts a typed tool's arguments, naming the argument and the type it takes when one has the wrong type (language.md, section 8.2).</summary>
internal static class TypedToolArgs
{
    /// <summary>
    /// Converts <paramref name="args"/>. A value of the wrong type for a declared argument throws an
    /// <see cref="OpenUIToolException"/> that the program can read; anything else is rethrown, so the
    /// session reports it with its generic message.
    /// </summary>
    public static T? Read<T>(string tool, OuiValue args, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            return args.As(typeInfo);
        }
        catch (JsonException exception) when (Describe(typeInfo, exception.Path) is var (argument, expected))
        {
            var takes = expected is null ? $"got the wrong type for {argument}" : $"takes {argument} as {expected}, and the program sent another type";
            throw new OpenUIToolException($"The tool \"{tool}\" {takes}.", OpenUIErrorCode.ToolError);
        }
    }

    // The name is rebuilt from the declared properties, never copied from the path, so it holds no text
    // the program wrote. A path that does not end on a declared property, such as a dictionary key or the
    // whole object, gives null.
    private static (string Argument, string? Expected)? Describe(JsonTypeInfo root, string? path)
    {
        if (path is null || !path.StartsWith('$') || path.Length == 1)
        {
            return null;
        }

        try
        {
            var current = root;
            var name = "";
            var rest = path.AsSpan(1);
            while (!rest.IsEmpty)
            {
                if (rest[0] == '[' && rest.IndexOf(']') is > 0 and var close && current.Kind == JsonTypeInfoKind.Enumerable && current.ElementType is { } element)
                {
                    rest = rest[(close + 1)..];
                    current = current.Options.GetTypeInfo(element);
                    name += "[]";
                }
                else if (rest[0] == '.' && current.Kind == JsonTypeInfoKind.Object)
                {
                    var end = rest[1..].IndexOfAny('.', '[');
                    var segment = (end < 0 ? rest[1..] : rest.Slice(1, end)).ToString();
                    rest = end < 0 ? [] : rest[(end + 1)..];
                    var property = current.Properties.FirstOrDefault(candidate => string.Equals(candidate.Name, segment, StringComparison.Ordinal));
                    if (property is null)
                    {
                        return null;
                    }

                    current = current.Options.GetTypeInfo(property.PropertyType);
                    name += name.Length == 0 ? property.Name : "." + property.Name;
                }
                else
                {
                    return null;
                }
            }

            return name.Length == 0 || name.StartsWith('[') ? null : (name, Expected(current));
        }
        catch (NotSupportedException)
        {
            // The resolver has no metadata for a nested type, so there is nothing to name.
            return null;
        }
    }

    // Null when the JSON form depends on a converter, as an enum's does (a number, or a name with
    // JsonStringEnumConverter), or for a type such as JsonElement that takes anything.
    private static string? Expected(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind == JsonTypeInfoKind.Enumerable)
        {
            return "an array";
        }

        if (typeInfo.Kind is JsonTypeInfoKind.Object or JsonTypeInfoKind.Dictionary)
        {
            return "an object";
        }

        var type = Nullable.GetUnderlyingType(typeInfo.Type) ?? typeInfo.Type;
        return Type.GetTypeCode(type) switch
        {
            _ when type.IsEnum => null,
            _ when type == typeof(Half) || type == typeof(Int128) || type == typeof(UInt128) => "a number",
            TypeCode.Boolean => "a boolean",
            TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32
                or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal => "a number",
            TypeCode.String or TypeCode.Char or TypeCode.DateTime => "a string",
            _ when type == typeof(Guid) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly)
                || type == typeof(TimeSpan) || type == typeof(Uri) => "a string",
            _ => null,
        };
    }
}
