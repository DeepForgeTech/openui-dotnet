using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OpenUI.Lang.AspNetCore;

/// <summary>
/// Writes the OpenAI <c>ChatCompletionChunk</c> lines for one reply. Each line is one whole JSON
/// object followed by a newline, with no <c>data:</c> prefix, as react-headless's
/// <c>openAIReadableStreamAdapter</c> reads them. The first content chunk carries <c>role</c>, as
/// OpenAI's do.
/// </summary>
internal sealed class ChatChunkWriter
{
    // The lines are not HTML, so the default encoder's escaping of &lt; and non-ASCII text is noise.
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly string _id = $"chatcmpl-{Guid.NewGuid():N}";
    private readonly long _created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private readonly ArrayBufferWriter<byte> _buffer = new();
    private string _model = "unknown";
    private bool _sentRole;

    /// <summary>The line for a piece of reply text. The bytes are valid until the next call.</summary>
    /// <param name="text">The text.</param>
    /// <param name="model">The model that produced it, when the provider says.</param>
    public ReadOnlyMemory<byte> Content(string text, string? model)
    {
        _model = model ?? _model;
        var role = _sentRole ? null : "assistant";
        _sentRole = true;
        return Line(role, text, finishReason: null);
    }

    /// <summary>The last line, with <c>finish_reason</c> <c>"stop"</c>. The bytes are valid until the next call.</summary>
    public ReadOnlyMemory<byte> Stop() => Line(role: null, content: null, finishReason: "stop");

    private ReadOnlyMemory<byte> Line(string? role, string? content, string? finishReason)
    {
        _buffer.ResetWrittenCount();
        using (var json = new Utf8JsonWriter(_buffer, WriterOptions))
        {
            json.WriteStartObject();
            json.WriteString("id", _id);
            json.WriteString("object", "chat.completion.chunk");
            json.WriteNumber("created", _created);
            json.WriteString("model", _model);
            json.WriteStartArray("choices");
            json.WriteStartObject();
            json.WriteNumber("index", 0);
            json.WriteStartObject("delta");
            if (role is not null)
            {
                json.WriteString("role", role);
            }

            if (content is not null)
            {
                json.WriteString("content", content);
            }

            json.WriteEndObject();
            json.WriteString("finish_reason", finishReason);
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteEndObject();
        }

        _buffer.Write("\n"u8);
        return _buffer.WrittenMemory;
    }
}
