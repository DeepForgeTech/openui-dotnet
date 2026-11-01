using System.Collections.Immutable;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Runtime;

/// <summary>One field's value and the kind of input that wrote it (language.md, sections 5.3 and 6.3).</summary>
/// <param name="Value">The value.</param>
/// <param name="ComponentType">The <c>componentType</c>, such as <c>input</c>.</param>
public readonly record struct FormField(OuiValue Value, string ComponentType);

/// <summary>Every form's fields, which a host may persist and restore (language.md, section 5.3).</summary>
/// <param name="Forms">The fields of each named form.</param>
/// <param name="Page">The page-level fields, the unnamed default scope.</param>
public sealed record FormStateSnapshot(
    ImmutableDictionary<string, ImmutableDictionary<string, FormField>> Forms,
    ImmutableDictionary<string, FormField> Page)
{
    /// <summary>No fields (language.md, section 5.3).</summary>
    public static FormStateSnapshot Empty { get; } = new(
        ImmutableDictionary.Create<string, ImmutableDictionary<string, FormField>>(StringComparer.Ordinal),
        ImmutableDictionary.Create<string, FormField>(StringComparer.Ordinal));

    internal FormStateSnapshot With(string? formName, string field, FormField value) => formName is null
        ? this with { Page = Page.SetItem(field, value) }
        : this with { Forms = Forms.SetItem(formName, Fields(formName).SetItem(field, value)) };

    internal ImmutableDictionary<string, FormField> Fields(string? formName) =>
        formName is null ? Page : Forms.GetValueOrDefault(formName) ?? Page.Clear();
}

/// <summary>One field's first failing rule (language.md, section 5.4).</summary>
/// <param name="Field">The field name.</param>
/// <param name="Message">What failed, in words a page can show.</param>
public sealed record FormFieldError(string Field, string Message);

/// <summary>The outcome of checking a form's fields against their rules (language.md, section 5.4).</summary>
/// <param name="Errors">One entry per failing field, in tree order.</param>
public sealed record FormValidation(ImmutableArray<FormFieldError> Errors)
{
    /// <summary>True when every field passed; a form submits only then (language.md, section 5.4).</summary>
    public bool IsValid => Errors.IsEmpty;
}

/// <summary>Reads and writes form fields (language.md, section 5.3). A null form name is the page scope.</summary>
public sealed class FormStateAccessor
{
    private readonly OpenUISession _session;

    internal FormStateAccessor(OpenUISession session) => _session = session;

    /// <summary>The field's value; null when it has none (language.md, section 5.3).</summary>
    public OuiValue Get(string? formName, string field) =>
        _session.FormSnapshot().Fields(formName).TryGetValue(field, out var value) ? value.Value : OuiValue.Null;

    /// <summary>Writes a field. Form values persist across parses (section 5.3).</summary>
    public void Set(string? formName, string field, OuiValue value, string componentType)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(componentType);
        _session.WriteField(formName, field, new FormField(value, componentType));
    }

    /// <summary>All fields as of now, for a host to persist (section 5.3).</summary>
    public FormStateSnapshot Snapshot() => _session.FormSnapshot();
}
