namespace OpenUI.Lang.Runtime;

/// <summary>The one reading of a URL's scheme and host, for the <c>url</c> rule (language.md, section 5.4) and <c>@OpenUrl</c>.</summary>
internal static class UrlText
{
    /// <summary>
    /// The scheme of <paramref name="text"/>, or null if it has none. Strictly the grammar of a scheme, with no
    /// space or control character anywhere in the text, since a browser ignores those and they could
    /// otherwise carry "javascript:" past a plain comparison.
    /// </summary>
    public static string? Scheme(string text)
    {
        var colon = text.IndexOf(':');
        if (colon <= 0 || !char.IsAsciiLetter(text[0]) || text.AsSpan().ContainsAnyInRange('\0', ' '))
        {
            return null;
        }

        foreach (var c in text.AsSpan(0, colon))
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('+' or '-' or '.'))
            {
                return null;
            }
        }

        return text[..colon];
    }

    /// <summary>An absolute URL has a scheme, then <c>//</c>, then a host that is not empty (section 5.4).</summary>
    public static bool IsAbsolute(string text)
    {
        if (Scheme(text) is not { } scheme || !text.AsSpan(scheme.Length).StartsWith("://"))
        {
            return false;
        }

        var authority = text.AsSpan(scheme.Length + 3);
        var end = authority.IndexOfAny('/', '?', '#');
        authority = end < 0 ? authority : authority[..end];
        authority = authority[(authority.LastIndexOf('@') + 1)..];
        var port = authority.LastIndexOf(':');
        return !(port < 0 ? authority : authority[..port]).IsEmpty;
    }
}
