using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests;

/// <summary>Turns the plain values <see cref="ReferenceData.Value"/> builds into <see cref="OuiValue"/>s.</summary>
internal static class ReferenceValues
{
    /// <summary>Each non-empty array and object becomes a new instance, as each JS literal is a new object; empty ones are the shared <see cref="OuiArray.Empty"/> and <see cref="OuiObject.Empty"/>.</summary>
    public static OuiValue ToOui(object? value) => value switch
    {
        null => OuiValue.Null,
        string text => text,
        double number => OuiValue.Number(number),
        bool flag => flag,
        List<object?> items => OuiArray.Create(items.Select(ToOui).ToArray()),
        OrderedDictionary<string, object?> fields => OuiObject.Create(fields.Select(field => KeyValuePair.Create(field.Key, ToOui(field.Value)))),
        _ => throw new InvalidDataException($"Unexpected reference value {value.GetType().Name}."),
    };

    /// <summary>A descriptor straight to an <see cref="OuiValue"/>.</summary>
    public static OuiValue Read(System.Text.Json.Nodes.JsonNode descriptor) => ToOui(ReferenceData.Value(descriptor));
}
