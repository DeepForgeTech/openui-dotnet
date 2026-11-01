using OpenUI.Lang.Library;
using OpenUI.Lang.Prompting;
using OpenUI.Lang.Blazor;
using OpenUI.Lang.Samples.WidgetPlayground.Data;

namespace OpenUI.Lang.Samples.WidgetPlayground.Catalog;

/// <summary>Everything built from one <see cref="LibraryDefinition"/>'s LibrarySpec.</summary>
/// <param name="Title">A name for the UI.</param>
/// <param name="Spec">The LibrarySpec.</param>
/// <param name="Library">The parser's library, read from <paramref name="Spec"/>.</param>
/// <param name="Prompt">The system prompt for writing a whole program, generated from <paramref name="Spec"/>.</param>
/// <param name="EditPrompt">The same prompt with edit mode on (spec §7), for patching the current program.</param>
public sealed record LibraryBundle(string Title, LibrarySpec Spec, OpenUILibrary Library, string Prompt, string EditPrompt);

/// <summary>The widget and page libraries, built once at startup from <see cref="WidgetCatalog"/>.</summary>
/// <param name="Widget">Custom widget programs: the primitives, with tools.</param>
/// <param name="Page">Page programs: the catalog widgets and <c>Page</c>.</param>
/// <param name="WidgetComponents">The Blazor components the renderer draws custom widget programs with.</param>
public sealed record PlaygroundLibraries(LibraryBundle Widget, LibraryBundle Page, ComponentRegistry WidgetComponents)
{
    /// <summary>Builds both bundles and the widget component registry.</summary>
    public static PlaygroundLibraries Build()
    {
        return new PlaygroundLibraries(
            Bundle("Custom widget library", WidgetCatalog.WidgetLibrary, new PromptOptions
            {
                Tools = PlaygroundTools.PromptTools,
                AdditionalRules = [PageVariablesRule()],
            }),
            Bundle("Page library", WidgetCatalog.PageLibrary, new PromptOptions { Bindings = true }),
            WidgetCatalog.WidgetLibrary.Components);
    }

    // The model has to know what the page passes in, and which values it takes, to wire the widget to it.
    private static string PageVariablesRule() =>
        "The page passes its variables in, already set: "
        + $"$location (string: {string.Join(", ", SeriesData.Locations.Select(l => l.Id))}), "
        + $"$metric (string: {string.Join(", ", SeriesData.Metrics.Select(m => m.Id))}) and "
        + $"$days (number, 1 to {SeriesData.MaxDays}). "
        + "Do not declare them. Read them in Query args and text, and bind them to a Select or Slider to change them.";

    private static LibraryBundle Bundle(string title, LibraryDefinition definition, PromptOptions options)
    {
        // One definition feeds the parser and both prompts, so they cannot drift apart.
        return new LibraryBundle(
            title,
            definition.Spec,
            definition.Library,
            definition.SystemPrompt(options),
            definition.SystemPrompt(options with { EditMode = true }));
    }
}
