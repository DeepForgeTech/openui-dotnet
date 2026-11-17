using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace OpenUI.Lang.Blazor.Chat;

/// <summary>
/// Asks a server's <c>MapOpenUIChat</c> endpoint, from OpenUI.Lang.AspNetCore, which suits Blazor WebAssembly: the key and the
/// prompt stay on the server. It posts <c>{"messages":[{"role","content"}]}</c> and reads the NDJSON chunks'
/// <c>choices[0].delta.content</c>. The server's prompt teaches its own components, so register the same component sources
/// on the client, or the replies draw as unknown.
/// </summary>
public sealed class EndpointBackend : IOpenUIChatBackend
{
    // The browser's fetch only streams a response when asked; elsewhere the option does nothing.
    private static readonly HttpRequestOptionsKey<bool> BrowserStreaming = new("WebAssemblyEnableStreamingResponse");

    private readonly HttpClient _http;
    private readonly Uri _endpoint;

    /// <summary>Creates the backend.</summary>
    /// <param name="http">The client. A relative <paramref name="endpoint"/> is resolved against its <see cref="HttpClient.BaseAddress"/>.</param>
    /// <param name="endpoint">The endpoint, such as <c>/api/chat</c>.</param>
    public EndpointBackend(HttpClient http, Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(endpoint);
        _http = http;
        _endpoint = endpoint;
    }

    /// <inheritdoc />
    /// <exception cref="HttpRequestException">The server answered with an error status. The message is the server's <c>error</c>, or the status when it gave none.</exception>
    /// <exception cref="IOException">The stream ended before the server finished the reply, or a line could not be read.</exception>
    public async IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatMessage> turns, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turns);
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = Body(turns) };
        request.Options.Set(BrowserStreaming, true);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(await ErrorOf(response, cancellationToken), null, response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var finished = false;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var (content, stop) = ReadChunk(line);
            finished |= stop;
            if (content is { Length: > 0 })
            {
                yield return content;
            }
        }

        // The endpoint ends a reply that failed, timed out or ran too long without the finishing line.
        if (!finished)
        {
            throw new IOException("The reply ended before the server finished it.");
        }
    }

    private static ByteArrayContent Body(IReadOnlyList<ChatMessage> turns)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteStartArray("messages");
            foreach (var turn in turns)
            {
                json.WriteStartObject();
                json.WriteString("role", turn.Role.Value);
                json.WriteString("content", turn.Text);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        var content = new ByteArrayContent(buffer.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        return content;
    }

    private static (string? Content, bool Stop) ReadChunk(string line)
    {
        try
        {
            using var chunk = JsonDocument.Parse(line);
            if (chunk.RootElement.ValueKind != JsonValueKind.Object
                || !chunk.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                return (null, false);
            }

            var choice = choices[0];
            var stop = choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String;
            var content = choice.TryGetProperty("delta", out var delta)
                && delta.ValueKind == JsonValueKind.Object
                && delta.TryGetProperty("content", out var text)
                && text.ValueKind == JsonValueKind.String
                    ? text.GetString()
                    : null;
            return (content, stop);
        }
        catch (JsonException error)
        {
            throw new IOException("The server sent a line that is not a chat chunk.", error);
        }
    }

    // The endpoint answers an error with { "error": "..." }, a fixed sentence a person may read.
    private static async Task<string> ErrorOf(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var fallback = $"The chat endpoint answered with status {(int)response.StatusCode}.";
        try
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return body.RootElement.ValueKind == JsonValueKind.Object
                && body.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String
                && error.GetString() is { Length: > 0 } message
                    ? message
                    : fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }
}
