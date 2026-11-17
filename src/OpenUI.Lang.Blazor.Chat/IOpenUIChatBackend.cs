using Microsoft.Extensions.AI;

namespace OpenUI.Lang.Blazor.Chat;

/// <summary>Where a reply comes from: a model in the process, or a server that asks one.</summary>
public interface IOpenUIChatBackend
{
    /// <summary>
    /// Streams the reply to the conversation, a piece of text at a time. The turns carry no system prompt: the backend
    /// adds it, or the server it calls does. Fail with an <see cref="IOException"/> or an <see cref="HttpRequestException"/>
    /// whose message a person may read, and honour the token: the session cancels it to stop a reply or end one that ran
    /// too long.
    /// </summary>
    /// <param name="turns">The user messages and replies, oldest first, as <see cref="OpenUIChatTurns.For"/> builds them.</param>
    /// <param name="cancellationToken">Ends the reply.</param>
    IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatMessage> turns, CancellationToken cancellationToken);
}
