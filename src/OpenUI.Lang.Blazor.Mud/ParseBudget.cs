namespace OpenUI.Lang.Blazor.Mud;

/// <summary>
/// The time a session may spend parsing markdown that is not in the cache. A stream can change a costly text on every
/// chunk, so that a bound on what is drawn at once does not bound the work. Ordinary parsing takes milliseconds, so a page
/// of it is nowhere near the budget. Time comes back at a second in every minute, up to the whole budget: a session that
/// lives for hours, such as a dashboard, recovers, and what a stream can cost stays bounded, by the budget and then a sixtieth
/// of a core. A parse that has begun runs to its end, so a session may spend one parse more than the budget.
/// </summary>
/// <param name="total">The time to spend.</param>
/// <param name="clock">The clock the time comes back by.</param>
internal sealed class ParseBudget(TimeSpan total, TimeProvider clock)
{
    private const int SecondsPerRefilledSecond = 60;

    private TimeSpan _spent;
    private long _refilled = clock.GetTimestamp();

    public ParseBudget()
        : this(TimeSpan.FromSeconds(5), TimeProvider.System)
    {
    }

    /// <summary>True when there is no time left to parse with.</summary>
    public bool IsSpent
    {
        get
        {
            Refill();
            return _spent >= total;
        }
    }

    /// <summary>Counts the time a parse took.</summary>
    /// <param name="time">The time.</param>
    public void Spend(TimeSpan time)
    {
        Refill();
        _spent += time;
    }

    private void Refill()
    {
        var now = clock.GetTimestamp();
        _spent = TimeSpan.FromTicks(Math.Max(0, _spent.Ticks - clock.GetElapsedTime(_refilled, now).Ticks / SecondsPerRefilledSecond));
        _refilled = now;
    }
}
