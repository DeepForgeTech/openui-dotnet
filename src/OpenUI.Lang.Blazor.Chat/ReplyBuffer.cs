using System.Diagnostics;
using System.Text;

namespace OpenUI.Lang.Blazor.Chat;

/// <summary>
/// Collects a streaming reply and copies it to the entry at most once per <see cref="Interval"/>, with one more when it ends,
/// so a reply in many small pieces costs neither a new string nor a render for each piece. A piece that arrives alone is
/// still shown once the interval is up.
/// </summary>
internal sealed class ReplyBuffer(OpenUIChatEntry reply, Action published)
{
    /// <summary>How often the entry's text may change while the reply streams.</summary>
    internal static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);

    private readonly Lock _gate = new();
    private readonly StringBuilder _text = new(reply.Text);
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private long _publishedAt = Stopwatch.GetTimestamp();
    private bool _pending;
    private bool _scheduled;
    private bool _done;

    /// <summary>The length of the reply so far, shown or not.</summary>
    public int Length
    {
        get
        {
            lock (_gate)
            {
                return _text.Length;
            }
        }
    }

    /// <summary>Adds a piece.</summary>
    public void Append(string piece)
    {
        lock (_gate)
        {
            _text.Append(piece);
            _pending = true;
        }

        TryPublish();
    }

    /// <summary>Copies what is left to the entry. Later pieces and timers do nothing.</summary>
    public void Complete()
    {
        lock (_gate)
        {
            _done = true;
            if (_pending)
            {
                reply.Text = _text.ToString();
                _pending = false;
            }
        }
    }

    // Publishes now if the last was an interval ago, or else once, when it is up.
    private void TryPublish()
    {
        TimeSpan wait;
        lock (_gate)
        {
            if (_done || !_pending || _scheduled)
            {
                return;
            }

            wait = Interval - Stopwatch.GetElapsedTime(_publishedAt);
            if (wait <= TimeSpan.Zero)
            {
                reply.Text = _text.ToString();
                _pending = false;
                _publishedAt = Stopwatch.GetTimestamp();
            }
            else
            {
                _scheduled = true;
            }
        }

        if (wait <= TimeSpan.Zero)
        {
            published();
        }
        else
        {
            _ = Task.Delay(wait).ContinueWith(_ => OnTimer(), TaskScheduler.Default);
        }
    }

    // On the context the reply started on, such as a component's renderer, when it had one.
    private void OnTimer()
    {
        void Run()
        {
            lock (_gate)
            {
                _scheduled = false;
            }

            TryPublish();
        }

        if (_context is null)
        {
            Run();
        }
        else
        {
            _context.Post(_ => Run(), null);
        }
    }
}
