using System.Collections.Immutable;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Semantics;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Runtime;

/// <summary>Form validation (language.md, section 5.4).</summary>
public sealed partial class OpenUISession
{
    /// <summary>
    /// Checks the fields of <paramref name="formName"/>, null for the page scope, against their rules
    /// (language.md, section 5.4). The rules are the evaluated value of the prop a field's
    /// <see cref="Library.ComponentTraits.FieldRulesProp"/> names, since that is where the program writes them. The value
    /// is the state of a bound prop, or else the form's field. A renderer calls this to show errors.
    /// </summary>
    public FormValidation ValidateForm(string? formName) => CheckForm(formName, new EvaluationBudget(_limits));

    // Outside the lock: the tree is immutable and a pattern can be slow to build.
    private FormValidation CheckForm(string? formName, EvaluationBudget budget)
    {
        var errors = ImmutableArray.CreateBuilder<FormFieldError>();
        var fields = FormSnapshot().Fields(formName);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var patterns = new EcmaRegexPass();
        string? limitMessage = null;
        void Visit(OuiValue value, string? enclosing)
        {
            budget.Spend();
            if (value.TryGetArray(out var items))
            {
                foreach (var item in items)
                {
                    Visit(item, enclosing);
                }
            }
            else if (value.TryGetObject(out var entries))
            {
                foreach (var (_, entry) in entries)
                {
                    Visit(entry, enclosing);
                }
            }
            else if (value.TryGetComponent(out var component))
            {
                var form = component.DeclaredFormName ?? enclosing;
                if (form == formName && FieldError(component, fields, seen, patterns, budget, ref limitMessage) is { } error)
                {
                    errors.Add(error);
                }

                foreach (var prop in component.Props)
                {
                    Visit(prop.Value, form);
                }
            }
        }

        try
        {
            if (Current.Root is { } root)
            {
                Visit(root, null);
            }
        }
        catch (LimitException limit)
        {
            // A form too large to check has not passed.
            errors.Add(new FormFieldError("", limit.Message));
            limitMessage = limit.Message;
        }

        if (limitMessage is { } message)
        {
            CommitIfAlive(() =>
            {
                _otherErrors.Add(new OpenUIError { Source = OpenUIErrorSource.Runtime, Code = OpenUIErrorCode.LimitExceeded, Message = message });
                return true;
            });
        }

        return new FormValidation(errors.ToImmutable());
    }

    private FormFieldError? FieldError(OuiComponent component, ImmutableDictionary<string, FormField> fields, HashSet<string> seen, EcmaRegexPass patterns, EvaluationBudget budget, ref string? limitMessage)
    {
        var traits = component.Spec?.Traits ?? default;
        if (traits.FieldRulesProp is not { } rulesProp
            || traits.FieldNameProp is not { } nameProp
            || !component[rulesProp].Value.TryGetObject(out var rules)
            || !component[nameProp].Value.TryGetString(out var name)
            || !seen.Add(name))
        {
            return null;
        }

        budget.Walk(rules);
        var bound = component.Props.Where(prop => prop.Binding is not null).Select(prop => (OuiValue?)prop.Value).FirstOrDefault();
        var value = bound ?? (fields.TryGetValue(name, out var field) ? field.Value : OuiValue.Null);
        budget.Walk(value);
        if (rules.TryGetValue("pattern", out var rule) && rule.TryGetString(out var pattern) && value.TryGetString(out var text))
        {
            if (text.Length > EcmaRegex.MaxMatchedChars)
            {
                limitMessage = $"A value longer than {EcmaRegex.MaxMatchedChars} characters fails its pattern rule without being matched.";
            }
            else
            {
                budget.Spend(EcmaRegex.MatchSteps(pattern, text.Length));
            }
        }

        return ValidationRules.Check(rules, value, patterns) is { } message ? new FormFieldError(name, message) : null;
    }
}
