using System.Collections.Immutable;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Runtime;

/// <summary>Reads and writes the state store (language.md, section 5.1). Keys include their <c>$</c>.</summary>
public sealed class StateAccessor : IStateWriter
{
    private readonly OpenUISession _session;

    internal StateAccessor(OpenUISession session) => _session = session;

    /// <summary>The current value, or the default when nothing has written it (section 5.1).</summary>
    public OuiValue Get(string key) => Snapshot().GetValueOrDefault(Checked(key));

    /// <summary>Writes a value as a user would through a binding (section 5.2). Defaults never overwrite it.</summary>
    public void Set(string key, OuiValue value) => _session.WriteState(new StateKey(Checked(key)), value);

    /// <inheritdoc/>
    public void Write(StateKey key, OuiValue value) => Set(key.Name, value);

    /// <summary>Restores the declared defaults, null if none (section 6.3).</summary>
    public void Reset(params ReadOnlySpan<string> keys)
    {
        var targets = ImmutableArray.CreateBuilder<StateKey>(keys.Length);
        foreach (var key in keys)
        {
            targets.Add(new StateKey(Checked(key)));
        }

        _session.ResetState(targets.MoveToImmutable());
    }

    /// <summary>Every variable with its current value, declared defaults and undeclared names included (section 5.1).</summary>
    public ImmutableDictionary<string, OuiValue> Snapshot() => _session.StateSnapshot();

    private static string Checked(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.StartsWith('$') ? key : throw new ArgumentException($"State keys start with $; got \"{key}\".", nameof(key));
    }
}
