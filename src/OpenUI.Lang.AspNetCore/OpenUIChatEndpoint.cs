using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OpenUI.Lang.AspNetCore;

/// <summary>
/// The chat request handler: reads the OpenAI-format messages, asks the <see cref="IChatClient"/>
/// for a streaming reply, and writes it as NDJSON <c>ChatCompletionChunk</c> lines.
/// </summary>
internal sealed class OpenUIChatEndpoint(OpenUIChatSetup setup, ILogger logger)
{
    // The default encoder would write the apostrophes in the messages as \u0027.
    private static readonly JsonSerializerOptions ErrorJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // The parser ignores anything past this, so there is no use in streaming or holding more of it.
    private static readonly int MaxReplyChars = OpenUILimits.Default.MaxInputChars;

    public async Task HandleAsync(HttpContext http)
    {
        var cancellationToken = http.RequestAborted;
        if (!http.Request.HasJsonContentType())
        {
            await RejectAsync(http, StatusCodes.Status415UnsupportedMediaType, "The request must have the content type application/json.");
            return;
        }

        ChatRequestReader.Result request;
        try
        {
            request = await ChatRequestReader.ReadAsync(http.Request, setup.Options, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (request.Messages is not { } conversation)
        {
            await RejectAsync(http, request.StatusCode, request.Error!);
            return;
        }

        // The library's prompt always leads. The client's own system messages were dropped by the reader.
        List<ChatMessage> messages = [new(ChatRole.System, setup.Prompt), .. conversation];
        var chatClient = http.RequestServices.GetRequiredService<IChatClient>();

        var hook = setup.Options.OnReplyCompleted;
        var reply = hook is null ? null : new StringBuilder();
        var writer = new ChatChunkWriter();
        var started = false;
        var replyChars = 0;
        var outcome = OpenUIReplyOutcome.Completed;
        using var replyLimit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        replyLimit.CancelAfter(setup.Options.ReplyTimeout);

        void StartResponse()
        {
            if (!started)
            {
                started = true;
                http.Response.ContentType = "application/x-ndjson";
            }
        }

        try
        {
            await foreach (var update in chatClient.GetStreamingResponseAsync(messages, cancellationToken: replyLimit.Token))
            {
                if (update.Text is not { Length: > 0 } text)
                {
                    continue;
                }

                replyChars += text.Length;
                if (replyChars > MaxReplyChars)
                {
                    // Leaving the loop disposes the stream, which ends the model call.
                    outcome = OpenUIReplyOutcome.TooLong;
                    ChatLog.ReplyTooLong(logger, MaxReplyChars);
                    break;
                }

                // The response is committed by the first text, so a failure before it can still be a status code.
                StartResponse();
                reply?.Append(text);
                await WriteLineAsync(http, writer.Content(text, update.ModelId), replyLimit.Token);
            }

            StartResponse();
            if (outcome == OpenUIReplyOutcome.Completed)
            {
                await WriteLineAsync(http, writer.Stop(), replyLimit.Token);
            }
        }
        catch (Exception error) when (cancellationToken.IsCancellationRequested && IsRecoverable(error))
        {
            outcome = OpenUIReplyOutcome.Aborted;
            ChatLog.ClientAborted(logger);
        }
        catch (Exception error) when (!started && IsRecoverable(error))
        {
            var (status, message) = Describe(error);
            ChatLog.ModelFailedBeforeReply(logger, error, status);
            await RejectAsync(http, status, message);
            return;
        }
        catch (Exception error) when (IsRecoverable(error))
        {
            // The status line has gone out, so the stream just ends early.
            outcome = replyLimit.IsCancellationRequested ? OpenUIReplyOutcome.TimedOut : OpenUIReplyOutcome.Failed;
            ChatLog.ModelFailedMidStream(logger, error);
        }

        if (started && hook is not null)
        {
            await RunHookAsync(hook, http, reply!.ToString(), outcome);
        }
    }

    private async Task RunHookAsync(Func<OpenUIReplyCompletedContext, Task> hook, HttpContext http, string reply, OpenUIReplyOutcome outcome)
    {
        try
        {
            await hook(new OpenUIReplyCompletedContext(http, reply, setup.Validator.Validate(reply), outcome));
        }
        catch (Exception error) when (IsRecoverable(error))
        {
            ChatLog.ReplyCompletedHookFailed(logger, error);
        }
    }

    // Running out of memory is the process's problem, and nothing about the model to tell a client.
    private static bool IsRecoverable(Exception error) => error is not OutOfMemoryException;

    private static async Task WriteLineAsync(HttpContext http, ReadOnlyMemory<byte> line, CancellationToken cancellationToken)
    {
        await http.Response.Body.WriteAsync(line, cancellationToken);
        await http.Response.Body.FlushAsync(cancellationToken);
    }

    // The client never sees the exception text, which can name hosts, keys or prompts. It is logged.
    private static (int Status, string Message) Describe(Exception error) => error switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } =>
            (StatusCodes.Status429TooManyRequests, "The model is rate limited. Try again shortly."),
        TimeoutException or OperationCanceledException =>
            (StatusCodes.Status504GatewayTimeout, "The model took too long to answer."),
        _ => (StatusCodes.Status502BadGateway, "The model request failed."),
    };

    private static Task RejectAsync(HttpContext http, int statusCode, string error)
    {
        http.Response.StatusCode = statusCode;
        http.Response.ContentType = "application/json; charset=utf-8";
        return http.Response.WriteAsync(new JsonObject { ["error"] = error }.ToJsonString(ErrorJson), http.RequestAborted);
    }
}
