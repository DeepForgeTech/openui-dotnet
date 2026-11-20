using Microsoft.Extensions.AI;

namespace OpenUI.Lang.Blazor.Chat;

/// <summary>Where a reply comes from: a model in the process, or a server that asks one.</summary>
public interface IOpenUIChatBackend
{
    /// <summary>
    /// Streams the reply to the conversation, a piece of text at a time. The turns carry no system prompt: the backend adds
    /// it, or the server it calls does. The backend sends as many of the newest turns as its limits allow, and always the
    /// last. To tell the person what went wrong, throw an <see cref="OpenUIChatException"/>: the session shows its message.
    /// Any other exception is logged and shown as a fixed sentence, since its text may name hosts, keys or prompts. Honour
    /// the token: the session cancels it to stop a reply or end one that ran too long.
    /// </summary>
    /// <param name="turns">The user messages and replies, oldest first, as <see cref="OpenUIChatTurns.For"/> builds them.</param>
    /// <param name="cancellationToken">Ends the reply.</param>
    IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatMessage> turns, CancellationToken cancellationToken);
}
