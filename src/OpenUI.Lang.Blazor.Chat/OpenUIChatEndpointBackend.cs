using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace OpenUI.Lang.Blazor.Chat;

/// <summary>
/// Asks a server's <c>MapOpenUIChat</c> endpoint, from OpenUI.Lang.AspNetCore, which suits Blazor WebAssembly: the key and the
/// prompt stay on the server. It posts <c>{"messages":[{"role","content"}]}</c> and reads the NDJSON chunks'
/// <c>choices[0].delta.content</c>. The server's prompt teaches its own components, so register the same component sources
/// on the client, or the replies draw as unknown. Every failure is an <see cref="OpenUIChatException"/> with a sentence a
/// person may read.
/// </summary>
public sealed class OpenUIChatEndpointBackend : IOpenUIChatBackend
{
    /// <summary>The most turns sent by default: what <c>MapOpenUIChat</c> takes by default.</summary>
    public const int DefaultMaxHistoryTurns = 100;

    /// <summary>The largest request body sent by default, in bytes: what <c>MapOpenUIChat</c> takes by default.</summary>
    public const int DefaultMaxHistoryBytes = 1024 * 1024;

    /// <summary>How much of an error response is read, in bytes. The endpoint's own are a short sentence.</summary>
    internal const int MaxErrorBytes = 4 * 1024;

    // The lines and the body are not HTML, so the default encoder's escaping of non-ASCII text only makes them longer.
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // {"messages":[ and ]}
    private const int EnvelopeBytes = 15;

    // The browser's fetch only streams a response when asked; elsewhere the option does nothing.
    private static readonly HttpRequestOptionsKey<bool> BrowserStreaming = new("WebAssemblyEnableStreamingResponse");

    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly OpenUIBlazorChatOptions _options;

    /// <summary>Creates the backend.</summary>
    /// <param name="http">The client. A relative <paramref name="endpoint"/> is resolved against its <see cref="HttpClient.BaseAddress"/>.</param>
    /// <param name="endpoint">The endpoint, such as <c>https://example.com/api/chat</c>.</param>
    /// <param name="options">The chat's options, for the history and reply limits. Null for the defaults.</param>
    public OpenUIChatEndpointBackend(HttpClient http, Uri endpoint, OpenUIBlazorChatOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(endpoint);
        _http = http;
        _endpoint = endpoint;
        _options = options ?? new OpenUIBlazorChatOptions();
    }

    /// <summary>
    /// The longest line read, in characters: room for a whole reply in one chunk with every character escaped, and the
    /// chunk's other members. A longer line fails the reply rather than filling memory.
    /// </summary>
    internal int MaxLineChars => (int)Math.Min(int.MaxValue, (long)_options.MaxReplyChars * 6 + 1024);

    /// <inheritdoc />
    /// <exception cref="OpenUIChatException">The server could not be reached, answered with an error, ended the reply before it finished, or sent something that is not a chat chunk.</exception>
    public async IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatMessage> turns, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turns);
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = Body(turns) };
        request.Options.Set(BrowserStreaming, true);
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new OpenUIChatException(await ErrorOf(response, cancellationToken), response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var lines = new LineReader(stream, MaxLineChars);
        var finished = false;
        while (await ReadLineAsync(lines, cancellationToken) is { } line)
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
            throw new OpenUIChatException("The reply ended before the server finished it.");
        }
    }

    // The newest turns that fit the endpoint's limits, as the request body.
    private ByteArrayContent Body(IReadOnlyList<ChatMessage> turns)
    {
        var encoded = turns.Select(Encode).ToList();
        var start = OpenUIChatTurns.FirstThatFits(
            turns,
            encoded.Select(item => (long)item.Length + 1).ToList(),
            EnvelopeBytes,
            _options.MaxHistoryTurns ?? DefaultMaxHistoryTurns,
            _options.MaxHistoryBytes ?? DefaultMaxHistoryBytes);

        using var buffer = new MemoryStream();
        buffer.Write("{\"messages\":["u8);
        for (var index = start; index < encoded.Count; index++)
        {
            if (index > start)
            {
                buffer.Write(","u8);
            }

            buffer.Write(encoded[index]);
        }

        buffer.Write("]}"u8);
        var content = new ByteArrayContent(buffer.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        return content;
    }

    private static byte[] Encode(ChatMessage turn)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, WriterOptions))
        {
            json.WriteStartObject();
            json.WriteString("role", turn.Role.Value);
            json.WriteString("content", turn.Text);
            json.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            throw new OpenUIChatException("The chat endpoint could not be reached.", error.StatusCode, error);
        }
    }

    private static async Task<string?> ReadLineAsync(LineReader lines, CancellationToken cancellationToken)
    {
        try
        {
            return await lines.ReadLineAsync(cancellationToken);
        }
        catch (Exception error) when (error is IOException or HttpRequestException)
        {
            throw new OpenUIChatException("The connection to the chat endpoint was lost.", null, error);
        }
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
            throw new OpenUIChatException("The chat endpoint sent a line that is not a chat chunk.", null, error);
        }
    }

    // The endpoint answers an error with { "error": "..." }, a fixed sentence a person may read. Only the start of the body is read.
    private static async Task<string> ErrorOf(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var fallback = $"The chat endpoint answered with status {(int)response.StatusCode}.";
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var body = new byte[MaxErrorBytes];
            var length = 0;
            int read;
            while (length < body.Length && (read = await stream.ReadAsync(body.AsMemory(length), cancellationToken)) > 0)
            {
                length += read;
            }

            using var json = JsonDocument.Parse(body.AsMemory(0, length));
            return json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String
                && error.GetString() is { Length: > 0 } message
                    ? message
                    : fallback;
        }
        catch (Exception error) when (error is JsonException or IOException or HttpRequestException or InvalidOperationException)
        {
            return fallback;
        }
    }

    /// <summary>Reads lines of at most a set length, so a server that never ends a line can't fill memory.</summary>
    private sealed class LineReader(Stream stream, int maxChars)
    {
        private readonly StreamReader _reader = new(stream, Encoding.UTF8);
        private readonly char[] _block = new char[8192];
        private readonly StringBuilder _line = new();
        private int _start;
        private int _end;

        public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            _line.Clear();
            while (true)
            {
                if (_start == _end)
                {
                    _start = 0;
                    _end = await _reader.ReadAsync(_block, cancellationToken);
                    if (_end == 0)
                    {
                        return _line.Length > 0 ? _line.ToString() : null;
                    }
                }

                var newline = Array.IndexOf(_block, '\n', _start, _end - _start);
                var stop = newline < 0 ? _end : newline;
                if (_line.Length + (stop - _start) > maxChars)
                {
                    throw new OpenUIChatException($"The chat endpoint sent a line longer than {maxChars} characters.");
                }

                _line.Append(_block, _start, stop - _start);
                if (newline >= 0)
                {
                    _start = newline + 1;
                    if (_line.Length > 0 && _line[^1] == '\r')
                    {
                        _line.Length--;
                    }

                    return _line.ToString();
                }

                _start = _end;
            }
        }
    }
}
