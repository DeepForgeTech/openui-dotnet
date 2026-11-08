using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor;

/// <summary>
/// Base class for a Blazor component that draws one program component. The renderer passes the
/// evaluated <see cref="Node"/>; the helpers read its props and send what the user does back to the
/// session. A prop bound to a state variable already holds the variable's current value.
/// </summary>
public abstract class OpenUIComponent : ComponentBase
{
    /// <summary>The evaluated component: its type name and props.</summary>
    [Parameter, EditorRequired]
    public OuiComponent Node { get; set; } = null!;

    /// <summary>The program this component belongs to.</summary>
    [CascadingParameter]
    public OpenUIContext Context { get; set; } = null!;

    /// <summary>The nearest form around this component, or null for the page scope (language.md §5.3).</summary>
    [CascadingParameter(Name = OpenUIContext.FormCascade)]
    public string? FormName { get; set; }

    private Drawn? _drawn;
    private (string? Form, string Field)? _field;

    /// <summary>True while a <see cref="RunOnceAsync"/> is running, so a view can show its control as busy.</summary>
    protected bool IsRunning { get; private set; }

    /// <summary>
    /// Takes the new props, and draws again only when they differ by value from the last ones drawn, or something else
    /// the view reads did change: the stream ending, a form field written by someone else, or a <see cref="OpenUIContext.Refresh"/>.
    /// Each evaluation hands every view a new <see cref="Node"/>, so without this every view would draw on every change.
    /// What the user does to a view draws it as usual. A view with parameters of its own always draws when its parent does,
    /// and one that reads anything else outside its props calls <see cref="OpenUIContext.Refresh"/> when that changes.
    /// </summary>
    public sealed override Task SetParametersAsync(ParameterView parameters)
    {
        // Here and not in OnParametersSet, so no view can override it away and forget it.
        var hasOwnParameters = HasOwnParameters(parameters);
        parameters.SetParameterProperties(this);
        var isStreaming = Context.Session.Current.IsStreaming;
        if (!isStreaming)
        {
            SeedField();
        }

        _field = Context.NoteFieldView(Node, FormName, this, _field);
        var heldChanged = HeldChanged();
        var drawn = new Drawn(Node, FormName, isStreaming, Context.Redraws);
        if (!heldChanged && !hasOwnParameters && _drawn is { } last && last.IsSame(drawn))
        {
            return Task.CompletedTask;
        }

        _drawn = drawn;
        return base.SetParametersAsync(ParameterView.Empty);
    }

    /// <summary>Draws the view now, for a change to what it shows that its props don't carry.</summary>
    internal void DrawAgain() => StateHasChanged();

    // A view's own parameters, beside the ones the renderer passes, are never compared, so a view that has any always draws.
    private static bool HasOwnParameters(ParameterView parameters)
    {
        foreach (var parameter in parameters)
        {
            if (parameter.Name is not (nameof(Node) or nameof(Context) or nameof(FormName)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when what <see cref="OpenUIWritableComponent"/> holds changed since this was last asked, so the view must draw it.</summary>
    private protected virtual bool HeldChanged() => false;

    /// <summary>
    /// Called before each draw once the program has finished, so a view can put what it draws as chosen into its field: what an
    /// action sends and what validation sees must match the page. It waits for the stream because a default still being
    /// written would stick as a half-written value. Write only what is missing, or each draw writes again.
    /// </summary>
    protected virtual void SeedField()
    {
    }

    /// <summary>The prop's value; null when the program left it out.</summary>
    protected OuiValue Value(string prop) => Node[prop].Value;

    /// <summary>The prop as display text; empty for null and non-scalars.</summary>
    protected string Text(string prop) => RuntimeValues.ToText(Value(prop));

    /// <summary>The prop as a number, or null.</summary>
    protected double? Number(string prop) => RuntimeValues.AsNumber(Value(prop));

    /// <summary>True when the prop is the boolean true.</summary>
    protected bool Flag(string prop) => Node.Flag(prop);

    /// <summary>The prop as a list; empty when it is not one.</summary>
    protected IReadOnlyList<OuiValue> Items(string prop) => Value(prop).TryGetArray(out var items) ? items : OuiArray.Empty;

    /// <summary>The item components in a list prop, in order. Read their props with <see cref="RuntimeValues.Text(OuiComponent, string)"/> and the like.</summary>
    protected IReadOnlyList<OuiComponent> ItemComponents(string prop) => [.. RuntimeValues.Components(Value(prop))];

    /// <summary>What an input shows: the bound variable, or else what the user entered in this field, or else the prop.</summary>
    protected virtual OuiValue Input(string prop) => Context.Read(Node, Node[prop], FormName);

    /// <summary>Writes user input to the prop's variable, when the program bound one, and to this component's form field, when it has one.</summary>
    protected virtual void Write(string prop, OuiValue value) => Context.Write(Node, Node[prop], FormName, value);

    /// <summary>Runs the action prop. A prop that holds no action does nothing.</summary>
    protected async Task RunAsync(string prop) => await Context.RunActionAsync(Node, Node[prop], FormName);

    /// <summary>Runs the action prop. With none, sends this message and the form state to the assistant, as a button does with its label (language.md §6.3).</summary>
    protected async Task RunAsync(string prop, string message) =>
        await Context.RunActionAsync(Node, Value(prop).TryGetAction(out var action) ? action : OuiAction.ToAssistant(message), FormName);

    /// <summary>Runs what the user asked for, unless the last one is still running: a double click acts once.</summary>
    protected async Task RunOnceAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        try
        {
            await action();
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>True for the keys that press an element with <c>role="button"</c>: Enter and Space.</summary>
    protected static bool IsPressKey(KeyboardEventArgs key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.Key is "Enter" or " ";
    }

    /// <summary><see cref="RunOnceAsync"/> for a key on an element with <c>role="button"</c>. Any other key does nothing.</summary>
    protected Task RunOnKeyAsync(KeyboardEventArgs key, Func<Task> action) => IsPressKey(key) ? RunOnceAsync(action) : Task.CompletedTask;

    /// <summary>This field's message from the last failed submit of its form, or null. It clears when the field becomes valid.</summary>
    protected string? FieldError() => Context.FieldError(Node, FormName);

    // What a view drew from. The node is compared by value; the rest are what a view reads from outside its props.
    private sealed record Drawn(OuiComponent Node, string? FormName, bool IsStreaming, int Redraws)
    {
        public bool IsSame(Drawn other) =>
            FormName == other.FormName
            && IsStreaming == other.IsStreaming
            && Redraws == other.Redraws
            && ((OuiValue)Node).Equals(other.Node);
    }
}
