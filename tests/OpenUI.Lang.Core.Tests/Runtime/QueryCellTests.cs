using OpenUI.Lang.Runtime;

namespace OpenUI.Lang.Core.Tests.Runtime;

public class QueryCellTests
{
    private sealed class CountingTimer : ITimer
    {
        public int Disposals { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose() => Disposals++;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public void A_new_cell_is_idle_and_asks_for_nothing()
    {
        var cell = new QueryCell();

        Assert.False(cell.IsBusy);
        Assert.False(cell.ToStatus().IsLoading);
        Assert.False(cell.Asked("t", "k"));
    }

    [Fact]
    public void A_fetch_that_was_stopped_cannot_finish_and_its_cancellation_goes_to_the_caller()
    {
        var cell = new QueryCell();
        using var first = new CancellationTokenSource();
        var fetch = cell.BeginFetch(first);
        Assert.True(cell.IsBusy);

        Assert.Same(first, cell.Stop());

        Assert.False(cell.IsBusy);
        Assert.False(cell.Finish(fetch));
    }

    [Fact]
    public void A_later_fetch_makes_the_earlier_one_stale()
    {
        var cell = new QueryCell();
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();
        var stale = cell.BeginFetch(first);
        var current = cell.BeginFetch(second);

        Assert.False(cell.Finish(stale));
        Assert.True(cell.IsBusy);
        Assert.True(cell.Finish(current));
        Assert.False(cell.IsBusy);
    }

    [Fact]
    public void A_finished_fetch_gives_back_its_cancellation()
    {
        var cell = new QueryCell();
        var source = new CancellationTokenSource();
        var fetch = cell.BeginFetch(source);

        Assert.True(cell.Finish(fetch));

        Assert.Throws<ObjectDisposedException>(() => source.Token.WaitHandle);
    }

    [Fact]
    public void A_cell_held_many_times_takes_one_place_in_the_queue()
    {
        var cell = new QueryCell();

        Assert.True(cell.Hold());
        Assert.False(cell.Hold());
        Assert.True(cell.IsBusy);

        Assert.True(cell.Unqueue());
        Assert.False(cell.IsBusy);
        Assert.True(cell.Hold());
    }

    [Fact]
    public void A_cell_stopped_while_queued_has_nothing_to_do_when_its_turn_comes()
    {
        var cell = new QueryCell();
        cell.Hold();

        Assert.Null(cell.Stop());
        Assert.False(cell.IsBusy);

        Assert.False(cell.Unqueue());
        Assert.True(cell.Hold());
    }

    [Fact]
    public void A_failure_forgets_the_arguments_so_the_next_parse_retries()
    {
        var cell = new QueryCell();
        cell.Ask("t", "k");
        Assert.True(cell.Asked("t", "k"));

        cell.Fail(new OpenUI.Lang.Diagnostics.OpenUIError { Source = OpenUI.Lang.Diagnostics.OpenUIErrorSource.Query, Code = OpenUI.Lang.Diagnostics.OpenUIErrorCode.RuntimeError, Message = "x" });

        Assert.False(cell.Asked("t", "k"));
        Assert.Single(cell.Errors);
    }

    [Fact]
    public void A_cell_takes_its_share_of_a_window_and_starts_over_in_the_next()
    {
        var cell = new QueryCell();

        Assert.True(cell.TakeFetch(1, 2));
        Assert.True(cell.TakeFetch(1, 2));
        Assert.False(cell.TakeFetch(1, 2));
        Assert.True(cell.TakeFetch(2, 2));
    }

    [Fact]
    public void The_same_refresh_interval_keeps_the_running_timer()
    {
        var cell = new QueryCell();
        var timer = new CountingTimer();
        var starts = 0;
        ITimer Start(TimeSpan _)
        {
            starts++;
            return timer;
        }

        cell.SetRefresh(TimeSpan.FromSeconds(1), Start);
        cell.SetRefresh(TimeSpan.FromSeconds(1), Start);
        Assert.Equal(1, starts);

        cell.SetRefresh(TimeSpan.FromSeconds(2), Start);
        Assert.Equal(2, starts);
        Assert.Equal(1, timer.Disposals);

        cell.SetRefresh(TimeSpan.Zero, Start);
        Assert.Equal(2, timer.Disposals);
        Assert.Equal(2, starts);
        Assert.False(cell.IsRefreshing);
    }

    [Fact]
    public void A_stopped_refresh_starts_again_at_the_same_interval()
    {
        var cell = new QueryCell();
        var starts = 0;
        ITimer Start(TimeSpan _)
        {
            starts++;
            return new CountingTimer();
        }

        cell.SetRefresh(TimeSpan.FromSeconds(1), Start);
        cell.StopRefresh();
        cell.SetRefresh(TimeSpan.FromSeconds(1), Start);

        Assert.Equal(2, starts);
    }
}
