using OpenUI.Lang.Runtime;

namespace OpenUI.Lang.Blazor;

/// <summary>
/// Feeds a session from program text that grows as it is written, the way a chat app holds a reply.
/// Text that extends what was fed goes to the session as a chunk; any other text starts a new stream.
/// The session keeps its state across both, so a rewrite never resets what the user picked.
/// </summary>
/// <param name="session">The session to feed.</param>
public sealed class ProgramFeed(OpenUISession session)
{
    private string _text = "";
    private bool _streaming;

    /// <summary>Takes the program text as it stands now.</summary>
    /// <param name="text">The whole text so far.</param>
    /// <param name="streaming">True while more may come: the text parses as a stream, and queries wait for it to end (language.md §4).</param>
    public void Set(string text, bool streaming)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text == _text && streaming == _streaming)
        {
            return;
        }

        if (_streaming && text.StartsWith(_text, StringComparison.Ordinal))
        {
            if (text.Length > _text.Length)
            {
                session.Append(text.AsSpan(_text.Length));
            }
        }
        else
        {
            session.BeginStream();
            session.Append(text);
        }

        _text = text;
        _streaming = streaming;
        if (!streaming)
        {
            session.EndStream();
        }
    }
}
