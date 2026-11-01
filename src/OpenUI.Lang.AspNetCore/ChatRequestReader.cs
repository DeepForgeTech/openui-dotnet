using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;

namespace OpenUI.Lang.AspNetCore;

/// <summary>
/// Reads the OpenAI-format <c>messages</c> that react-headless's <c>openAIMessageFormat</c> sends.
/// The body is untrusted: its size and shape are checked here, and every error text is fixed, so
/// nothing the client sent is echoed back.
/// </summary>
internal static class ChatRequestReader
{
    /// <summary>The conversation to send the model, or the reason the request cannot be used.</summary>
    public readonly record struct Result(IReadOnlyList<ChatMessage>? Messages, string? Error, int StatusCode = StatusCodes.Status400BadRequest)
    {
        public static Result Rejected(string error, int statusCode = StatusCodes.Status400BadRequest) => new(null, error, statusCode);
    }

    /// <summary>Reads at most <see cref="OpenUIChatOptions.MaxRequestBodyBytes"/> of the body and parses it.</summary>
    public static async Task<Result> ReadAsync(HttpRequest request, OpenUIChatOptions options, CancellationToken cancellationToken)
    {
        var tooLarge = Result.Rejected($"The request body must not be larger than {options.MaxRequestBodyBytes} bytes.", StatusCodes.Status413PayloadTooLarge);
        if (request.ContentLength > options.MaxRequestBodyBytes)
        {
            return tooLarge;
        }

        // Counted as it arrives, since a chunked body has no Content-Length to trust.
        using var body = new MemoryStream();
        var block = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(block, cancellationToken)) > 0)
        {
            if (body.Length + read > options.MaxRequestBodyBytes)
            {
                return tooLarge;
            }

            body.Write(block, 0, read);
        }

        return Parse(body.GetBuffer().AsMemory(0, (int)body.Length), options.MaxMessages);
    }

    private static Result Parse(ReadOnlyMemory<byte> body, int maxMessages)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return ReadMessages(document.RootElement, maxMessages);
        }
        catch (JsonException)
        {
            return Result.Rejected("The request body must be valid JSON.");
        }
        catch (InvalidOperationException)
        {
            // JsonDocument decodes strings late, so a lone surrogate or a bad UTF-8 byte passes the
            // parse and only fails when a string is read.
            return Result.Rejected("The strings in the request body must be valid Unicode.");
        }
    }

    private static Result ReadMessages(JsonElement root, int maxMessages)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("messages", out var items)
            || items is not { ValueKind: JsonValueKind.Array }
            || items.GetArrayLength() == 0)
        {
            return Result.Rejected("The request body must be a JSON object with a non-empty 'messages' array.");
        }

        if (items.GetArrayLength() > maxMessages)
        {
            return Result.Rejected($"'messages' must not have more than {maxMessages} entries.");
        }

        var messages = new List<ChatMessage>(items.GetArrayLength());
        var index = 0;
        foreach (var item in items.EnumerateArray())
        {
            if (!TryReadMessage(item, index++, out var message, out var error))
            {
                return Result.Rejected(error);
            }

            if (message is not null)
            {
                messages.Add(message);
            }
        }

        return messages.Count > 0
            ? new Result(messages, null)
            : Result.Rejected("'messages' must hold at least one user or assistant message.");
    }

    // A null message with no error is one that is dropped on purpose.
    private static bool TryReadMessage(JsonElement item, int index, out ChatMessage? message, out string error)
    {
        message = null;
        if (item.ValueKind != JsonValueKind.Object)
        {
            error = $"messages[{index}] must be an object.";
            return false;
        }

        if (!item.TryGetProperty("role", out var roleElement) || roleElement.ValueKind != JsonValueKind.String)
        {
            error = $"messages[{index}].role must be a string.";
            return false;
        }

        var role = roleElement.GetString();
        if (role is not ("user" or "assistant" or "system" or "developer"))
        {
            error = $"messages[{index}].role must be 'user', 'assistant', 'system' or 'developer'.";
            return false;
        }

        if (!TryReadText(item, index, out var text, out error))
        {
            return false;
        }

        // System and developer messages are checked like any other, then dropped: the library's
        // prompt must not be something a browser can replace.
        message = role switch
        {
            "user" => new ChatMessage(ChatRole.User, text),
            "assistant" => new ChatMessage(ChatRole.Assistant, text),
            _ => null,
        };
        return true;
    }

    // Content is a string, null, or parts such as { type: "text", text } and { type: "image_url", ... }.
    // Only text reaches the model; the other part types are well formed but ignored.
    private static bool TryReadText(JsonElement item, int index, out string text, out string error)
    {
        text = "";
        error = "";
        var ok = !item.TryGetProperty("content", out var content) || content.ValueKind switch
        {
            JsonValueKind.Null => true,
            JsonValueKind.String => (text = content.GetString()!) is not null,
            JsonValueKind.Array => TryReadParts(content, out text),
            _ => false,
        };

        if (!ok)
        {
            error = $"messages[{index}].content must be a string, an array of content parts, or null.";
        }

        return ok;
    }

    private static bool TryReadParts(JsonElement parts, out string text)
    {
        var joined = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Object
                || !part.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String)
            {
                text = "";
                return false;
            }

            if (!type.ValueEquals("text"))
            {
                continue;
            }

            if (!part.TryGetProperty("text", out var partText) || partText.ValueKind != JsonValueKind.String)
            {
                text = "";
                return false;
            }

            joined.Append(partText.GetString());
        }

        text = joined.ToString();
        return true;
    }
}
