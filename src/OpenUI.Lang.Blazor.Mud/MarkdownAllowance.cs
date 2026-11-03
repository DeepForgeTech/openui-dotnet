using System.Runtime.CompilerServices;

namespace OpenUI.Lang.Blazor.Mud;

/// <summary>
/// What a session may spend on markdown. <see cref="Markdown.MaxChars"/> limits one text, and a program can repeat a text
/// of that size many times, each repeat costing it a few characters and the server a parse, so the session has an allowance
/// of the characters drawn at once. Each piece of markdown holds what it drew, and drawing it again replaces what it holds,
/// so a stream that grows a text does not use it up. A stream can also change a costly text on every chunk, which the
/// characters at once do not bound, so the session has a <see cref="ParseBudget"/> of time too. Text that comes when either
/// is gone shows as plain text.
/// </summary>
internal sealed class MarkdownAllowance
{
    /// <summary>
    /// The characters of a session, as many as <c>OpenUIProgram</c> lets its values hold. The time is what bounds the cost: the
    /// worst text for the parser takes about 0.5 s to 0.8 s at <see cref="Markdown.MaxChars"/> in a Release build, and ordinary
    /// markdown a few milliseconds. It is also some 40,000 words of markdown drawn at once, more than any page holds.
    /// </summary>
    public const int Total = 256 * 1024;

    private static readonly ConditionalWeakTable<OpenUIContext, MarkdownAllowance> Sessions = [];

    private readonly Dictionary<object, Held> _held = [];
    private readonly SortedDictionary<long, Held> _cut = [];
    private int _used;
    private long _arrivals;

    /// <summary>The time the session may spend parsing.</summary>
    public ParseBudget Time { get; } = new();

    /// <summary>The allowance of a view's session.</summary>
    public static MarkdownAllowance Of(OpenUIContext context) => Sessions.GetOrCreateValue(context);

    /// <summary>Whether the piece may be drawn as markdown, in place of what it held.</summary>
    /// <param name="piece">What draws the text.</param>
    /// <param name="chars">The characters of the text.</param>
    /// <param name="redraw">Draws the piece again, for when other pieces have given the allowance back.</param>
    public bool Fit(object piece, int chars, Action redraw)
    {
        Return(piece);
        var fits = chars <= Total - _used;
        var held = new Held(chars, fits, redraw, fits ? 0 : ++_arrivals);
        _held[piece] = held;
        if (fits)
        {
            _used += chars;
        }
        else
        {
            _cut[held.Arrival] = held;
        }

        return fits;
    }

    /// <summary>Gives back what the piece held, and tells the pieces that were cut and now fit, the first to arrive first.</summary>
    public void Release(object piece)
    {
        Return(piece);
        var free = Total - _used;
        var fitting = new List<Action>();
        foreach (var held in _cut.Values)
        {
            if (free == 0)
            {
                break;
            }

            if (held.Chars <= free)
            {
                free -= held.Chars;
                fitting.Add(held.Redraw);
            }
        }

        foreach (var redraw in fitting)
        {
            redraw();
        }
    }

    private void Return(object piece)
    {
        if (!_held.Remove(piece, out var held))
        {
            return;
        }

        if (held.Drawn)
        {
            _used -= held.Chars;
        }
        else
        {
            _cut.Remove(held.Arrival);
        }
    }

    private readonly record struct Held(int Chars, bool Drawn, Action Redraw, long Arrival);
}
