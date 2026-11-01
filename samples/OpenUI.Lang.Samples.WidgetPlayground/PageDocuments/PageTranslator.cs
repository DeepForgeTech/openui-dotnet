using System.Text.Json.Nodes;
using OpenUI.Lang.Samples.WidgetPlayground.Catalog;
using OpenUI.Lang.Blazor;
using OpenUI.Lang.Samples.WidgetPlayground.Data;
using OpenUI.Lang.Values;
using OpenUILibrary = OpenUI.Lang.Library.OpenUILibrary;

namespace OpenUI.Lang.Samples.WidgetPlayground.PageDocuments;

/// <summary>The result of translating a page program: the document, or why there is none.</summary>
/// <param name="Document">The document, or null when the program has no usable <c>Page</c>.</param>
/// <param name="Problems">What the translation could not use, for display next to the editor.</param>
public sealed record PageTranslation(PageDocument? Document, IReadOnlyList<string> Problems);

/// <summary>
/// Turns an evaluated page program into a <see cref="PageDocument"/>. Every widget param that holds
/// a binding becomes a binding to a page variable; every other param becomes a setting.
/// </summary>
/// <remarks>
/// This only works because every catalog widget param is declared bindable, read-only ones included.
/// An evaluated tree replaces a <c>$variable</c> in a non-bindable prop with its current value, so a
/// bindable prop is the only place a host can see which variable a prop was connected to.
/// </remarks>
public static class PageTranslator
{
    private const int MaxRows = 12;

    /// <summary>Translates the evaluated entry of a page program.</summary>
    /// <param name="root">The evaluated entry; it must be a <c>Page</c>.</param>
    /// <param name="state">The page's state store, which holds every page variable's default.</param>
    /// <param name="widgetLibrary">The custom widget library, to read which variables a custom widget's program uses.</param>
    public static PageTranslation Translate(OuiComponent? root, IReadOnlyDictionary<string, OuiValue> state, OpenUILibrary widgetLibrary)
    {
        if (root is null || root.Type != WidgetCatalog.Page.Name)
        {
            return new PageTranslation(null, [$"The entry must be a {WidgetCatalog.Page.Name}(...) component."]);
        }

        var variables = state
            .Where(entry => entry.Key.StartsWith('$'))
            .Select(entry => new PageVariable(entry.Key[1..], TypeName(entry.Value), JsonValues.ToJson(entry.Value)))
            .ToList();
        var variableNames = variables.Select(variable => variable.Name).ToHashSet(StringComparer.Ordinal);
        var columns = (int)Math.Clamp(RuntimeValues.AsNumber(root["columns"].Value) ?? 12, 1, 24);

        var problems = new List<string>();
        var widgets = new List<WidgetInstance>();
        var placed = root["widgets"].Value.TryGetArray(out var items) ? items.Where(item => item.Kind == OuiValueKind.Component) : [];
        foreach (var (item, index) in placed.Select((item, index) => (item, index)))
        {
            item.TryGetComponent(out var element);
            if (WidgetCatalog.FindWidget(element!.Type) is not { } spec)
            {
                problems.Add($"{element.Type} is not a catalog widget, so it was left off the page.");
                continue;
            }

            widgets.Add(Instance(element, spec, index, columns, variableNames, widgetLibrary));
        }

        var title = RuntimeValues.ToText(root["title"].Value);
        return new PageTranslation(new PageDocument(title, columns, variables, widgets), problems);
    }

    private static WidgetInstance Instance(
        OuiComponent element,
        ComponentDefinition spec,
        int index,
        int columns,
        IReadOnlySet<string> variableNames,
        OpenUILibrary widgetLibrary)
    {
        var settings = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in spec.Props.Where(prop => prop.Name is not ("w" or "h")))
        {
            var evaluated = element[prop.Name];
            if (evaluated.Binding is { } binding)
            {
                bindings[prop.Name] = binding.Name[1..];
            }
            else if (!evaluated.Value.IsNull)
            {
                settings[prop.Name] = JsonValues.ToJson(evaluated.Value);
            }
        }

        // A custom widget's own $variables bind to the page variables with the same names.
        if (spec.Name == WidgetCatalog.CustomWidgetName && element["program"].Value.TryGetString(out var program))
        {
            var usedState = new OpenUIValidator(widgetLibrary).Validate(program).Render.StateDefaults.Keys;
            foreach (var name in usedState.Select(name => name[1..]))
            {
                if (variableNames.Contains(name)) bindings[name] = name;
            }
        }

        var grid = new GridSize(Size(element, spec, "w", columns), Size(element, spec, "h", MaxRows));
        // Always with the index: inline widgets share their enclosing statement, a statement can be
        // placed twice, and PageView keys on this.
        return new WidgetInstance($"{element.StatementId ?? spec.Name}-{index + 1}", spec.Name, grid, settings, bindings);
    }

    private static int Size(OuiComponent element, ComponentDefinition spec, string prop, int max)
    {
        var size = RuntimeValues.AsNumber(element[prop].Value)
            ?? RuntimeValues.AsNumber(spec.Prop(prop)?.Default ?? OuiValue.Null)
            ?? 1;
        return (int)Math.Clamp(Math.Round(size), 1, max);
    }

    private static string TypeName(OuiValue value) => value.Kind switch
    {
        OuiValueKind.String => "string",
        OuiValueKind.Number => "number",
        OuiValueKind.Boolean => "boolean",
        _ => "any",
    };
}
