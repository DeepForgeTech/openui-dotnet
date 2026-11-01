using System.Text;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Hostile;

public partial class HostileInputTests
{
    [Fact]
    public async Task Ending_a_stream_with_many_queries_is_linear()
    {
        var program = new StringBuilder("root = Stack([Text(\"p\")])\n");
        for (var i = 0; i < 9_990; i++)
        {
            program.Append('q').Append(i).Append(" = Query(\"nope\", {})\n");
        }

        await HostileProbe.SurvivesAsync("session", program.ToString(), TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task Tool_calls_queue_behind_the_concurrency_limit_and_all_finish()
    {
        var running = 0;
        var peak = 0;
        var finished = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tools = new OpenUITools().Add("t", OpenUIToolEffect.Read, async (_, ct) =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref peak, now);
            await release.Task.WaitAsync(ct);
            Interlocked.Decrement(ref running);
            Interlocked.Increment(ref finished);
            return OuiValue.Null;
        });
        var program = new StringBuilder("root = Stack([])\n");
        for (var i = 0; i < 50; i++)
        {
            program.Append('q').Append(i).Append(" = Query(\"t\", {})\n");
        }

        var clock = new Runtime.ManualClock();
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools, Time = clock });
        session.Load(program.ToString());
        await Task.Delay(500);
        Assert.True(Volatile.Read(ref peak) <= OpenUILimits.Default.MaxConcurrentToolCalls, $"Peak {peak}.");

        release.SetResult();
        await session.WhenIdleAsync().WaitAsync(Budget);

        // The ones past the session's fetches a second start in the next second.
        clock.Advance(TimeSpan.FromSeconds(1));
        await session.WhenIdleAsync().WaitAsync(Budget);
        Assert.Equal(50, Volatile.Read(ref finished));
        Assert.True(Volatile.Read(ref peak) <= OpenUILimits.Default.MaxConcurrentToolCalls, $"Peak {peak}.");
    }

    [Fact]
    public async Task Fetches_of_one_query_are_held_to_its_rate_and_the_last_one_still_runs()
    {
        var clock = new Runtime.ManualClock();
        var calls = new List<double>();
        var tools = new OpenUITools().Add("t", OpenUIToolEffect.Read, (args, _) =>
        {
            Assert.True(args.TryGetObject(out var fields));
            lock (calls)
            {
                calls.Add(OpenUI.Lang.Values.Ecma.ToNumber(fields["n"]));
            }

            return ValueTask.FromResult(OuiValue.Null);
        });
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools, Time = clock });
        session.Load("$n = 0\nq = Query(\"t\", {n: $n})\nroot = Stack([])\n");
        for (var i = 1; i <= 20; i++)
        {
            session.State.Set("$n", (double)i);
        }

        await session.WhenIdleAsync().WaitAsync(Budget);
        Assert.True(calls.Count <= OpenUILimits.Default.MaxFetchesPerSecond, $"{calls.Count} calls.");

        clock.Advance(TimeSpan.FromSeconds(2));
        await session.WhenIdleAsync().WaitAsync(Budget);
        Assert.Equal(20, calls[^1]);
    }

    [Fact]
    public async Task Fetches_of_one_query_that_each_finish_are_still_held_to_its_rate()
    {
        var clock = new Runtime.ManualClock();
        var (tools, calls) = CountingTools();
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools, Time = clock });
        session.Load("$n = 0\nq = Query(\"t\", {n: $n})\nroot = Stack([])\n");
        for (var i = 1; i <= 20; i++)
        {
            await session.WhenIdleAsync().WaitAsync(Budget);
            session.State.Set("$n", (double)i);
        }

        await session.WhenIdleAsync().WaitAsync(Budget);
        Assert.Equal(OpenUILimits.Default.MaxFetchesPerSecond, calls());

        clock.Advance(TimeSpan.FromSeconds(1));
        await session.WhenIdleAsync().WaitAsync(Budget);
        Assert.Equal(OpenUILimits.Default.MaxFetchesPerSecond + 1, calls());
    }

    private static (OpenUITools Tools, Func<int> Calls) CountingTools()
    {
        var calls = 0;
        var tools = new OpenUITools().Add("t", OpenUIToolEffect.Read, (_, _) =>
        {
            Interlocked.Increment(ref calls);
            return ValueTask.FromResult(OuiValue.Null);
        });
        return (tools, () => Volatile.Read(ref calls));
    }

    private static string Queries(int count, string extra = "") =>
        string.Concat(Enumerable.Range(0, count).Select(i => $"q{i} = Query(\"t\", {{n: {i}}}, {{}}{extra})\n")) + "root = Stack([])\n";

    [Fact]
    public async Task A_session_starts_no_more_fetches_a_second_than_its_limit_and_gets_to_all_of_them()
    {
        var clock = new Runtime.ManualClock();
        var (tools, calls) = CountingTools();
        var limit = OpenUILimits.Default.MaxSessionFetchesPerSecond;
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools, Time = clock });

        session.Load(Queries((2 * limit) + 5));
        await session.WhenIdleAsync().WaitAsync(Budget);
        Assert.Equal(limit, calls());
        Assert.False(session.GetQueryStatus("q0").IsLoading);
        Assert.True(session.GetQueryStatus("q" + limit).IsLoading);

        clock.Advance(TimeSpan.FromSeconds(1));
        await session.WhenIdleAsync().WaitAsync(Budget);
        Assert.Equal(2 * limit, calls());

        clock.Advance(TimeSpan.FromSeconds(1));
        await session.WhenIdleAsync().WaitAsync(Budget);
        Assert.Equal((2 * limit) + 5, calls());
        Assert.False(session.GetQueryStatus("q" + ((2 * limit) + 4)).IsLoading);
    }

    [Fact]
    public async Task Queries_past_the_refresh_limit_fetch_once_and_report_it()
    {
        var clock = new Runtime.ManualClock();
        var (tools, calls) = CountingTools();
        var limits = OpenUILimits.Default with { MaxRefreshingQueries = 3 };
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools, Time = clock, Limits = limits });

        session.Load(Queries(10, ", 5"));
        await session.WhenIdleAsync().WaitAsync(Budget);
        Assert.Equal(10, calls());
        Assert.Contains(session.Current.Errors, error => error.Message.StartsWith("More than 3 queries refresh", StringComparison.Ordinal));

        clock.Advance(TimeSpan.FromSeconds(5));
        await session.WhenIdleAsync().WaitAsync(Budget);
        Assert.Equal(13, calls());

        clock.Advance(TimeSpan.FromSeconds(5));
        await session.WhenIdleAsync().WaitAsync(Budget);
        Assert.Equal(16, calls());
    }

    [Fact]
    public async Task A_refresh_slot_comes_free_when_its_query_goes()
    {
        var clock = new Runtime.ManualClock();
        var (tools, calls) = CountingTools();
        var limits = OpenUILimits.Default with { MaxRefreshingQueries = 1 };
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools, Time = clock, Limits = limits });

        session.Load("a = Query(\"t\", {}, {}, 5)\nroot = Stack([])\n");
        await session.WhenIdleAsync().WaitAsync(Budget);
        session.Load("b = Query(\"t\", {}, {}, 5)\nroot = Stack([])\n");
        await session.WhenIdleAsync().WaitAsync(Budget);
        var before = calls();

        clock.Advance(TimeSpan.FromSeconds(5));
        await session.WhenIdleAsync().WaitAsync(Budget);

        Assert.Equal(before + 1, calls());
        Assert.DoesNotContain(session.Current.Errors, error => error.Message.StartsWith("More than", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_refresh_timer_fires_once_and_is_set_again_from_its_tick()
    {
        var clock = new PeriodRecorder();
        var (tools, _) = CountingTools();
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools, Time = clock });

        session.Load("q = Query(\"t\", {}, {}, 5)\nroot = Stack([])\n");
        await session.WhenIdleAsync().WaitAsync(Budget);

        Assert.Equal(Timeout.InfiniteTimeSpan, clock.LastPeriod);
    }

    [Fact]
    public async Task A_stall_past_the_end_of_the_window_does_not_hand_the_timer_a_negative_wait()
    {
        var clock = new SteppingTime(TimeSpan.FromMilliseconds(600));
        var (tools, _) = CountingTools();
        var limits = OpenUILimits.Default with { MaxSessionFetchesPerSecond = 1 };
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools, Time = clock, Limits = limits });

        session.Load("a = Query(\"t\", {})\nb = Query(\"t\", {})\nroot = Stack([])\n");

        Assert.True(clock.Waits.Count > 0, "No fetch was held, so the timer was never asked.");
        Assert.All(clock.Waits, wait => Assert.True(wait >= TimeSpan.Zero, $"Asked for {wait}."));
    }

    // Every reading of the clock is later than the one before, as after a stall. A negative wait
    // throws, as it does from the system's own timers.
    private sealed class SteppingTime(TimeSpan step) : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public List<TimeSpan> Waits { get; } = [];

        public override DateTimeOffset GetUtcNow() => _now += step;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Waits.Add(dueTime);
            ArgumentOutOfRangeException.ThrowIfLessThan(dueTime, TimeSpan.Zero);
            return new Timer(_ => { }, null, Timeout.Infinite, Timeout.Infinite);
        }
    }

    private sealed class PeriodRecorder : TimeProvider
    {
        public TimeSpan LastPeriod { get; private set; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            LastPeriod = period;
            return new Timer(_ => { }, null, Timeout.Infinite, Timeout.Infinite);
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
