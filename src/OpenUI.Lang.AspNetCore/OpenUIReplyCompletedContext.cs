using Microsoft.AspNetCore.Http;

namespace OpenUI.Lang.AspNetCore;

/// <summary>How a reply that started streaming came to an end.</summary>
public enum OpenUIReplyOutcome
{
    /// <summary>The model finished, and the client was sent the final chunk.</summary>
    Completed,

    /// <summary>The model failed after the reply started. The stream ended early.</summary>
    Failed,

    /// <summary>The client went away, and the model call was cancelled.</summary>
    Aborted,

    /// <summary>The reply went past <see cref="OpenUILimits.MaxInputChars"/>. The model call was stopped and the stream ended early.</summary>
    TooLong,

    /// <summary>The reply took longer than <see cref="OpenUIChatOptions.ReplyTimeout"/>. The model call was cancelled and the stream ended early.</summary>
    TimedOut,
}

/// <summary>The reply that <see cref="OpenUIChatOptions.OnReplyCompleted"/> is told about.</summary>
public sealed class OpenUIReplyCompletedContext
{
    internal OpenUIReplyCompletedContext(HttpContext httpContext, string reply, ValidationReport report, OpenUIReplyOutcome outcome)
    {
        HttpContext = httpContext;
        Reply = reply;
        Report = report;
        Outcome = outcome;
    }

    /// <summary>The request, for its services, user and headers.</summary>
    public HttpContext HttpContext { get; }

    /// <summary>The text the model streamed, as the client received it.</summary>
    public string Reply { get; }

    /// <summary>
    /// <see cref="Reply"/> checked by <see cref="OpenUIValidator"/>: the parsed document and its
    /// evaluation with default state and no tool data. When <see cref="Outcome"/> is not
    /// <see cref="OpenUIReplyOutcome.Completed"/>, the reply is cut short and the document is incomplete.
    /// </summary>
    public ValidationReport Report { get; }

    /// <summary>How the reply ended.</summary>
    public OpenUIReplyOutcome Outcome { get; }
}
