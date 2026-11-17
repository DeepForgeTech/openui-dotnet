namespace OpenUI.Lang.Blazor.Chat;

/// <summary>The roles of a chat entry.</summary>
public static class OpenUIChatRoles
{
    /// <summary>What the person wrote, or an action in a reply sent on their behalf.</summary>
    public const string User = "user";

    /// <summary>A program the model wrote.</summary>
    public const string Assistant = "assistant";
}

/// <summary>One message of a conversation. The session changes <see cref="Text"/> as a reply streams.</summary>
public sealed class OpenUIChatEntry
{
    /// <summary><see cref="OpenUIChatRoles.User"/> or <see cref="OpenUIChatRoles.Assistant"/>.</summary>
    public required string Role { get; init; }

    /// <summary>What the person wrote, or the program the model wrote.</summary>
    public string Text { get; set; } = "";

    /// <summary>For a message sent by an action: the context and form values, as JSON (prompt.md §6.3).</summary>
    public string? Context { get; init; }

    /// <summary>For a reply: its error list as JSON, sent back with the next message (prompt.md §6.2).</summary>
    public string? Errors { get; set; }

    internal OpenUIChatEntry Copy() => new() { Role = Role, Text = Text, Context = Context, Errors = Errors };
}

/// <summary>A conversation, as a store keeps it.</summary>
public sealed class OpenUIConversation
{
    /// <summary>Its id.</summary>
    public required Guid Id { get; init; }

    /// <summary>The start of its first message.</summary>
    public required string Title { get; init; }

    /// <summary>When it was last saved. The store sets it.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The messages, oldest first.</summary>
    public List<OpenUIChatEntry> Messages { get; init; } = [];

    internal OpenUIConversation Copy() => new()
    {
        Id = Id,
        Title = Title,
        UpdatedAt = UpdatedAt,
        Messages = Messages.ConvertAll(entry => entry.Copy()),
    };
}
