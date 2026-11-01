using Microsoft.Extensions.Logging;

namespace OpenUI.Lang.AspNetCore;

internal static partial class ChatLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "The model call failed before the reply started; answering {StatusCode}")]
    public static partial void ModelFailedBeforeReply(ILogger logger, Exception exception, int statusCode);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "The model call failed mid-stream; the reply ends early")]
    public static partial void ModelFailedMidStream(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "The client went away; the model call is cancelled")]
    public static partial void ClientAborted(ILogger logger);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "OnReplyCompleted threw")]
    public static partial void ReplyCompletedHookFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "The reply passed {MaxChars} characters; the model call is stopped and the reply ends early")]
    public static partial void ReplyTooLong(ILogger logger, int maxChars);
}
