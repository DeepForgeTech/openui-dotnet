using System.Globalization;
using System.Text;

namespace OpenUI.Lang.Syntax;

internal enum TokenKind
{
    End,
    Identifier,
    StateName,
    AtName,
    Number,
    String,
    True,
    False,
    Null,
    Assign,
    Equal,
    NotEqual,
    Greater,
    Less,
    GreaterOrEqual,
    LessOrEqual,
    Plus,
    Minus,
    Star,
    Slash,
    Percent,
    AndAnd,
    OrOr,
    Bang,
    Question,
    Colon,
    Dot,
    Comma,
    OpenParen,
    CloseParen,
    OpenBracket,
    CloseBracket,
    OpenBrace,
    CloseBrace,
}

/// <summary>
/// One token. <see cref="Text"/> is the identifier (with its <c>$</c> or without its <c>@</c>), the
/// decoded string, or the number's source text; <see cref="Number"/> is a number's value.
/// </summary>
internal readonly record struct Token(TokenKind Kind, int Start, string? Text = null, double Number = 0)
{
    /// <summary>True for tokens after which <c>-</c> is subtraction (language.md, section 1.3).</summary>
    public bool IsValue => Kind is TokenKind.Number or TokenKind.String or TokenKind.True or TokenKind.False or TokenKind.Null
        or TokenKind.Identifier or TokenKind.StateName or TokenKind.CloseParen or TokenKind.CloseBracket;
}

/// <summary>
/// Lexes one statement (language.md, section 1.3). Lexing never fails: unknown characters are skipped.
/// With implicit closing (section 4, rule 2) an unterminated string ends at the end of the text and
/// unclosed brackets get synthetic closers in reverse order.
/// </summary>
internal static class Lexer
{
    public static List<Token> Tokenize(string text, bool implicitClose)
    {
        var tokens = new List<Token>();
        var brackets = new Stack<char>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (IsIdentifierStart(c))
            {
                var start = i;
                i = SkipIdentifier(text, i);
                var name = text[start..i];
                tokens.Add(name switch
                {
                    "true" => new Token(TokenKind.True, start),
                    "false" => new Token(TokenKind.False, start),
                    "null" => new Token(TokenKind.Null, start),
                    _ => new Token(TokenKind.Identifier, start, name),
                });
                continue;
            }

            if (IsDigit(c) || (c == '-' && i + 1 < text.Length && IsDigit(text[i + 1]) && (tokens.Count == 0 || !tokens[^1].IsValue)))
            {
                tokens.Add(ReadNumber(text, ref i));
                continue;
            }

            switch (c)
            {
                case '"' or '\'':
                    tokens.Add(ReadString(text, ref i, implicitClose));
                    continue;
                case '$' or '@' when i + 1 < text.Length && IsIdentifierStart(text[i + 1]):
                    var nameStart = i;
                    i = SkipIdentifier(text, i + 1);
                    tokens.Add(c == '$'
                        ? new Token(TokenKind.StateName, nameStart, text[nameStart..i])
                        : new Token(TokenKind.AtName, nameStart, text[(nameStart + 1)..i]));
                    continue;
                case '(' or '[' or '{':
                    brackets.Push(c);
                    break;
                case ')' or ']' or '}':
                    if (brackets.Count > 0 && brackets.Peek() == Opener(c))
                    {
                        brackets.Pop();
                    }

                    break;
            }

            var (kind, length) = Operator(text, i);
            if (kind != TokenKind.End)
            {
                tokens.Add(new Token(kind, i));
            }

            i += length;
        }

        if (implicitClose)
        {
            while (brackets.Count > 0)
            {
                tokens.Add(new Token(Closer(brackets.Pop()), text.Length));
            }
        }

        tokens.Add(new Token(TokenKind.End, text.Length));
        return tokens;
    }

    public static bool IsIdentifierStart(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '_';

    public static bool IsIdentifierPart(char c) => IsIdentifierStart(c) || IsDigit(c);

    public static bool IsDigit(char c) => c is >= '0' and <= '9';

    public static char Opener(char closer) => closer switch
    {
        ')' => '(',
        ']' => '[',
        _ => '{',
    };

    private static TokenKind Closer(char opener) => opener switch
    {
        '(' => TokenKind.CloseParen,
        '[' => TokenKind.CloseBracket,
        _ => TokenKind.CloseBrace,
    };

    private static int SkipIdentifier(string text, int i)
    {
        while (i < text.Length && IsIdentifierPart(text[i]))
        {
            i++;
        }

        return i;
    }

    // -?[0-9]+(\.[0-9]+)?([eE][+-]?[0-9]+)?. An exponent marker with no digits ends the token and
    // is ignored, so "1e" is 1 (implementation-defined in 1.0, section 1.6).
    private static Token ReadNumber(string text, ref int i)
    {
        var start = i;
        if (text[i] == '-')
        {
            i++;
        }

        i = SkipDigits(text, i);
        if (i + 1 < text.Length && text[i] == '.' && IsDigit(text[i + 1]))
        {
            i = SkipDigits(text, i + 1);
        }

        var mantissaEnd = i;
        var hasExponent = false;
        if (i < text.Length && text[i] is 'e' or 'E')
        {
            i++;
            var signed = i < text.Length && text[i] is '+' or '-';
            if ((signed ? i + 1 : i) < text.Length && IsDigit(text[signed ? i + 1 : i]))
            {
                i = SkipDigits(text, signed ? i + 1 : i);
                hasExponent = true;
            }
        }

        var numberText = text[start..(hasExponent ? i : mantissaEnd)];
        var value = double.Parse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture);
        return new Token(TokenKind.Number, start, text[start..i], value);
    }

    private static int SkipDigits(string text, int i)
    {
        while (i < text.Length && IsDigit(text[i]))
        {
            i++;
        }

        return i;
    }

    // Double quotes take JSON escapes; single quotes take \' \\ \n \t. Any other escape keeps its
    // backslash. At the end of text under implicit closing a lone backslash is literal and a partial
    // \u escape is dropped (section 4, rule 2; section 1.6).
    private static Token ReadString(string text, ref int i, bool implicitClose)
    {
        var start = i;
        var quote = text[i++];
        var value = new StringBuilder();
        while (i < text.Length)
        {
            var c = text[i++];
            if (c == quote)
            {
                return new Token(TokenKind.String, start, value.ToString());
            }

            if (c != '\\')
            {
                value.Append(c);
                continue;
            }

            if (i >= text.Length)
            {
                value.Append('\\');
                break;
            }

            var escaped = text[i++];
            switch (escaped)
            {
                case 'n':
                    value.Append('\n');
                    break;
                case 't':
                    value.Append('\t');
                    break;
                case '\\':
                    value.Append('\\');
                    break;
                case '\'' when quote == '\'':
                    value.Append('\'');
                    break;
                case '"' when quote == '"':
                    value.Append('"');
                    break;
                case '/' when quote == '"':
                    value.Append('/');
                    break;
                case 'b' when quote == '"':
                    value.Append('\b');
                    break;
                case 'f' when quote == '"':
                    value.Append('\f');
                    break;
                case 'r' when quote == '"':
                    value.Append('\r');
                    break;
                case 'u' when quote == '"':
                    var digits = 0;
                    while (digits < 4 && i + digits < text.Length && char.IsAsciiHexDigit(text[i + digits]))
                    {
                        digits++;
                    }

                    if (digits == 4)
                    {
                        value.Append((char)int.Parse(text.AsSpan(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                    }
                    else if (!(implicitClose && i + digits == text.Length))
                    {
                        value.Append('\\').Append('u');
                    }
                    else
                    {
                        i += digits;
                    }

                    break;
                default:
                    value.Append('\\').Append(escaped);
                    break;
            }
        }

        return new Token(TokenKind.String, start, value.ToString());
    }

    private static (TokenKind Kind, int Length) Operator(string text, int i)
    {
        var c = text[i];
        var next = i + 1 < text.Length ? text[i + 1] : '\0';
        return c switch
        {
            '=' when next == '=' => (TokenKind.Equal, 2),
            '=' => (TokenKind.Assign, 1),
            '!' when next == '=' => (TokenKind.NotEqual, 2),
            '!' => (TokenKind.Bang, 1),
            '>' when next == '=' => (TokenKind.GreaterOrEqual, 2),
            '>' => (TokenKind.Greater, 1),
            '<' when next == '=' => (TokenKind.LessOrEqual, 2),
            '<' => (TokenKind.Less, 1),
            '&' => (TokenKind.AndAnd, next == '&' ? 2 : 1),
            '|' => (TokenKind.OrOr, next == '|' ? 2 : 1),
            '+' => (TokenKind.Plus, 1),
            '-' => (TokenKind.Minus, 1),
            '*' => (TokenKind.Star, 1),
            '/' => (TokenKind.Slash, 1),
            '%' => (TokenKind.Percent, 1),
            '?' => (TokenKind.Question, 1),
            ':' => (TokenKind.Colon, 1),
            '.' => (TokenKind.Dot, 1),
            ',' => (TokenKind.Comma, 1),
            '(' => (TokenKind.OpenParen, 1),
            ')' => (TokenKind.CloseParen, 1),
            '[' => (TokenKind.OpenBracket, 1),
            ']' => (TokenKind.CloseBracket, 1),
            '{' => (TokenKind.OpenBrace, 1),
            '}' => (TokenKind.CloseBrace, 1),
            _ => (TokenKind.End, 1),
        };
    }
}
