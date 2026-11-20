using OpenUI.Lang.Prompting;

namespace OpenUI.Lang.Blazor.Chat;

/// <summary>Options for the Blazor chat, set in <c>AddOpenUIChat</c>.</summary>
public sealed class OpenUIBlazorChatOptions
{
    private TimeSpan _replyTimeout = TimeSpan.FromMinutes(5);
    private int _maxReplyChars = OpenUILimits.Default.MaxInputChars;
    private int _titleLength = 60;
    private int? _maxHistoryTurns;
    private int? _maxHistoryBytes;

    /// <summary>
    /// The most turns sent with a message, the newest kept and the oldest dropped. The stored conversation keeps them all.
    /// Null, the default, takes the backend's: 100 for an endpoint, the most <c>MapOpenUIChat</c> takes by default, and 100 in
    /// process. Set it to match an endpoint whose <c>MaxMessages</c> is lower.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int? MaxHistoryTurns
    {
        get => _maxHistoryTurns;
        set => _maxHistoryTurns = value is null or > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The limit must be positive.");
    }

    /// <summary>
    /// The most UTF-8 bytes sent with a message, the newest turns kept and the oldest dropped: for an endpoint the whole request
    /// body, in process the turns' text. Null, the default, takes the backend's: 1 MiB for an endpoint, the most
    /// <c>MapOpenUIChat</c> takes by default, and 512 KiB in process. The newest message is always sent, whatever its size.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int? MaxHistoryBytes
    {
        get => _maxHistoryBytes;
        set => _maxHistoryBytes = value is null or > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The limit must be positive.");
    }

    /// <summary>
    /// How long one reply may take. Past it the reply is cut off and keeps what came. The backend has to honour its
    /// cancellation token for this to take effect. The default is 5 minutes.
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
    /// The longest reply kept, in characters. A longer one is cut off there and keeps what came. The default is
    /// <c>OpenUILimits.Default.MaxInputChars</c>, past which the parser reads nothing.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int MaxReplyChars
    {
        get => _maxReplyChars;
        set => _maxReplyChars = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The limit must be positive.");
    }

    /// <summary>How much of the first message becomes a conversation's title, in characters. The default is 60.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int TitleLength
    {
        get => _titleLength;
        set => _titleLength = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The length must be positive.");
    }

    /// <summary>
    /// What the prompt teaches beside the registered components, such as the tools and extra rules. <c>UseChatClient</c>
    /// builds the system prompt from it. In endpoint mode the server builds the prompt, and only <see cref="PromptOptions.Tools"/>
    /// is read here, by the wiring check.
    /// </summary>
    public PromptOptions? PromptOptions { get; set; }

    /// <summary>
    /// Runs Core's <c>OpenUIWiringCheck</c> on each finished reply, against <see cref="PromptOptions"/>' tools. Its findings
    /// show under the reply with the parser's errors and go back to the model with the next message. Off by default.
    /// </summary>
    public bool CheckWiring { get; set; }
}
