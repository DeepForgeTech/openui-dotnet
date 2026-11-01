using OpenUI.Lang.Library;
using OpenUI.Lang.Blazor;
using OpenUI.Lang.Samples.WidgetPlayground.Data;
using OpenUI.Lang.Samples.WidgetPlayground.PageDocuments;
using OpenUI.Lang.Samples.WidgetPlayground.Primitives;
using OpenUI.Lang.Samples.WidgetPlayground.Widgets;

namespace OpenUI.Lang.Samples.WidgetPlayground.Catalog;

/// <summary>
/// Every component in the playground, defined once. The custom widget library is the primitives;
/// the page library is the catalog widgets plus <c>Page</c>. Both LibrarySpecs, parsers, prompts
/// and component registries are built from these definitions.
/// </summary>
public static class WidgetCatalog
{
    /// <summary>The name of the catalog widget that holds an inline OpenUI Lang program.</summary>
    public const string CustomWidgetName = "CustomWidget";

    private static readonly string LocationIds = string.Join(", ", SeriesData.Locations.Select(location => location.Id));
    private static readonly string MetricIds = string.Join(", ", SeriesData.Metrics.Select(metric => metric.Id));

    /// <summary>The fine-grained components custom widget programs are built from.</summary>
    public static IReadOnlyList<GroupDefinition> Primitives { get; } =
    [
        new("Layout",
        [
            new("Card", "A titled panel, the usual root of a widget.", typeof(CardView),
            [
                new("title", TypeShape.String) { Required = true },
                new("children", new ArrayShape(TypeShape.Any)) { Required = true },
            ]),
            new("Stack", "Lays its children out in a column or a row.", typeof(StackView),
            [
                new("children", new ArrayShape(TypeShape.Any)) { Required = true },
                new("direction", new EnumShape(["column", "row"]), "default column"),
                new("justify", new EnumShape(["start", "center", "end", "between"]), "default start; \"between\" pushes the first child to the start and the last to the end"),
            ]),
        ], []),
        new("Content",
        [
            new("Text", "A line of text.", typeof(TextView),
            [
                new("text", TypeShape.String) { Required = true },
                new("style", new EnumShape(["body", "heading", "muted"]), "default body"),
            ]),
            new("Metric", "One headline number with a label.", typeof(MetricView),
            [
                new("label", TypeShape.String) { Required = true },
                new("value", TypeShape.Any) { Required = true },
                new("unit", TypeShape.String),
            ]),
            new("Table", "Rows of values under column headings.", typeof(TableView),
            [
                new("columns", new ArrayShape(TypeShape.String)) { Required = true },
                new("rows", new ArrayShape(new ArrayShape(TypeShape.Any)), "one list of cells per row") { Required = true },
            ]),
            new("Chart", "A small line or bar chart of one series.", typeof(ChartView),
            [
                new("values", new ArrayShape(TypeShape.Number)) { Required = true },
                new("labels", new ArrayShape(TypeShape.String), "one per value"),
                new("kind", new EnumShape(["line", "bar"]), "default line"),
            ]),
        ], []),
        new("Controls",
        [
            new("Button", "A button that runs its action.", typeof(ButtonView),
            [
                new("label", TypeShape.String) { Required = true },
                new("action", new ActionSlotShape([])),
            ]),
            new("Select", "A drop-down list.", typeof(SelectView),
            [
                new("label", TypeShape.String) { Required = true },
                new("options", new ArrayShape(TypeShape.String)) { Required = true },
                new("value", TypeShape.String, "the chosen option") { Access = PropAccess.ReadWrite },
            ]),
            new("Slider", "A number picked on a range.", typeof(SliderView),
            [
                new("label", TypeShape.String) { Required = true },
                new("min", TypeShape.Number) { Required = true },
                new("max", TypeShape.Number) { Required = true },
                new("value", TypeShape.Number, "the chosen number") { Access = PropAccess.ReadWrite },
                new("step", TypeShape.Number, "default 1"),
            ]),
        ], []),
    ];

    /// <summary>The prebuilt widgets a page places on its grid, including the custom widget host.</summary>
    public static IReadOnlyList<ComponentDefinition> Widgets { get; } =
    [
        new("LocationPicker", "Picks a location from a list.", typeof(LocationPickerWidget),
        [
            new("value", TypeShape.String, $"a location id: {LocationIds}") { Required = true, Access = PropAccess.ReadWrite },
            .. Grid(3, 1),
        ]),
        new("DateRangePicker", "Picks how many days back to show.", typeof(DateRangePickerWidget),
        [
            new("days", TypeShape.Number, "7, 14, 30 or 90") { Required = true, Access = PropAccess.ReadWrite },
            .. Grid(3, 1),
        ]),
        new("MapView", "A map of the locations; clicking a marker picks it.", typeof(MapViewWidget),
        [
            new("location", TypeShape.String, "the highlighted location") { Required = true, Access = PropAccess.ReadWrite },
            .. Grid(4, 3),
        ]),
        new("MetricTile", "The latest reading and the average over the range.", typeof(MetricTileWidget),
        [
            new("location", TypeShape.String) { Required = true, Access = PropAccess.ReadOnly },
            new("metric", TypeShape.String, MetricIds) { Required = true, Access = PropAccess.ReadOnly },
            new("days", TypeShape.Number) { Required = true, Access = PropAccess.ReadOnly },
            .. Grid(3, 1),
        ]),
        new("TimeSeriesChart", "Plots one metric's daily readings for a location.", typeof(TimeSeriesChartWidget),
        [
            new("location", TypeShape.String) { Required = true, Access = PropAccess.ReadOnly },
            new("metric", TypeShape.String, MetricIds) { Required = true, Access = PropAccess.ReadOnly },
            new("days", TypeShape.Number) { Required = true, Access = PropAccess.ReadOnly },
            .. Grid(6, 3),
        ]),
        new(CustomWidgetName, "A widget written as an OpenUI Lang program.", typeof(CustomWidgetFrame),
        [
            new("title", TypeShape.String) { Required = true },
            new("program", TypeShape.String, "the program, in a single-quoted string; its $variables bind to the page variables of the same name") { Required = true },
            .. Grid(4, 3),
        ]),
    ];

    /// <summary>The root of a page program.</summary>
    public static ComponentDefinition Page { get; } = new("Page", "A dashboard page: widgets on a grid.", typeof(PageView),
    [
        new("title", TypeShape.String) { Required = true },
        new("widgets", new ArrayShape(new ComponentRefShape([.. Widgets.Select(widget => widget.Name)])), "placed left to right, wrapping") { Required = true },
        new("columns", TypeShape.Number, "grid columns, default 12") { Default = 12 },
    ]);

    /// <summary>The library custom widgets are written against.</summary>
    public static LibraryDefinition WidgetLibrary { get; } = new("widget-playground-widgets", "Card", Primitives);

    /// <summary>The library pages are written against.</summary>
    public static LibraryDefinition PageLibrary { get; } = new("widget-playground-pages", "Page",
    [
        new("Page", [Page], []),
        new("Widgets", Widgets,
        [
            "- Declare page variables as `$name = default` and pass them to widget params to connect widgets. A read-write param writes user changes back, and every widget bound to the same variable updates.",
            "- `w` and `h` size a widget in grid columns and rows.",
            "- A CustomWidget program can Query the tools `getSeries(location, metric, days)` and `listLocations()`, and is built from these components:",
            .. Primitives.SelectMany(group => group.Components).Select(component => $"  - {component.Signature}"),
        ]),
    ]);

    /// <summary>The catalog widget with this name, or null.</summary>
    public static ComponentDefinition? FindWidget(string name) => Widgets.FirstOrDefault(widget => widget.Name == name);

    private static PropDefinition[] Grid(int width, int height) =>
    [
        new("w", TypeShape.Number, "width in grid columns") { Default = width },
        new("h", TypeShape.Number, "height in grid rows") { Default = height },
    ];
}
