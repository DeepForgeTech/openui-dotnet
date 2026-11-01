using OpenUI.Lang.Values;

namespace OpenUI.Lang.Syntax;

/// <summary>A statement's text range in the prepared text.</summary>
/// <param name="Start">Offset of the first character.</param>
/// <param name="End">Offset just past the last character.</param>
/// <param name="NeedsClose">True when the text ends inside a string or an open bracket (language.md, section 4, rule 2).</param>
/// <param name="TooLong">True when the statement is longer than the limit and must be dropped.</param>
internal readonly record struct Segment(int Start, int End, bool NeedsClose, bool TooLong)
{
    public int Length => End - Start;
}

/// <summary>
/// Splits prepared text into statements (language.md, sections 1.4 and 4), one character at a time,
/// so batch parsing, streaming and the edit merge share one set of boundaries. A newline ends a
/// statement only outside strings, at bracket depth zero, with no ternary open; even then the
/// boundary waits for the next line's first non-whitespace character, which must not be <c>?</c>.
/// A line that does not start like <c>name =</c> is skipped up to its newline.
/// </summary>
internal sealed class Segmenter
{
    private readonly int _maxStatementChars;
    private readonly Stack<char> _brackets = new();
    private int _position;
    private int _start = -1;
    private Head _head;
    private char _quote;
    private bool _escape;
    private int _openTernaries;
    private bool _needOperand;
    private int _boundary = -1;

    public Segmenter(int maxStatementChars) => _maxStatementChars = maxStatementChars;

    private enum Head
    {
        Start,
        AfterDollar,
        Name,
        Space,
        Equals,
        Statement,
        Junk,
    }

    /// <summary>Scans characters not seen yet and appends each completed statement to <paramref name="completed"/>.</summary>
    public void Scan(CharBuffer text, List<Segment> completed)
    {
        for (; _position < text.Length; _position++)
        {
            Step(text[_position], completed);
        }
    }

    /// <summary>The pending tail: text after the last completed statement, or null when there is none.</summary>
    public Segment? Tail(int textLength)
    {
        if (_start < 0 || _head == Head.Junk)
        {
            return null;
        }

        var end = _boundary >= 0 ? _boundary : textLength;
        var needsClose = _boundary < 0 && (_quote != '\0' || _brackets.Count > 0);
        return new Segment(_start, end, needsClose, end - _start > _maxStatementChars);
    }

    /// <summary>
    /// What a tail that needs closing is missing, so it reads the same with no implicit closing (section
    /// 4, rule 2): a lone backslash made literal, the quote, then the brackets, innermost first.
    /// </summary>
    public string Closers =>
        (_escape ? "\\" : "") + (_quote != '\0' ? _quote.ToString() : "") + string.Concat(_brackets.Select(opener => opener switch { '(' => ')', '[' => ']', _ => '}' }));

    /// <summary>True when the text so far ends inside a string or an open bracket (Appendix B, <c>incomplete</c>).</summary>
    public bool EndsInsideStringOrBracket => _boundary < 0 && _head == Head.Statement && (_quote != '\0' || _brackets.Count > 0);

    private void Step(char c, List<Segment> completed)
    {
        if (_start < 0)
        {
            if (Ecma.IsWhiteSpace(c))
            {
                return;
            }

            Begin();
        }
        else if (_boundary >= 0)
        {
            if (Ecma.IsWhiteSpace(c))
            {
                return;
            }

            if (c == '?')
            {
                _boundary = -1;
            }
            else
            {
                completed.Add(new Segment(_start, _boundary, false, _boundary - _start > _maxStatementChars));
                Begin();
            }
        }

        if (_head != Head.Statement && !StepHead(c))
        {
            return;
        }

        StepStatement(c);
    }

    private void Begin()
    {
        _start = _position;
        _head = Head.Start;
        _quote = '\0';
        _escape = false;
        _brackets.Clear();
        _openTernaries = 0;
        _needOperand = false;
        _boundary = -1;
    }

    // Recognizes "name =" or "$name =" (but not "=="). Returns true when c belongs to the statement body.
    private bool StepHead(char c)
    {
        if (_head == Head.Junk)
        {
            if (c == '\n')
            {
                _start = -1;
            }

            return false;
        }

        _head = (_head, c) switch
        {
            (Head.Start, '$') => Head.AfterDollar,
            (Head.Start or Head.AfterDollar, _) when Lexer.IsIdentifierStart(c) => Head.Name,
            (Head.Name, _) when Lexer.IsIdentifierPart(c) => Head.Name,
            (Head.Name or Head.Space, ' ' or '\t') => Head.Space,
            (Head.Name or Head.Space, '=') => Head.Equals,
            (Head.Equals, '=') => Head.Junk,
            (Head.Equals, _) => Head.Statement,
            _ => Head.Junk,
        };

        if (_head == Head.Junk && c == '\n')
        {
            _start = -1;
        }

        return _head == Head.Statement;
    }

    private void StepStatement(char c)
    {
        if (_quote != '\0')
        {
            if (_escape)
            {
                _escape = false;
            }
            else if (c == '\\')
            {
                _escape = true;
            }
            else if (c == _quote)
            {
                _quote = '\0';
            }

            return;
        }

        var topLevel = _brackets.Count == 0;
        switch (c)
        {
            case '"' or '\'':
                _quote = c;
                break;
            case '(' or '[' or '{':
                _brackets.Push(c);
                break;
            case ')' or ']' or '}':
                if (_brackets.Count > 0 && _brackets.Peek() == Lexer.Opener(c))
                {
                    _brackets.Pop();
                }

                return;
            case '?' when topLevel:
                _openTernaries++;
                return;
            case ':' when topLevel && _openTernaries > 0:
                _openTernaries--;
                _needOperand = true;
                return;
            case '\n' when topLevel:
                if (_openTernaries == 0 && !_needOperand)
                {
                    _boundary = _position;
                }

                return;
            default:
                if (!(Lexer.IsIdentifierPart(c) || c is '$' or '@'))
                {
                    return;
                }

                break;
        }

        if (topLevel)
        {
            _needOperand = false;
        }
    }
}
