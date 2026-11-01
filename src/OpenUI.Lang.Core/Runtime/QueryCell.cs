using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Runtime;

/// <summary>
/// What one query statement holds and what it is doing (language.md, section 6.1). A cell is idle,
/// fetching, or waiting out its rate limit, and only one of those. The session's lock guards it.
/// </summary>
internal sealed class QueryCell
{
    private Activity _activity = new Idle();
    private string? _tool;
    private string? _argsKey;
    private ITimer? _refreshTimer;
    private TimeSpan _refreshInterval;
    private long _window;
    private int _windowFetches;
    private bool _queued;

    public OuiValue Data { get; private set; }

    public bool HasData { get; private set; }

    public OpenUIErrorList Errors { get; private set; } = OpenUIErrorList.Empty;

    /// <summary>Whether a tick has to leave this cell alone.</summary>
    public bool IsBusy => _activity is not Idle;

    public bool IsRefreshing => _refreshTimer is not null;

    public QueryStatus ToStatus() => new(Data, IsBusy && !HasData, IsBusy && HasData, Errors);

    /// <summary>Whether this is what the cell last asked for. Forgotten arguments are never that, so the next parse retries.</summary>
    public bool Asked(string tool, string argsKey) => _tool == tool && _argsKey == argsKey;

    public void Ask(string tool, string argsKey)
    {
        _tool = tool;
        _argsKey = argsKey;
    }

    public void ForgetArguments() => _argsKey = null;

    public void ClearErrors() => Errors = OpenUIErrorList.Empty;

    public void Succeed(OuiValue data)
    {
        Data = data;
        HasData = true;
        Errors = OpenUIErrorList.Empty;
    }

    /// <summary>Records a failure; without its arguments the next parse retries, so it cannot disappear with the errors a new stream clears (section 8.3).</summary>
    public void Fail(OpenUIError error)
    {
        ForgetArguments();
        Errors = OpenUIErrorList.Create([error]);
    }

    /// <summary>Goes idle. Returns a running fetch's cancellation, which the caller cancels once the lock is released, since tool code can run inside Cancel.</summary>
    public CancellationTokenSource? Stop()
    {
        var running = (_activity as Fetching)?.Cancellation;
        _activity = new Idle();
        return running;
    }

    public Fetching BeginFetch(CancellationTokenSource cancellation)
    {
        var fetch = new Fetching(cancellation);
        _activity = fetch;
        return fetch;
    }

    /// <summary>Goes idle if <paramref name="fetch"/> is still what the cell is doing; if not, its result is stale (section 6.1).</summary>
    public bool Finish(Fetching fetch)
    {
        if (!ReferenceEquals(_activity, fetch))
        {
            return false;
        }

        _activity = new Idle();
        fetch.Cancellation.Dispose();
        return true;
    }

    /// <summary>Waits for a fetch. Answers whether the cell needs a place in the queue: one place is enough however often it is held.</summary>
    public bool Hold()
    {
        _activity = new Held();
        var needsPlace = !_queued;
        _queued = true;
        return needsPlace;
    }

    /// <summary>Leaves the queue. Answers whether the cell was still waiting, and if so it goes idle.</summary>
    public bool Unqueue()
    {
        _queued = false;
        if (_activity is not Held)
        {
            return false;
        }

        _activity = new Idle();
        return true;
    }

    /// <summary>Takes one of the fetches this query may start in <paramref name="window"/>; false if none are left.</summary>
    public bool TakeFetch(long window, int fetchesPerWindow)
    {
        if (window != _window)
        {
            _window = window;
            _windowFetches = 0;
        }

        if (_windowFetches >= fetchesPerWindow)
        {
            return false;
        }

        _windowFetches++;
        return true;
    }

    /// <summary>Starts refreshing at <paramref name="interval"/>, zero for never; the same interval leaves the running timer alone.</summary>
    public void SetRefresh(TimeSpan interval, Func<TimeSpan, ITimer> start)
    {
        if (interval == _refreshInterval)
        {
            return;
        }

        StopRefresh();
        _refreshInterval = interval;
        if (interval > TimeSpan.Zero)
        {
            _refreshTimer = start(interval);
        }
    }

    /// <summary>Sets the next tick. A timer fires once and is set again from its own tick, so ticks cannot pile up behind a busy session.</summary>
    public void RearmRefresh() => _refreshTimer?.Change(_refreshInterval, Timeout.InfiniteTimeSpan);

    public void StopRefresh()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        _refreshInterval = TimeSpan.Zero;
    }

    internal abstract record Activity;

    internal sealed record Idle : Activity;

    /// <summary>A fetch under way. The instance is its identity: a later fetch is a different one.</summary>
    internal sealed record Fetching(CancellationTokenSource Cancellation) : Activity;

    internal sealed record Held : Activity;
}
