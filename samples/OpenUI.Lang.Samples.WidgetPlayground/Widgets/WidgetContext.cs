using Microsoft.AspNetCore.Components;
using OpenUI.Lang.Blazor;
using OpenUI.Lang.Samples.WidgetPlayground.Data;
using OpenUI.Lang.Samples.WidgetPlayground.PageDocuments;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Samples.WidgetPlayground.Widgets;

/// <summary>A write to a variable: a page variable (no <c>$</c>) or a custom widget's state variable (with <c>$</c>).</summary>
/// <param name="Name">The variable.</param>
/// <param name="Value">The new runtime value.</param>
public sealed record VariableChange(string Name, OuiValue Value);

/// <summary>
/// What a catalog widget sees of the page: its params, resolved from page variables or settings,
/// and a way to write its read-write params back.
/// </summary>
/// <param name="instance">The widget instance from the page document.</param>
/// <param name="spec">The widget's catalog definition.</param>
/// <param name="variables">The current page variable values, by name without <c>$</c>.</param>
/// <param name="setVariable">Writes one page variable.</param>
public sealed class WidgetContext(
    WidgetInstance instance,
    ComponentDefinition spec,
    IReadOnlyDictionary<string, OuiValue> variables,
    Func<VariableChange, Task> setVariable)
{
    /// <summary>The instance being drawn.</summary>
    public WidgetInstance Instance => instance;

    /// <summary>The current page variable values.</summary>
    public IReadOnlyDictionary<string, OuiValue> Variables => variables;

    /// <summary>A param's value: its bound variable's, else its setting, else the catalog default.</summary>
    public OuiValue Get(string param) =>
        instance.Bindings.TryGetValue(param, out var variable) ? variables.GetValueOrDefault(variable)
        : instance.Settings.TryGetValue(param, out var setting) ? JsonValues.FromJson(setting)
        : spec.Prop(param)?.Default ?? OuiValue.Null;

    /// <summary>A param as display text.</summary>
    public string Text(string param) => RuntimeValues.ToText(Get(param));

    /// <summary>A param as a number, or null.</summary>
    public double? Number(string param) => RuntimeValues.AsNumber(Get(param));

    /// <summary>The page variable a param is bound to, or null.</summary>
    public string? BoundVariable(string param) => instance.Bindings.GetValueOrDefault(param);

    /// <summary>Writes a read-write param's page variable. A read-only or unbound param is left alone.</summary>
    public Task SetAsync(string param, OuiValue value) =>
        spec.Prop(param) is { Access: PropAccess.ReadWrite } && BoundVariable(param) is { } variable
            ? setVariable(new VariableChange(variable, value))
            : Task.CompletedTask;

    /// <summary>Writes a page variable directly. Custom widgets use this: all their bindings are two-way.</summary>
    public Task SetVariableAsync(VariableChange change) => setVariable(change);
}

/// <summary>Base class for a catalog widget's Blazor component.</summary>
public abstract class CatalogWidget : ComponentBase
{
    /// <summary>The widget's view of the page.</summary>
    [Parameter, EditorRequired]
    public WidgetContext Context { get; set; } = null!;
}
