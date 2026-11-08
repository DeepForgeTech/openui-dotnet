using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor.Tests;

/// <summary>
/// A host's shared variables, as a page framework keeps them: it tells every listener about each change, synchronously,
/// including the change a listener made itself. It records each write the program makes through the binding.
/// </summary>
public sealed class FakeStateStore(params string[] keys) : IOpenUIStateBinding
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, OuiValue> _values = new(StringComparer.Ordinal);
    private readonly List<Action<string, OuiValue>> _listeners = [];
    private readonly List<(string Key, OuiValue Value)> _writes = [];
    private readonly List<(Func<FakeStateStore, bool> Condition, TaskCompletionSource Done)> _waits = [];

    public IReadOnlyCollection<string> Keys { get; } = keys;

    /// <summary>False for a store that does not tell its listeners about the binding's own writes.</summary>
    public bool Echoes { get; init; } = true;

    /// <summary>Every write the binding made, in order.</summary>
    public IReadOnlyList<(string Key, OuiValue Value)> Writes
    {
        get
        {
            lock (_gate)
            {
                return [.. _writes];
            }
        }
    }

    public int Listeners
    {
        get
        {
            lock (_gate)
            {
                return _listeners.Count;
            }
        }
    }

    public OuiValue Read(string key)
    {
        lock (_gate)
        {
            return _values.GetValueOrDefault(key);
        }
    }

    public IDisposable Subscribe(Action<string, OuiValue> changed)
    {
        lock (_gate)
        {
            _listeners.Add(changed);
        }

        return new Unsubscriber(() =>
        {
            lock (_gate)
            {
                _listeners.Remove(changed);
            }

            CheckWaits();
        });
    }

    public ValueTask WriteAsync(string key, OuiValue value)
    {
        lock (_gate)
        {
            _writes.Add((key, value));
            if (!Echoes)
            {
                _values[key] = value;
            }
        }

        if (Echoes)
        {
            Set(key, value);
        }
        else
        {
            CheckWaits();
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Changes a value from outside the program, as another widget would.</summary>
    public void Set(string key, OuiValue value)
    {
        Action<string, OuiValue>[] listeners;
        lock (_gate)
        {
            _values[key] = value;
            listeners = [.. _listeners];
        }

        foreach (var listener in listeners)
        {
            listener(key, value);
        }

        CheckWaits();
    }

    /// <summary>
    /// Completes once <paramref name="condition"/> holds, checked now and after every change to the store. A write lands after
    /// the render that caused it, so a wait that re-checks only on renders can miss it.
    /// </summary>
    public async Task WhenAsync(Func<FakeStateStore, bool> condition)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _waits.Add((condition, done));
        }

        CheckWaits();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private void CheckWaits()
    {
        (Func<FakeStateStore, bool> Condition, TaskCompletionSource Done)[] waits;
        lock (_gate)
        {
            waits = [.. _waits];
        }

        foreach (var wait in waits.Where(wait => wait.Condition(this)))
        {
            lock (_gate)
            {
                _waits.Remove(wait);
            }

            wait.Done.TrySetResult();
        }
    }

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
