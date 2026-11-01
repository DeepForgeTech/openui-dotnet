namespace OpenUI.Lang.Core.Tests.Runtime;

/// <summary>A clock that moves only when told to and fires what is due, on the calling thread.</summary>
internal sealed class ManualClock : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_timers)
        {
            return _now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_timers)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    public void Advance(TimeSpan by)
    {
        DateTimeOffset target;
        lock (_timers)
        {
            target = _now + by;
        }

        while (true)
        {
            ManualTimer? next;
            lock (_timers)
            {
                next = _timers.Where(t => t.Due is { } due && due <= target).MinBy(t => t.Due);
                if (next is null)
                {
                    _now = target;
                    return;
                }

                _now = next.Due!.Value;
                next.Advanced();
            }

            next.Fire();
        }
    }

    private void Remove(ManualTimer timer)
    {
        lock (_timers)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period;

        public DateTimeOffset? Due { get; private set; }

        public void Advanced() => Due = _period > TimeSpan.Zero && _period != Timeout.InfiniteTimeSpan ? Due + _period : null;

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _period = period;
            Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.GetUtcNow() + dueTime;
            return true;
        }

        public void Dispose() => clock.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
