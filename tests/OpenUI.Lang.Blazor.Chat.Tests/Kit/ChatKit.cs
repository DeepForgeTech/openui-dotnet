using OpenUI.Lang.Library;

namespace OpenUI.Lang.Blazor.Chat.Tests.Kit;

/// <summary>A plain component library, so these tests exercise the chat and nothing else.</summary>
internal sealed class ChatKit() : LibraryDefinition("chat-kit", "Stack",
[
    new("All",
    [
        new("Stack", "A column.", typeof(KitStackView), [new("children", new ArrayShape(TypeShape.Any)) { Required = true }]),
        new("Card", "A card.", typeof(KitCardView), [new("children", new ArrayShape(TypeShape.Any)) { Required = true }]),
        new("TextContent", "A line.", typeof(KitTextView), [new("text", TypeShape.String) { Required = true }]),
        new("Button", "A button.", typeof(KitButtonView),
        [
            new("label", TypeShape.String) { Required = true },
            new("action", new ActionSlotShape([])),
        ]),
    ], []),
]);
