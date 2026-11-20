using OpenUI.Lang.Prompting;

namespace OpenUI.Lang.Blazor.Chat;

/// <summary>Options for the Blazor chat, set in <c>AddOpenUIChat</c>.</summary>
public sealed class OpenUIBlazorChatOptions
{
    private TimeSpan _replyTimeout = TimeSpan.FromMinutes(5);
    private int _maxReplyChars = OpenUILimits.Default.MaxInputChars;
    private int _titleLength = 60;

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
