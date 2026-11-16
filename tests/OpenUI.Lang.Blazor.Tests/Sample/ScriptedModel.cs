using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.AI;

namespace OpenUI.Lang.Blazor.Tests.Sample;

/// <summary>Streams exactly what the test tells it to, and remembers each turn it was asked for.</summary>
internal sealed class ScriptedModel : IChatClient
{
    private readonly List<IReadOnlyList<ChatMessage>> _turns = [];
    private Channel<string> _chunks = Channel.CreateUnbounded<string>();

    public IReadOnlyList<IReadOnlyList<ChatMessage>> Turns
    {
        get
        {
            lock (_turns)
            {
                return [.. _turns];
            }
        }
    }

    public async Task Say(string text) => await _chunks.Writer.WriteAsync(text);

    public void Finish()
    {
        _chunks.Writer.TryComplete();
    }

    public void Fail(string message) => Fail(new IOException(message));

    public void Fail(Exception error) => _chunks.Writer.TryComplete(error);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        lock (_turns)
        {
            _turns.Add([.. messages]);
        }

        var chunks = _chunks;
        await foreach (var chunk in chunks.Reader.ReadAllAsync(cancellationToken))
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
        }

        // The next turn gets a stream of its own.
        _chunks = Channel.CreateUnbounded<string>();
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The page streams.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

internal sealed class NoApi : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new HttpRequestException("No API in this test.");
}
