using Microsoft.Extensions.AI;

namespace OpenUI.Lang.Samples.Llm;

/// <summary>The parts of <see cref="IChatClient"/> that are the same for every provider here, which only stream.</summary>
public abstract class StreamingChatClient : IChatClient
{
    /// <inheritdoc />
    public abstract IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    /// <inheritdoc />
    public void Dispose() => GC.SuppressFinalize(this);
}
