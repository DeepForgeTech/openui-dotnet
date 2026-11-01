using System.Collections.Immutable;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Semantics;

/// <summary>One failed check: the code, the JSON Pointer path, and whether it was an enum check (those wait for stream end).</summary>
internal readonly record struct Issue(OpenUIErrorCode Code, string Path, bool IsEnum);

/// <summary>What happens to one argument after checking (language.md, section 8.2).</summary>
internal enum ArgumentVerdict
{
    /// <summary>Use <see cref="ArgumentCheck.Value"/>.</summary>
    Use,

    /// <summary>Leave the optional prop out.</summary>
    Omit,

    /// <summary>The required argument is unusable: a component is dropped, a function gives null, an action does nothing.</summary>
    Reject,
}

internal readonly record struct ArgumentCheck(ArgumentVerdict Verdict, OuiValue Value);

/// <summary>
/// The one implementation of prop and param checks, used on literal arguments when binding and on
/// runtime values when evaluating (language.md, sections 3.6, 8.2 and 8.4).
/// </summary>
internal static class PropValidator
{
    /// <summary>
    /// Checks one argument; <paramref name="value"/> is null when no value was given. Recovery order:
    /// the schema default, then leaving an optional prop out, then rejecting.
    /// </summary>
    public static ArgumentCheck Check(PropSpec prop, OuiValue? value, List<Issue> issues)
    {
        var path = "/" + prop.Name;
        if (value is not { } given || given.IsNull)
        {
            if (prop.IsEffectivelyRequired)
            {
                issues.Add(new Issue(value is null ? OpenUIErrorCode.MissingRequired : OpenUIErrorCode.NullRequired, path, false));
                return new ArgumentCheck(ArgumentVerdict.Reject, OuiValue.Null);
            }

            return Fallback(prop);
        }

        if (Validate(prop.Type, given, path, issues) is { } accepted)
        {
            return new ArgumentCheck(ArgumentVerdict.Use, accepted);
        }

        issues.Add(new Issue(OpenUIErrorCode.TypeMismatch, path, IsEnum(prop.Type)));
        return prop.Default is not null || !prop.Required ? Fallback(prop) : new ArgumentCheck(ArgumentVerdict.Reject, OuiValue.Null);
    }

    /// <summary>The steps <see cref="Validate"/> takes on this value: its top items, deeper only where the shape goes deeper.</summary>
    public static long Cost(TypeShape shape, OuiValue value)
    {
        switch (shape)
        {
            case ArrayShape array when value.TryGetArray(out var items):
                long cost = 1 + items.Count;
                if (array.Item is ArrayShape or UnionShape)
                {
                    foreach (var item in items)
                    {
                        cost += Cost(array.Item, item);
                    }
                }

                return cost;
            case UnionShape union:
                return union.Options.Sum(option => Cost(option, value));
            default:
                return 1;
        }
    }

    /// <summary>
    /// The value if it matches <paramref name="shape"/>, with invalid array items left out (each one
    /// reported); null when the value as a whole does not match.
    /// </summary>
    public static OuiValue? Validate(TypeShape shape, OuiValue value, string path, List<Issue> issues)
    {
        switch (shape)
        {
            case ScalarShape { Kind: TypeShapeKind.Any }:
            case ActionSlotShape:
                return value;
            case ScalarShape scalar:
                return value.Kind == scalar.Kind switch
                {
                    TypeShapeKind.String => OuiValueKind.String,
                    TypeShapeKind.Number => OuiValueKind.Number,
                    _ => OuiValueKind.Boolean,
                }
                    ? value
                    : (OuiValue?)null;
            case EnumShape e:
                return value.TryGetString(out var text) && e.Values.Contains(text) ? value : (OuiValue?)null;
            case ObjectShape:
                return value.Kind == OuiValueKind.Object ? value : (OuiValue?)null;
            case ComponentRefShape:
                return value.TryGetComponent(out var component) && !component.IsUnknown ? value : (OuiValue?)null;
            case ArrayShape array:
                return value.TryGetArray(out var items) ? ValidateItems(array.Item, items, path, issues) : (OuiValue?)null;
            case UnionShape union:
                foreach (var option in union.Options)
                {
                    var optionIssues = new List<Issue>();
                    if (Validate(option, value, path, optionIssues) is { } accepted && optionIssues.Count == 0)
                    {
                        return accepted;
                    }
                }

                return null;
            default:
                throw new InvalidOperationException($"Unhandled type shape {shape.Kind}.");
        }
    }

    // An explicit null item is kept and renders nothing (section 8.4).
    private static OuiValue ValidateItems(TypeShape itemShape, OuiArray items, string path, List<Issue> issues)
    {
        ImmutableArray<OuiValue>.Builder? kept = null;

        // Only an item shape that holds items of its own reports under the item's path, so the common
        // array of scalars or components does not build a path per item.
        var nested = itemShape is ArrayShape or UnionShape;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var accepted = item.IsNull ? item : Validate(itemShape, item, nested ? $"{path}/{i}" : path, issues);
            if (accepted is null)
            {
                issues.Add(new Issue(OpenUIErrorCode.TypeMismatch, $"{path}/{i}", IsEnum(itemShape)));
            }

            if (kept is null && (accepted is null || !ReferenceEquals(accepted.Value.Payload, item.Payload)))
            {
                kept = ImmutableArray.CreateBuilder<OuiValue>(items.Count);
                for (var j = 0; j < i; j++)
                {
                    kept.Add(items[j]);
                }
            }

            if (accepted is { } value)
            {
                kept?.Add(value);
            }
        }

        return kept is null ? items : OuiArray.Create(kept.ToImmutable());
    }

    private static ArgumentCheck Fallback(PropSpec prop) =>
        prop.Default is { } fallback ? new ArgumentCheck(ArgumentVerdict.Use, fallback) : new ArgumentCheck(ArgumentVerdict.Omit, OuiValue.Null);

    private static bool IsEnum(TypeShape shape) => shape.Kind == TypeShapeKind.Enum;
}
