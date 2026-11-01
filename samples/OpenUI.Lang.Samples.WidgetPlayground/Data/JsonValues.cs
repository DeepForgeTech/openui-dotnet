using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Samples.WidgetPlayground.Data;

/// <summary>JSON for the stored page document and the tools.</summary>
public static class JsonValues
{
    private const int MaxDisplayBytes = 256 * 1024;

    // What parsing the text back would allow, which is where this limit used to come from.
    private const int MaxDisplayDepth = 64;

    /// <summary>A JSON value as a runtime value. Tools return these.</summary>
    public static OuiValue FromJson(JsonNode? node) =>
        node is null ? OuiValue.Null : OuiValue.FromJson(JsonSerializer.SerializeToElement(node));

    /// <summary>A runtime value as JSON. Components and actions are written in the summary form of language.md Appendix B.</summary>
    public static JsonNode? ToJson(OuiValue value)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { MaxDepth = MaxDisplayDepth }))
        {
            value.WriteTo(writer);
        }

        return JsonNode.Parse(buffer.ToArray());
    }

    /// <summary>
    /// A runtime value as text for a state table. This runs during render, where a throw ends the
    /// circuit, and each level of a value writes several levels of JSON, so a generated program can
    /// reach the JSON depth limit with a value Core allows. A value is shown only if its JSON is
    /// small enough: the table writes every variable out on each render, and Core lets them hold megabytes.
    /// </summary>
    public static string ToDisplayText(OuiValue value)
    {
        try
        {
            var buffer = new CappedBuffer(MaxDisplayBytes);
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { MaxDepth = MaxDisplayDepth }))
            {
                value.WriteTo(writer);
            }

            return Encoding.UTF8.GetString(buffer.Written);
        }
        catch (TooLargeException)
        {
            return "(too large to show)";
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            return "(nested too deeply to show)";
        }
    }

    private sealed class TooLargeException : Exception;

    private sealed class CappedBuffer(int limit) : IBufferWriter<byte>
    {
        private readonly ArrayBufferWriter<byte> _inner = new();

        public ReadOnlySpan<byte> Written => _inner.WrittenSpan;

        public void Advance(int count)
        {
            _inner.Advance(count);
            if (_inner.WrittenCount > limit)
            {
                throw new TooLargeException();
            }
        }

        public Memory<byte> GetMemory(int sizeHint = 0) => _inner.GetMemory(sizeHint);

        public Span<byte> GetSpan(int sizeHint = 0) => _inner.GetSpan(sizeHint);
    }
}
