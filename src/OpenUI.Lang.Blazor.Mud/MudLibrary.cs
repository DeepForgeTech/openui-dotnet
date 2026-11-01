using OpenUI.Lang.Library;

namespace OpenUI.Lang.Blazor.Mud;

/// <summary>
/// The MudBlazor component set: layout, content, tables and form inputs with validation. The host
/// calls <c>AddMudServices</c> and puts the MudBlazor providers in its layout, as for any MudBlazor app.
/// </summary>
public static class MudLibrary
{
    private const string Rules = "validation rules checked when a Button inside a Form runs: required, email, url, numeric, minLength, maxLength, min, max, pattern, as in {required: true, maxLength: 80}";

    private static readonly ArrayShape Children = new(TypeShape.Any);

    /// <summary>The definition, for a host that passes it to <see cref="OpenUIProgram"/> and asks it for the system prompt.</summary>
    public static LibraryDefinition Definition { get; } = new("openui-mudblazor", "Card",
    [
        new("Layout",
        [
            new("Card", "A titled panel, the usual root of a program.", typeof(CardView),
            [
                new("title", TypeShape.String) { Required = true },
                new("children", Children) { Required = true },
            ]),
            new("Stack", "Lays its children out in a column or a row.", typeof(StackView),
            [
                new("children", Children) { Required = true },
                new("direction", new EnumShape(["column", "row"]), "default column"),
                new("spacing", TypeShape.Number, "gap between children in 4px steps, default 2"),
            ]),
            new("Form", "Groups inputs: its fields go with an action run from inside it, and a Button inside it runs only when every field passes its rules.", typeof(FormView),
            [
                new("name", TypeShape.String) { Required = true },
                new("children", Children) { Required = true },
            ])
            {
                Traits = new ComponentTraits(FormNameProp: "name"),
            },
            new("Divider", "A horizontal rule between sections.", typeof(DividerView), []),
        ], []),
        new("Content",
        [
            new("Text", "A line of text.", typeof(TextView),
            [
                new("text", TypeShape.String) { Required = true },
                new("style", new EnumShape(["body", "heading", "muted"]), "default body"),
            ]),
            new("Alert", "A highlighted message.", typeof(AlertView),
            [
                new("text", TypeShape.String) { Required = true },
                new("severity", new EnumShape(["info", "success", "warning", "error"]), "default info"),
            ]),
            new("Chip", "A small label.", typeof(ChipView),
            [
                new("text", TypeShape.String) { Required = true },
                new("color", new EnumShape(["default", "info", "success", "warning", "error"]), "default default"),
            ]),
            new("Metric", "One headline number with a label.", typeof(MetricView),
            [
                new("label", TypeShape.String) { Required = true },
                new("value", TypeShape.Any) { Required = true },
                new("unit", TypeShape.String),
            ]),
            new("Chart", "Bars or lines of one or more series. Use it for any comparison or trend, never bars drawn with text.", typeof(ChartView),
            [
                new("labels", new ArrayShape(TypeShape.String), "one per point along the axis") { Required = true },
                new("series", new ArrayShape(new ObjectShape([new PropSpec("name", TypeShape.String, true), new PropSpec("values", new ArrayShape(TypeShape.Number), true)])), "each with one number per label") { Required = true },
                new("kind", new EnumShape(["bar", "line"]), "default bar"),
            ]),
            new("Table", "Rows of values under column headings.", typeof(TableView),
            [
                new("columns", new ArrayShape(TypeShape.String)) { Required = true },
                new("rows", new ArrayShape(new ArrayShape(TypeShape.Any)), "one list of cells per row; a cell may be a value or a component") { Required = true },
                new("emptyText", TypeShape.String, "shown when there are no rows"),
            ]),
        ], []),
        new("Controls",
        [
            new("Button", "A button that runs its action.", typeof(ButtonView),
            [
                new("label", TypeShape.String) { Required = true },
                new("action", new ActionSlotShape([]), "default sends the label to the assistant"),
                new("variant", new EnumShape(["filled", "outlined", "text"]), "default filled"),
            ]),
            Field("TextField", "A single-line text input.", typeof(TextFieldView), "input",
            [
                new("value", TypeShape.String, "the entered text") { Access = PropAccess.ReadWrite },
                new("rules", new ObjectShape([]), Rules),
                new("placeholder", TypeShape.String),
            ]),
            Field("NumberField", "A number input.", typeof(NumberFieldView), "input",
            [
                new("value", TypeShape.Number, "the entered number") { Access = PropAccess.ReadWrite },
                new("rules", new ObjectShape([]), Rules),
            ]),
            Field("Select", "A dropdown that picks one of its options.", typeof(SelectView), "select",
            [
                new("options", new ArrayShape(TypeShape.Any), "each one a string, or an object {value, label}") { Required = true },
                new("value", TypeShape.Any, "the picked option's value") { Access = PropAccess.ReadWrite },
                new("rules", new ObjectShape([]), Rules),
            ]),
            Field("Slider", "Picks a number in a range by dragging.", typeof(SliderView), "slider",
            [
                new("min", TypeShape.Number) { Required = true },
                new("max", TypeShape.Number) { Required = true },
                new("value", TypeShape.Number, "the picked number") { Access = PropAccess.ReadWrite },
                new("step", TypeShape.Number, "default 1"),
            ]),
            Field("Switch", "An on/off toggle.", typeof(SwitchView), "switch",
            [
                new("value", TypeShape.Boolean, "true when on") { Access = PropAccess.ReadWrite },
            ]),
        ], []),
    ]);

    // Every input is named by its label, in its form and in what the form sends with an action.
    private static ComponentDefinition Field(string name, string description, Type view, string componentType, PropDefinition[] props) =>
        new(name, description, view, [new("label", TypeShape.String) { Required = true }, .. props])
        {
            Traits = new ComponentTraits(
                FieldNameProp: "label",
                FieldComponentType: componentType,
                FieldRulesProp: props.Any(prop => prop.Name == "rules") ? "rules" : null),
        };
}
