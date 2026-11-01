namespace OpenUI.Lang.Samples.BlazorApp.Chat;

/// <summary>One message of a conversation. The page changes <see cref="Text"/> as a reply streams.</summary>
public sealed class ChatEntry
{
    /// <summary><c>user</c> or <c>assistant</c>.</summary>
    public required string Role { get; init; }

    /// <summary>What the person wrote, or the program the model wrote.</summary>
    public string Text { get; set; } = "";

    /// <summary>For a message sent by an action: the context and form values, as JSON (prompt.md §6.3).</summary>
    public string? Context { get; init; }

    /// <summary>For a reply: its error list as JSON, sent back with the next message (prompt.md §6.2).</summary>
    public string? Errors { get; set; }
}

/// <summary>A stored conversation.</summary>
public sealed class Conversation
{
    /// <summary>Its id, which is also its address.</summary>
    public required Guid Id { get; init; }

    /// <summary>The start of its first message.</summary>
    public required string Title { get; init; }

    /// <summary>When its last message was added.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The messages, oldest first.</summary>
    public List<ChatEntry> Messages { get; init; } = [];
}
