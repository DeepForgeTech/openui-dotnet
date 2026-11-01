using System.Collections.Immutable;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Semantics;
using OpenUI.Lang.Values;

namespace OpenUI.Lang;

/// <summary>What evaluation reads besides the document (language.md, sections 5 and 6).</summary>
/// <param name="State">State values keyed by name with its <c>$</c>; missing names take their declared default (section 5.1).</param>
/// <param name="QueryData">
/// Results keyed by statement name: a query's data, or a mutation's <c>{status, data, error}</c>
/// (sections 6.1 and 6.2). A query without an entry evaluates to its defaults; a mutation to idle.
/// </param>
public sealed record EvaluationInputs(
    IReadOnlyDictionary<string, OuiValue>? State = null,
    IReadOnlyDictionary<string, OuiValue>? QueryData = null);

/// <summary>The evaluated tree of one document and inputs (language.md, sections 3 and 8.4).</summary>
public sealed class RenderResult
{
    internal RenderResult(OuiComponent? root, OpenUIErrorList errors, OpenUIErrorList diagnostics, ImmutableDictionary<string, OuiValue> stateDefaults, long fuelSpent)
    {
        FuelSpent = fuelSpent;
        Root = root;
        Errors = errors;
        Diagnostics = diagnostics;
        StateDefaults = stateDefaults;
    }

    /// <summary>The steps this evaluation took.</summary>
    internal long FuelSpent { get; }

    /// <summary>The entry's component, or null when nothing renders (section 2.2).</summary>
    public OuiComponent? Root { get; }

    /// <summary>The document's errors and the ones found while evaluating, each reported once (section 8).</summary>
    public OpenUIErrorList Errors { get; }

    /// <summary>Every diagnostic whatever its timing, extension codes such as <c>x-collation-degraded</c> included (section 8.1).</summary>
    public OpenUIErrorList Diagnostics { get; }

    /// <summary>
    /// Every state variable's default, keyed by name with its <c>$</c>; names used without a declaration default to null (section 5.1).
    /// </summary>
    public ImmutableDictionary<string, OuiValue> StateDefaults { get; }
}

/// <summary>Every state variable's default for one document and what evaluating them reported; they depend on nothing else (language.md, section 5.1).</summary>
internal sealed record StateDefaultValues(ImmutableDictionary<string, OuiValue> Values, IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>Evaluates a document: a pure function of the document and its inputs (language.md, sections 3, 5 and 6).</summary>
public static class OpenUIEvaluator
{
    /// <summary>
    /// Evaluates <paramref name="document"/>'s entry against <paramref name="inputs"/> (language.md,
    /// section 3). Each statement is evaluated at most once. Evaluation never throws for any document:
    /// a hit limit drops the statement with <c>x-limit-exceeded</c> (section 8.1).
    /// </summary>
    /// <param name="document">The document to evaluate.</param>
    /// <param name="inputs">State and query data; null for defaults only.</param>
    /// <param name="limits">Budgets; null for <see cref="OpenUILimits.Default"/>.</param>
    public static RenderResult Evaluate(OpenUIDocument document, EvaluationInputs? inputs = null, OpenUILimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Evaluate(document, inputs, limits ?? OpenUILimits.Default, OuiCollator.Default);
    }

    internal static RenderResult Evaluate(OpenUIDocument document, EvaluationInputs? inputs, OpenUILimits limits, IOuiCollator collator, StateDefaultValues? precomputed = null)
    {
        var bag = new DiagnosticBag(limits.MaxErrors);
        bag.AddRange(document.AllDiagnostics);

        var stateDefaults = precomputed ?? StateDefaults(document, limits, collator);
        bag.AddRange(stateDefaults.Diagnostics);
        var defaults = stateDefaults.Values;
        var state = inputs?.State;
        var evaluator = new Evaluator(
            document,
            key => state is not null && state.TryGetValue(key.Name, out var value) ? value : defaults.GetValueOrDefault(key.Name),
            inputs?.QueryData,
            limits,
            collator,
            bag);

        OuiComponent? root = null;
        if (document.Entry is { } entry
            && evaluator.EvaluateStatement(entry.Name) is { IsAbsent: false } result
            && result.Value.TryGetComponent(out var component)
            && !component.IsUnknown)
        {
            root = component;
        }
        else if (document.Entry is not null)
        {
            // The entry exists but yields no component, such as root = "hello" (section 2.2).
            bag.Add(
                new OpenUIError
                {
                    Source = OpenUIErrorSource.Runtime,
                    Code = OpenUIErrorCode.NoRoot,
                    Message = $"The entry statement '{document.Entry.Name}' does not evaluate to a component.",
                    StatementId = document.Entry.Name,
                },
                DiagnosticTiming.StreamEnd);
        }

        return new RenderResult(
            root,
            DiagnosticBag.Visible(bag.Items, document.IsFinal),
            OpenUIErrorList.Create(bag.Items.Select(d => d.Error)),
            defaults,
            evaluator.FuelSpent);
    }

    // Declared state in source order, each default evaluated with the defaults before it in view;
    // then every $name used anywhere without a declaration, as null (section 5.1).
    internal static StateDefaultValues StateDefaults(OpenUIDocument document, OpenUILimits limits, IOuiCollator collator)
    {
        var bag = new DiagnosticBag(limits.MaxErrors);
        var defaults = ImmutableDictionary.CreateBuilder<string, OuiValue>(StringComparer.Ordinal);
        var evaluator = new Evaluator(document, key => defaults.GetValueOrDefault(key.Name), null, limits, collator, bag);
        foreach (var statement in document.Statements)
        {
            if (statement.Kind == StatementKind.State)
            {
                defaults[statement.Name] = evaluator.EvaluateIn(statement.Name, statement.Bound.Value);
            }
        }

        foreach (var statement in document.Statements)
        {
            foreach (var name in statement.Bound.Facts.StateNames)
            {
                defaults.TryAdd(name, OuiValue.Null);
            }
        }

        return new StateDefaultValues(defaults.ToImmutable(), bag.Items);
    }
}
