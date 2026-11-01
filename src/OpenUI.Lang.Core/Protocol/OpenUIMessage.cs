using System.Collections.Immutable;
using System.Text;

namespace OpenUI.Lang.Protocol;

/// <summary>A stored message, read by its marker lines (prompt.md, section 7.3).</summary>
public sealed class OpenUIMessage
{
    private OpenUIMessage(string content, string? context, bool ended, ImmutableArray<KeyValuePair<string, string>> attributes)
    {
        Content = content;
        Context = context;
        Ended = ended;
        Attributes = attributes;
    }

    /// <summary>The last <c>content</c> section, or the whole text when it has no content marker (prompt.md, section 7.3).</summary>
    public string Content { get; }

    /// <summary>The <c>context</c> section body that follows the last content marker, or null (prompt.md, section 7.3).</summary>
    public string? Context { get; }

    /// <summary>True when the stream reached its last chunk (an <c>end</c> line, section 7.2).</summary>
    public bool Ended { get; }

    /// <summary>The decoded attributes of the last content marker, such as <c>library</c> (prompt.md, section 7.3).</summary>
    public ImmutableArray<KeyValuePair<string, string>> Attributes { get; }

    /// <summary>Reads a stored message: last content wins and unknown kinds are stripped (section 7.3).</summary>
    public static OpenUIMessage Parse(string stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        var reader = new OpenUIMessageStreamReader();
        var content = new StringBuilder();
        foreach (var update in new[] { reader.Push(stored), reader.Complete() })
        {
            if (update.IsReset)
            {
                content.Clear();
            }

            content.Append(update.Text);
        }

        return new OpenUIMessage(content.ToString(), reader.Context, reader.Ended, reader.Attributes);
    }

    /// <summary>The text a model may see: the content with every marker line stripped (section 7.5).</summary>
    public string ForModel() => Content;
}

/// <summary>Writes the marker lines (prompt.md, section 7.4).</summary>
public static class OpenUIMessageWriter
{
    /// <summary>Writes a <c>content</c> marker line, naming the library as <c>library=id@version</c> when given (prompt.md, section 7.4).</summary>
    public static void WriteContentMarker(TextWriter writer, string? libraryId, string? libraryVersion)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write("]]>openui:content");
        if (libraryId is not null)
        {
            writer.Write("?library=");
            writer.Write(Encode(libraryVersion is null ? libraryId : libraryId + "@" + libraryVersion));
        }

        writer.Write('\n');
    }

    /// <summary>Writes a <c>context</c> marker on its own line, then the JSON body (prompt.md, section 7.4).</summary>
    public static void WriteContext(TextWriter writer, ReadOnlySpan<byte> utf8Json)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write("\n]]>openui:context\n");
        writer.Write(Encoding.UTF8.GetString(utf8Json));
    }

    /// <summary>Writes the <c>end</c> line (prompt.md, section 7.4).</summary>
    public static void WriteEnd(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write("\n]]>openui:end\n");
    }

    // URLSearchParams serialization: alphanumerics and *-._ stay, space becomes +, the rest is %XX.
    private static string Encode(string value)
    {
        var text = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if (char.IsAsciiLetterOrDigit(c) || c is '*' or '-' or '.' or '_')
            {
                text.Append(c);
            }
            else if (c == ' ')
            {
                text.Append('+');
            }
            else
            {
                text.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return text.ToString();
    }
}
