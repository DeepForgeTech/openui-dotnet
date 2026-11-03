using OpenUI.Lang.Blazor;

namespace OpenUI.Lang.Samples.BlazorApp.Playback;

/// <summary>
/// A player's behaviour, apart from how it looks: while playing, it writes the next of its
/// <c>values</c> to its bound <c>value</c> every <c>seconds</c>, and wraps at the end when <c>loop</c>
/// is on. It needs nothing from MudBlazor, so a view in any component set can inherit it.
/// </summary>
public abstract class PlayerBase : OpenUIComponent, IDisposable
{
    /// <summary>The shortest step, so a program can't ask for a write on every frame.</summary>
    public static readonly TimeSpan MinimumStep = TimeSpan.FromMilliseconds(100);

    private CancellationTokenSource? _playing;
    private PeriodicTimer? _timer;

    /// <summary>True while the player is stepping.</summary>
    protected bool IsPlaying => _playing is not null;

    /// <summary>
    /// The time between steps: <c>seconds</c>, one second when it is missing, never less than <see cref="MinimumStep"/>.
    /// It may come as a number, as text such as a Select writes, or as a one-item list such as a Slider writes.
    /// </summary>
    public TimeSpan Interval =>
        Value("seconds") is var given && RuntimeValues.AsNumber(given.TryGetArray(out var items) && items.Count == 1 ? items[0] : given) is { } seconds
        && double.IsFinite(seconds)
            ? TimeSpan.FromSeconds(Math.Max(seconds, MinimumStep.TotalSeconds))
            : TimeSpan.FromSeconds(1);

    /// <summary>A change to <c>seconds</c> while playing retimes the player at once: the next step comes one new interval later.</summary>
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (_timer is { } timer && timer.Period != Interval)
        {
            timer.Period = Interval;
        }
    }

    /// <summary>Whether the player starts again from the first value after the last. On unless the program says false.</summary>
    protected bool Loops => Value("loop") is var loop && (loop.IsNull || Flag("loop"));

    /// <summary>Starts or stops the player.</summary>
    protected void Toggle()
    {
        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    /// <summary>Moves one value on, as a tick would.</summary>
    protected void StepOnce()
    {
        if (PlaybackSteps.Next(Input("value"), Items("values"), Loops) is { } next)
        {
            Write("value", next);
        }
    }

    /// <summary>Stops the player where it is.</summary>
    protected void Pause()
    {
        _playing?.Cancel();
        _playing?.Dispose();
        _playing = null;
        _timer = null;
    }

    /// <inheritdoc />
    public void Dispose() => Pause();

    private void Play()
    {
        // Pressing play on the last value of a player that doesn't loop plays it again from the start.
        if (!Loops && PlaybackSteps.AtEnd(Input("value"), Items("values")))
        {
            Write("value", Items("values")[0]);
        }

        var playing = new CancellationTokenSource();
        _playing = playing;
        _ = RunAsync(playing.Token);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        // Made and dropped on the renderer's thread, which is also where a new interval arrives.
        using var timer = new PeriodicTimer(Interval);
        _timer = timer;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await InvokeAsync(() =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    if (PlaybackSteps.Next(Input("value"), Items("values"), Loops) is { } next)
                    {
                        Write("value", next);
                    }
                    else
                    {
                        Pause();
                    }

                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Paused, or the program went away.
        }
    }
}
