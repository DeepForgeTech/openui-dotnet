using System.Collections.Immutable;
using System.Text;

namespace OpenUI.Lang.Protocol;

/// <summary>What a chunk added to the content (prompt.md, section 7.3).</summary>
/// <param name="IsReset">True when a new <c>content</c> section replaced everything read before: discard it, then use <paramref name="Text"/>.</param>
/// <param name="Text">Content text to append (after the reset, when there was one).</param>
public readonly record struct ContentUpdate(bool IsReset, string Text);

/// <summary>
/// Reads a stored or streaming message with <c>]]&gt;openui:</c> marker lines and yields only the
/// content (prompt.md, section 7). A line that may still become a marker is held back until its
/// newline arrives or the stream ends (section 7.3). Not thread-safe: one producer.
/// </summary>
public sealed class OpenUIMessageStreamReader
{
    private const string Prefix = "]]>openui:";
    private const string ContentMarker = "]]>openui:content";

    private readonly StringBuilder _line = new();
    private readonly StringBuilder _midLine = new();
    private readonly StringBuilder _context = new();
    private readonly StringBuilder _update = new();
    private bool _atLineStart = true;
    private bool _markerLine;
    private bool _pendingNewline;
    private bool _emittedContent;
    private bool _reset;
    private Section _section;

    private enum Section
    {
        BeforeFirstMarker,
        Content,
        Context,
        Other,
    }

    /// <summary>The body of the last <c>context</c> section after the last <c>content</c> marker, or null (prompt.md, section 7).</summary>
    public string? Context { get; private set; }

    /// <summary>True once an <c>end</c> line was read after the last <c>content</c> marker (prompt.md, section 7).</summary>
    public bool Ended { get; private set; }

    /// <summary>The attributes of the last <c>content</c> marker, decoded (prompt.md, section 7.1).</summary>
    public ImmutableArray<KeyValuePair<string, string>> Attributes { get; private set; } = [];

    /// <summary>Reads one chunk (prompt.md, section 7).</summary>
    public ContentUpdate Push(ReadOnlySpan<char> chunk)
    {
        foreach (var c in chunk)
        {
            Step(c);
        }

        return TakeUpdate();
    }

    /// <summary>Ends the message: held text is released (a held marker line is read as a marker) (prompt.md, section 7).</summary>
    public ContentUpdate Complete()
    {
        if (_markerLine)
        {
            ReadMarker(_line.ToString());
            _line.Clear();
            _markerLine = false;
        }
        else if (_midLine.Length == ContentMarker.Length)
        {
            ReadMarker(_midLine.ToString());
            _midLine.Clear();
        }

        FlushHeld();
        if (_pendingNewline)
        {
            _pendingNewline = false;
            Emit('\n');
        }

        if (_section == Section.Context)
        {
            Context = _context.ToString();
        }

        return TakeUpdate();
    }

    private void Step(char c)
    {
        if (_markerLine)
        {
            if (c == '\n')
            {
                ReadMarker(_line.ToString());
                _line.Clear();
                _markerLine = false;
                _atLineStart = true;
            }
            else
            {
                _line.Append(c);
            }

            return;
        }

        if (c == '\n')
        {
            if (_midLine.Length == ContentMarker.Length)
            {
                ReadMarker(_midLine.ToString());
                _midLine.Clear();
                _atLineStart = true;
                return;
            }

            FlushHeld();
            if (_pendingNewline)
            {
                _pendingNewline = false;
                Emit('\n');
            }

            _pendingNewline = true;
            _atLineStart = true;
            return;
        }

        if (_atLineStart)
        {
            _line.Append(c);
            if (Prefix.AsSpan().StartsWith(_line.ToString()))
            {
                if (_line.Length == Prefix.Length)
                {
                    _markerLine = true;
                    _pendingNewline = false;
                }

                return;
            }

            _atLineStart = false;
            FlushHeld();
            return;
        }

        StepMidLine(c);
    }

    // A content marker in the middle of a line starts a new line there (section 7.1).
    private void StepMidLine(char c)
    {
        if (_midLine.Length == ContentMarker.Length)
        {
            if (c == '?' || c == '\r')
            {
                _line.Append(_midLine).Append(c);
                _midLine.Clear();
                _markerLine = true;
                _pendingNewline = false;
                return;
            }

            FlushHeld();
        }

        _midLine.Append(c);
        if (ContentMarker.AsSpan().StartsWith(_midLine.ToString()))
        {
            return;
        }

        _midLine.Length--;
        FlushHeld();
        if (c == ']')
        {
            _midLine.Append(c);
        }
        else
        {
            Emit(c);
        }
    }

    private void FlushHeld()
    {
        if (_line.Length == 0 && _midLine.Length == 0)
        {
            return;
        }

        if (_pendingNewline)
        {
            _pendingNewline = false;
            Emit('\n');
        }

        foreach (var chunk in _line.GetChunks())
        {
            foreach (var c in chunk.Span)
            {
                Emit(c);
            }
        }

        foreach (var chunk in _midLine.GetChunks())
        {
            foreach (var c in chunk.Span)
            {
                Emit(c);
            }
        }

        _line.Clear();
        _midLine.Clear();
    }

    private void Emit(char c)
    {
        if (_pendingNewline)
        {
            _pendingNewline = false;
            Emit('\n');
        }

        switch (_section)
        {
            case Section.BeforeFirstMarker or Section.Content:
                _update.Append(c);
                _emittedContent = true;
                break;
            case Section.Context:
                _context.Append(c);
                break;
        }
    }

    private void ReadMarker(string line)
    {
        var body = line[Prefix.Length..].TrimEnd('\r');
        var query = body.IndexOf('?');
        var kind = query < 0 ? body : body[..query];
        if (_section == Section.Context)
        {
            Context = _context.ToString();
        }

        switch (kind)
        {
            case "content":
                if (_emittedContent)
                {
                    _update.Clear();
                    _reset = true;
                }

                _section = Section.Content;
                _emittedContent = true;
                Context = null;
                Ended = false;
                Attributes = query < 0 ? [] : ParseAttributes(body[(query + 1)..]);
                break;
            case "context":
                _section = Section.Context;
                _context.Clear();
                break;
            case "end":
                Ended = true;
                break;
            default:
                _section = Section.Other;
                break;
        }
    }

    private ContentUpdate TakeUpdate()
    {
        var update = new ContentUpdate(_reset, _update.ToString());
        _update.Clear();
        _reset = false;
        return update;
    }

    // application/x-www-form-urlencoded, as URLSearchParams writes it; an unencoded @ is accepted.
    internal static ImmutableArray<KeyValuePair<string, string>> ParseAttributes(string query) =>
        [.. query.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(pair =>
        {
            var eq = pair.IndexOf('=');
            return KeyValuePair.Create(Decode(eq < 0 ? pair : pair[..eq]), eq < 0 ? "" : Decode(pair[(eq + 1)..]));
        })];

    private static string Decode(string text)
    {
        try
        {
            return Uri.UnescapeDataString(text.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            return text;
        }
    }
}
