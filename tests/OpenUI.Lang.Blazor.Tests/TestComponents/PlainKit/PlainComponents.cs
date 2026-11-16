using OpenUI.Lang.Library;

namespace OpenUI.Lang.Blazor.Tests.TestComponents.PlainKit;

/// <summary>A component package written from nothing for no UI library: its own definitions, its own names and plain HTML.</summary>
internal sealed class PlainComponents() : LibraryDefinition("plain", "Column",
[
    new("Plain",
    [
        new("Column", "Stacks things.", typeof(PlainColumnView),
        [
            new("children", new ArrayShape(new ComponentRefShape(["Heading", "Field", "Press", "Notice"]))) { Required = true },
            new("gap", TypeShape.Number),
        ]),
        new("Heading", "A heading.", typeof(PlainHeadingView),
        [
            new("text", TypeShape.String) { Required = true },
            new("level", new EnumShape(["one", "two"])),
        ]),
        new("Field", "A text field.", typeof(PlainFieldView),
        [
            new("name", TypeShape.String) { Required = true },
            new("value", TypeShape.String) { Access = PropAccess.ReadWrite },
        ])
        {
            Traits = new ComponentTraits(FieldNameProp: "name", FieldComponentType: "input"),
        },
        new("Press", "A button.", typeof(PlainPressView),
        [
            new("text", TypeShape.String) { Required = true },
            new("onPress", new ActionSlotShape([])),
        ]),
        new("Notice", "A message.", typeof(PlainNoticeView),
        [
            new("message", TypeShape.String) { Required = true },
        ]),
    ], []),
]);

/// <summary>A second source, as a host's own beside a package's: it adds a component to a slot of the first and redraws another.</summary>
internal sealed class PlainExtras() : LibraryDefinition("plain-extras", null,
[
    new("Extras",
    [
        new("Badge", "A small label.", typeof(PlainBadgeView), [new("text", TypeShape.String) { Required = true }]) { Slots = ["Column"] },
    ], []),
],
new Dictionary<string, Type> { ["Notice"] = typeof(PlainLoudNoticeView) });
