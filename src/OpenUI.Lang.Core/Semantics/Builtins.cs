using System.Collections.Immutable;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Semantics;

/// <summary>The built-in functions of language.md, section 3.4, except <c>@Each</c>.</summary>
internal static class Builtins
{
    // Telling a long unknown operator from the known ones per item would cost its length each time.
    private const int LongestOperator = 8;

    /// <summary>Calls <paramref name="name"/>; <paramref name="budget"/> pays for each comparison or element visited.</summary>
    public static OuiValue Call(string name, ReadOnlySpan<OuiValue> args, IOuiCollator collator, EvaluationBudget budget)
    {
        var first = args.Length > 0 ? args[0] : OuiValue.Null;
        first.TryGetArray(out var array);
        return name switch
        {
            "Count" => array?.Count ?? 0,
            "First" => array is { Count: > 0 } ? array[0] : OuiValue.Null,
            "Last" => array is { Count: > 0 } ? array[^1] : OuiValue.Null,
            "Sum" => array is null ? 0 : Sum(array, budget),
            "Avg" => array is { Count: > 0 } ? Sum(array, budget) / array.Count : 0,
            "Min" => array is { Count: > 0 } ? array.Items.Min(item => Item(item, budget)) : 0,
            "Max" => array is { Count: > 0 } ? array.Items.Max(item => Item(item, budget)) : 0,
            "Filter" => array is null ? OuiArray.Create(ImmutableArray<OuiValue>.Empty) : Filter(array, Arg(args, 1), Arg(args, 2), Arg(args, 3), budget),
            "Sort" => array is null ? first : Sort(array, Arg(args, 1), Arg(args, 2), collator, budget),
            "Round" => Round(budget.ToNumber(first), budget.ToNumber(Arg(args, 1))),
            "Abs" => Math.Abs(budget.ToNumber(first)),
            "Floor" => Math.Floor(budget.ToNumber(first)),
            "Ceil" => Math.Ceiling(budget.ToNumber(first)),
            _ => throw new InvalidOperationException($"Unknown built-in @{name}."),
        };
    }

    private static FieldPath PathParts(OuiValue field, EvaluationBudget budget)
    {
        if (!field.TryGetString(out var path) || path.Length == 0)
        {
            return new FieldPath([], 0);
        }

        budget.Walk(field);
        var dots = path.AsSpan().Count('.');
        budget.Spend(dots);
        return new FieldPath(path.Split('.'), dots + 1 + budget.ReadCost(path.Length));
    }

    // Reads a field by dot path ("customer.name"); an empty path is the value itself (section 3.4).
    private static OuiValue Field(OuiValue value, FieldPath path)
    {
        foreach (var part in path.Parts)
        {
            if (!value.TryGetObject(out var obj))
            {
                return OuiValue.Null;
            }

            value = obj[part];
        }

        return value;
    }

    private static OuiValue Arg(ReadOnlySpan<OuiValue> args, int index) => index < args.Length ? args[index] : OuiValue.Null;

    private static double Item(OuiValue item, EvaluationBudget budget)
    {
        budget.Spend();
        return budget.ToNumber(item);
    }

    private static double Sum(OuiArray array, EvaluationBudget budget)
    {
        var sum = 0.0;
        foreach (var item in array)
        {
            sum += Item(item, budget);
        }

        return sum;
    }

    private static double Round(double value, double decimals)
    {
        var factor = Math.Pow(10, decimals);
        return Ecma.MathRound(value * factor) / factor;
    }

    private static OuiArray Filter(OuiArray array, OuiValue field, OuiValue op, OuiValue operand, EvaluationBudget budget)
    {
        var path = PathParts(field, budget);
        var opText = op.IsNull ? "==" : op.TryGetString(out var o) && o.Length <= LongestOperator ? o : "";
        var kept = ImmutableArray.CreateBuilder<OuiValue>();
        foreach (var item in array)
        {
            budget.Spend(1 + path.ReadCost);
            if (Matches(Field(item, path), opText, operand, budget))
            {
                kept.Add(item);
            }
        }

        return OuiArray.Create(kept.ToImmutable());
    }

    private static bool Matches(OuiValue value, string op, OuiValue operand, EvaluationBudget budget) => op switch
    {
        "==" => budget.LooselyEquals(value, operand),
        "!=" => !budget.LooselyEquals(value, operand),
        ">" => budget.ToNumber(value) > budget.ToNumber(operand),
        "<" => budget.ToNumber(value) < budget.ToNumber(operand),
        ">=" => budget.ToNumber(value) >= budget.ToNumber(operand),
        "<=" => budget.ToNumber(value) <= budget.ToNumber(operand),
        "contains" => budget.Contains(budget.Stringify(value), budget.Stringify(operand)),
        _ => false,
    };

    // Stable. Numbers and numeric strings compare as numbers; anything else by its string form
    // (null is "") through the collator (section 3.4).
    private static OuiArray Sort(OuiArray array, OuiValue field, OuiValue direction, IOuiCollator collator, EvaluationBudget budget)
    {
        var path = PathParts(field, budget);
        var descending = direction.TryGetString(out var d) && d == "desc";
        var comparer = Comparer<OuiValue>.Create((a, b) =>
        {
            budget.Spend(1 + (2 * path.ReadCost));
            return Compare(Field(a, path), Field(b, path), collator, budget);
        });
        var sorted = descending ? array.Items.OrderByDescending(x => x, comparer) : array.Items.OrderBy(x => x, comparer);
        return OuiArray.Create(sorted.ToImmutableArray());
    }

    private static int Compare(OuiValue a, OuiValue b, IOuiCollator collator, EvaluationBudget budget)
    {
        if (Numeric(a, budget) is { } x && Numeric(b, budget) is { } y)
        {
            return x.CompareTo(y);
        }

        var (left, right) = (a.IsNull ? "" : budget.Stringify(a), b.IsNull ? "" : budget.Stringify(b));
        budget.Collate(left, right);
        return collator.Compare(left, right);
    }

    private static double? Numeric(OuiValue value, EvaluationBudget budget)
    {
        if (value.TryGetNumber(out var number))
        {
            return number;
        }

        if (!value.TryGetString(out var text))
        {
            return null;
        }

        budget.Walk(value);
        var parsed = Ecma.StringToNumber(text);
        return double.IsNaN(parsed) ? null : parsed;
    }

    // A dot path and what reading it from one item costs: each part hashed, each of its characters read.
    private readonly record struct FieldPath(string[] Parts, long ReadCost);
}
