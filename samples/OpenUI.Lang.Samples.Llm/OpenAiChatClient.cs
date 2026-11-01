using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace OpenUI.Lang.Samples.Llm;

/// <summary>Calls any OpenAI-compatible <c>/chat/completions</c> endpoint with <c>stream: true</c>.</summary>
/// <remarks>
/// An error status from the endpoint is thrown as an <see cref="HttpRequestException"/> that carries
/// it, which is how <c>MapOpenUIChat</c> tells a rate limit from any other failure.
/// </remarks>
public sealed class OpenAiChatClient(HttpClient http, OpenAiSettings settings) : StreamingChatClient
{
    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(messages, cancellationToken);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(body, Encoding.UTF8);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue; // blank separators, comments and event: lines
            }

            var payload = line["data:".Length..].Trim();
            if (payload == "[DONE]")
            {
                yield break;
            }

            if (DeltaText(payload) is { Length: > 0 } text)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, text) { ModelId = settings.Model };
            }
        }
    }

    private async Task<HttpResponseMessage> SendAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["model"] = settings.Model,
            ["messages"] = new JsonArray([.. messages.Select(message => (JsonNode)new JsonObject { ["role"] = message.Role.Value, ["content"] = message.Text })]),
            ["stream"] = true,
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.BaseUrl.ToString().TrimEnd('/') + "/chat/completions"))
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (settings.ApiKey is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        }

        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            var message = ErrorMessage(text) ?? $"The model endpoint answered {(int)response.StatusCode} {response.ReasonPhrase}.";
            throw new HttpRequestException(message, inner: null, response.StatusCode);
        }
    }

    // { "choices": [{ "delta": { "content": "..." } }] }. A line of any other shape carries no text.
    private static string? DeltaText(string payload)
    {
        try
        {
            return JsonNode.Parse(payload)?["choices"]?[0]?["delta"]?["content"] is JsonValue value && value.TryGetValue<string>(out var text)
                ? text
                : null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            // Not JSON, or not shaped like a chunk (indexing a value of the wrong kind throws).
            return null;
        }
    }

    // OpenAI-compatible servers report { "error": { "message": "..." } }; some send { "error": "..." }.
    private static string? ErrorMessage(string body)
    {
        try
        {
            var error = JsonNode.Parse(body)?["error"];
            var message = error is JsonObject ? error["message"] : error;
            return message is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
