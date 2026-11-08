using Microsoft.AspNetCore.Components;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor;

/// <summary>
/// What a rendered component can do with the program it belongs to. <see cref="OpenUIView"/> cascades
/// one to every component it draws; <see cref="OpenUIComponent"/> wraps it in helpers.
/// </summary>
public sealed class OpenUIContext
{
    internal const string FormCascade = "OpenUI.Lang.Blazor.FormName";

    private readonly Action _refresh;
    private readonly Dictionary<string, FormValidation> _failedForms = new(StringComparer.Ordinal);
    private int _redraws;
    private FormStateSnapshot? _forms;

    // The views that have drawn each form field. Weak, because a view says nothing when it leaves: one that has left but not
    // been collected yet only costs an extra draw.
    private readonly Dictionary<(string? Form, string Field), List<WeakReference<OpenUIComponent>>> _fieldViews = [];

    internal OpenUIContext(OpenUISession session, OpenUIView view, Action refresh)
    {
        Session = session;
        View = view;
        _refresh = refresh;
    }

    /// <summary>The runtime: state, forms, queries and the evaluated view.</summary>
    public OpenUISession Session { get; }

    internal OpenUIView View { get; }

    /// <summary>
    /// How many times every view has had to draw again: for a <see cref="Refresh"/>, or for a form field written other than
    /// through <see cref="Write"/>, such as by the host. A view whose props did not change draws only when this did.
    /// </summary>
    internal int Redraws
    {
        get
        {
            NoteFormsWrittenElsewhere();
            return Volatile.Read(ref _redraws);
        }
    }

    /// <summary>What an input shows: the bound variable, or else its form field, or else the prop's value.</summary>
    /// <param name="node">The input.</param>
    /// <param name="prop">Its value prop.</param>
    /// <param name="formName">The nearest form around it, or null.</param>
    public OuiValue Read(OuiComponent node, OuiProp prop, string? formName)
    {
        ArgumentNullException.ThrowIfNull(node);
        return prop.Binding is null && FieldName(node) is { } field && Session.Forms.Get(formName, field) is { IsNull: false } entered
            ? entered
            : prop.Value;
    }

    /// <summary>
    /// Writes user input through a bound prop (language.md §5.2) and into the component's form field
    /// (§5.3), since an action sends the form's fields to the model whether or not they are bound.
    /// </summary>
    /// <param name="node">The input.</param>
    /// <param name="prop">Its value prop.</param>
    /// <param name="formName">The nearest form around it, or null.</param>
    /// <param name="value">The input's new value.</param>
    public void Write(OuiComponent node, OuiProp prop, string? formName, OuiValue value)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (prop.Binding is { } key)
        {
            Session.State.Write(key, value);
        }

        if (FieldName(node) is { } field)
        {
            // The view that wrote draws what it wrote, so only a write from elsewhere needs every view to look again.
            NoteFormsWrittenElsewhere();
            Session.Forms.Set(formName, field, value, node.Spec?.Traits.FieldComponentType ?? "input");
            _forms = Session.Forms.Snapshot();

            // Unless another view shows the same field: it may sit under views whose props did not change, which skip.
            if (IsShownTwice(formName, field))
            {
                Refresh();
            }
        }

        if (formName is not null && _failedForms.ContainsKey(formName))
        {
            _failedForms[formName] = Session.ValidateForm(formName);
        }
    }

    /// <summary>Runs the action in a prop (language.md §6.3). A prop that holds no action does nothing and returns null.</summary>
    /// <param name="node">The component the user acted on.</param>
    /// <param name="prop">The component's action prop.</param>
    /// <param name="formName">The nearest form around it, or null.</param>
    /// <returns>How the action ended. When a field failed its rules, the fields show why.</returns>
    public async Task<ActionOutcome?> RunActionAsync(OuiComponent node, OuiProp prop, string? formName)
    {
        ArgumentNullException.ThrowIfNull(node);
        return prop.Value.TryGetAction(out var action) ? await RunActionAsync(node, action, formName) : null;
    }

    /// <summary>Runs an action for a component (language.md §6.3), such as <see cref="OuiAction.ToAssistant"/> for one the program gave none.</summary>
    /// <param name="node">The component the user acted on.</param>
    /// <param name="action">The action.</param>
    /// <param name="formName">The nearest form around it, or null.</param>
    /// <returns>How the action ended. When a field failed its rules, the fields show why.</returns>
    public async Task<ActionOutcome> RunActionAsync(OuiComponent node, OuiAction action, string? formName)
    {
        ArgumentNullException.ThrowIfNull(node);
        var outcome = await Session.RunActionAsync(action, new ActionOrigin(node, formName));
        if (formName is not null)
        {
            if (outcome is { Kind: ActionOutcomeKind.Invalid, Validation: { } validation })
            {
                _failedForms[formName] = validation;
            }
            else
            {
                _failedForms.Remove(formName);
            }

            // Nothing in the session changed, so the fields hear about it from here.
            Refresh();
        }

        return outcome;
    }

    /// <summary>Draws the views again, for a change that is not in the session. Every view draws, even one whose props did not change.</summary>
    public void Refresh()
    {
        // A host may call this from any thread.
        Interlocked.Increment(ref _redraws);
        _refresh();
    }

    /// <summary>The field's message from the last failed submit of its form, or null.</summary>
    /// <param name="node">The input.</param>
    /// <param name="formName">The nearest form around it, or null.</param>
    public string? FieldError(OuiComponent node, string? formName)
    {
        ArgumentNullException.ThrowIfNull(node);
        return formName is not null && _failedForms.TryGetValue(formName, out var validation) && FieldName(node) is { } field
            ? validation.Errors.FirstOrDefault(error => error.Field == field)?.Message
            : null;
    }

    /// <summary>Notes that the view draws the node's form field, if it has one, so a write to that field from another view reaches it.</summary>
    internal void NoteFieldView(OuiComponent node, string? formName, OpenUIComponent view)
    {
        if (FieldName(node) is not { } field)
        {
            return;
        }

        if (!_fieldViews.TryGetValue((formName, field), out var views))
        {
            _fieldViews[(formName, field)] = views = [];
        }

        views.RemoveAll(entry => !entry.TryGetTarget(out _));
        if (!views.Exists(entry => entry.TryGetTarget(out var drawn) && ReferenceEquals(drawn, view)))
        {
            views.Add(new WeakReference<OpenUIComponent>(view));
        }
    }

    private bool IsShownTwice(string? formName, string field) =>
        _fieldViews.TryGetValue((formName, field), out var views) && views.Count(entry => entry.TryGetTarget(out _)) > 1;

    private void NoteFormsWrittenElsewhere()
    {
        var forms = Session.Forms.Snapshot();
        if (!ReferenceEquals(forms, _forms))
        {
            _forms = forms;
            Interlocked.Increment(ref _redraws);
        }
    }

    private static string? FieldName(OuiComponent node) =>
        node.Spec?.Traits.FieldNameProp is { } nameProp && node[nameProp].Value.TryGetString(out var name) ? name : null;
}
