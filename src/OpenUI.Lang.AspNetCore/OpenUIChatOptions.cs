namespace OpenUI.Lang.AspNetCore;

/// <summary>Options for the chat endpoint that <c>MapOpenUIChat</c> maps.</summary>
public sealed class OpenUIChatOptions
{
    private int _maxRequestBodyBytes = 1024 * 1024;
    private int _maxMessages = 100;
    private TimeSpan _replyTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The largest request body the endpoint reads, in bytes. A larger body is answered with 413
    /// before it is parsed. The default is 1 MiB.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int MaxRequestBodyBytes
    {
        get => _maxRequestBodyBytes;
        set => _maxRequestBodyBytes = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The limit must be positive.");
    }

    /// <summary>
    /// The most entries the request's <c>messages</c> array may have, counting the ones that are
    /// dropped. More is answered with 400. The default is 100.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int MaxMessages
    {
        get => _maxMessages;
        set => _maxMessages = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The limit must be positive.");
    }

    /// <summary>
    /// How long one reply may take, from the model call to the last chunk. Before the reply starts,
    /// going over is answered with 504; after that, the stream ends early. The model's client has to
    /// honour its cancellation token for this to take effect. The default is 5 minutes.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive, or longer than <see cref="int.MaxValue"/> milliseconds.</exception>
    public TimeSpan ReplyTimeout
    {
        get => _replyTimeout;
        set => _replyTimeout = value > TimeSpan.Zero && value.TotalMilliseconds <= int.MaxValue
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "The limit must be positive, and at most int.MaxValue milliseconds.");
    }

    /// <summary>
    /// Runs when a reply has stopped streaming, with what the library made of it, so the host can
    /// log or validate it. It does not run for a request that was rejected, or that failed before
    /// the reply started. The response ends when this returns, so keep it quick. An exception thrown
    /// here is logged and does not reach the client.
    /// </summary>
    public Func<OpenUIReplyCompletedContext, Task>? OnReplyCompleted { get; set; }
}
