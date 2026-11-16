using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor;

/// <summary>
/// One session following one <see cref="IOpenUIStateBinding"/> in both directions, from when it is made until it is disposed.
/// It remembers the last value each side agreed on, so a change that comes back from the other side is not passed on again.
/// </summary>
internal sealed class StateBindingLink : IDisposable
{
    private readonly OpenUISession _session;
    private readonly IOpenUIStateBinding _binding;
    private readonly HashSet<string> _keys;
    private readonly Func<Func<Task>, Task> _onRenderer;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, OuiValue> _synced = new(StringComparer.Ordinal);
    private readonly IDisposable _subscription;
    private int _writeQueued;
    private volatile bool _disposed;

    // Subscribes before it reads, so a value the store took after the session was made (a sibling filling it during its
    // own start, say) is heard or read here, whichever comes first, and never lost between the two.
    private StateBindingLink(OpenUISession session, IOpenUIStateBinding binding, Func<Func<Task>, Task> onRenderer)
    {
        _session = session;
        _binding = binding;
        _keys = new HashSet<string>(binding.Keys, StringComparer.Ordinal);
        _onRenderer = onRenderer;
        _session.Invalidated += OnInvalidated;
        _subscription = binding.Subscribe(FromStore);
        foreach (var key in _keys)
        {
            if (binding.Read(key) is { IsNull: false } value)
            {
                FromStore(key, value);
            }
        }

        QueueWriteBack();
    }

    /// <summary>The link for this session and binding: <paramref name="current"/> when it already is one, else a new one, with the old one disposed.</summary>
    /// <param name="current">The link so far, or null.</param>
    /// <param name="session">The session to follow the store.</param>
    /// <param name="binding">The store, or null for none.</param>
    /// <param name="onRenderer">Runs work on the renderer, reporting what it throws.</param>
    public static StateBindingLink? Follow(StateBindingLink? current, OpenUISession session, IOpenUIStateBinding? binding, Func<Func<Task>, Task> onRenderer)
    {
        if (current is not null && ReferenceEquals(current._session, session) && ReferenceEquals(current._binding, binding))
        {
            return current;
        }

        current?.Dispose();
        return binding is null ? null : new StateBindingLink(session, binding, onRenderer);
    }

    public void Dispose()
    {
        _disposed = true;
        _session.Invalidated -= OnInvalidated;
        _subscription.Dispose();
    }

    private void FromStore(string key, OuiValue value)
    {
        if (_disposed || !_keys.Contains(key))
        {
            return;
        }

        // The store can change on any thread. Taking its value and putting it in the session as one step keeps a write-back
        // from reading the session in between, seeing the old value differ from the new one, and writing the old one back.
        lock (_gate)
        {
            if (_synced.TryGetValue(key, out var last) && last.Equals(value))
            {
                return;
            }

            _synced[key] = value;
            try
            {
                // Set even when the value equals what the program shows now: that may be a default, which a later stream or
                // edit can change, and only a set pins the store's value against it.
                _session.State.Set(key, value);
            }
            catch (ObjectDisposedException)
            {
                // The session went away first; the view disposes this link next.
            }
        }
    }

    private void OnInvalidated(object? sender, SessionInvalidatedEventArgs e) => QueueWriteBack();

    // The session raises Invalidated while it is still notifying, which can be in the middle of a render. Writing the store
    // from there lets the store's own listeners re-enter rendering, so the write waits its turn on the renderer.
    private void QueueWriteBack()
    {
        if (Interlocked.Exchange(ref _writeQueued, 1) == 0)
        {
            _ = _onRenderer(async () =>
            {
                await Task.Yield();
                Volatile.Write(ref _writeQueued, 0);
                await WriteBackAsync();
            });
        }
    }

    private async Task WriteBackAsync()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var (key, value) in TakeChanges())
        {
            await _binding.WriteAsync(key, value);
        }
    }

    // The variables the program changed since the two sides last agreed, now counted as agreed.
    private List<(string Key, OuiValue Value)> TakeChanges()
    {
        var changes = new List<(string Key, OuiValue Value)>();
        lock (_gate)
        {
            IReadOnlyDictionary<string, OuiValue> state;
            try
            {
                // A stream's defaults can still change until it ends, which invalidates again.
                if (_session.Current.IsStreaming)
                {
                    return changes;
                }

                state = _session.State.Snapshot();
            }
            catch (ObjectDisposedException)
            {
                return changes;
            }

            foreach (var key in _keys)
            {
                var value = state.GetValueOrDefault(key);
                var unchanged = _synced.TryGetValue(key, out var last) ? last.Equals(value) : value.IsNull;
                if (!unchanged)
                {
                    _synced[key] = value;
                    changes.Add((key, value));
                }
            }
        }

        return changes;
    }
}
