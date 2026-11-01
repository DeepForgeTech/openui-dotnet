using System.Text.Json;
using System.Text.Json.Nodes;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Conformance;

/// <summary>The seam between the fixtures and the library under test.</summary>
internal static partial class ConformanceAdapter
{
    /// <summary>Checks one value against a validation rules object (language.md, section 5.4).</summary>
    public static bool RunValidation(JsonObject rules, JsonNode? value)
    {
        var ruleSet = ToValue(rules).TryGetObject(out var obj) ? obj : throw new InvalidDataException("Rules must be an object.");
        return ValidationRules.Check(ruleSet, ToValue(value)) is null;
    }

    private static OuiValue ToValue(JsonNode? node) =>
        node is null ? OuiValue.Null : OuiValue.FromJson(JsonSerializer.SerializeToElement(node));
}
