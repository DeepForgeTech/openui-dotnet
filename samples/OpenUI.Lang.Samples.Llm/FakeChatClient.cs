using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace OpenUI.Lang.Samples.Llm;

/// <summary>
/// Streams a canned reply a few characters at a time, so the whole path can be run without a model.
/// Each app supplies its own reply, since each speaks a different component library.
/// </summary>
/// <param name="reply">Picks the reply for a conversation.</param>
public sealed class FakeChatClient(Func<IReadOnlyList<ChatMessage>, string> reply) : StreamingChatClient
{
    private const int ChunkLength = 12;

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var text = reply([.. messages]);
        for (var start = 0; start < text.Length; start += ChunkLength)
        {
            // A short pause per chunk lets the browser show the reply building up.
            await Task.Delay(TimeSpan.FromMilliseconds(15), cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, text.Substring(start, Math.Min(ChunkLength, text.Length - start))) { ModelId = "fake" };
        }
    }
}
