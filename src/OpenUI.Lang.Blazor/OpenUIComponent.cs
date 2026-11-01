using Microsoft.AspNetCore.Components;
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

    /// <summary>The prop's value; null when the program left it out.</summary>
    protected OuiValue Value(string prop) => Node[prop].Value;

    /// <summary>The prop as display text; empty for null and non-scalars.</summary>
    protected string Text(string prop) => RuntimeValues.ToText(Value(prop));

    /// <summary>The prop as a number, or null.</summary>
    protected double? Number(string prop) => RuntimeValues.AsNumber(Value(prop));

    /// <summary>The prop as a list; empty when it is not one.</summary>
    protected IReadOnlyList<OuiValue> Items(string prop) => Value(prop).TryGetArray(out var items) ? items : OuiArray.Empty;

    /// <summary>What an input shows: the bound variable, or else what the user entered in this field, or else the prop.</summary>
    protected OuiValue Input(string prop) => Context.Read(Node, Node[prop], FormName);

    /// <summary>Writes user input to the prop's variable, when the program bound one, and to this component's form field, when it has one.</summary>
    protected void Write(string prop, OuiValue value) => Context.Write(Node, Node[prop], FormName, value);

    /// <summary>Runs the action prop. A prop that holds no action does nothing.</summary>
    protected async Task RunAsync(string prop) => await Context.RunActionAsync(Node, Node[prop], FormName);

    /// <summary>Runs the action prop. With none, sends this message and the form state to the assistant, as a button does with its label (language.md §6.3).</summary>
    protected async Task RunAsync(string prop, string message) =>
        await Context.RunActionAsync(Node, Value(prop).TryGetAction(out var action) ? action : OuiAction.ToAssistant(message), FormName);

    /// <summary>This field's message from the last failed submit of its form, or null. It clears when the field becomes valid.</summary>
    protected string? FieldError() => Context.FieldError(Node, FormName);
}
