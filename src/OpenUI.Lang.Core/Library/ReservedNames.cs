using System.Collections.Frozen;

namespace OpenUI.Lang.Library;

/// <summary>The reserved names of language.md, section 1.3, shared by the parser and the library checks.</summary>
internal static class ReservedNames
{
    /// <summary>The built-in functions (section 3.4), without the <c>@</c>.</summary>
    public static readonly FrozenSet<string> BuiltinFunctions = FrozenSet.Create(
        StringComparer.Ordinal,
        "Count", "First", "Last", "Sum", "Avg", "Min", "Max", "Filter", "Sort", "Round", "Abs", "Floor", "Ceil", "Each");

    /// <summary>The built-in actions (section 6.3), without the <c>@</c>.</summary>
    public static readonly FrozenSet<string> BuiltinActions = FrozenSet.Create(
        StringComparer.Ordinal, "Set", "Reset", "Run", "ToAssistant", "OpenUrl");

    /// <summary>The reserved call forms (section 2.1).</summary>
    public static readonly FrozenSet<string> CallForms = FrozenSet.Create(StringComparer.Ordinal, "Query", "Mutation", "Action");

    /// <summary>True for any name a library may not use for a component, function or action.</summary>
    public static bool IsReserved(string name) =>
        BuiltinFunctions.Contains(name) || BuiltinActions.Contains(name) || CallForms.Contains(name);

    /// <summary>True for a valid uppercase name: <c>[A-Z][a-zA-Z0-9_]*</c> (section 1.3; prompt.md, section 3.1).</summary>
    public static bool IsUppercaseIdentifier(string name) =>
        name.Length > 0 && char.IsAsciiLetterUpper(name[0]) && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
