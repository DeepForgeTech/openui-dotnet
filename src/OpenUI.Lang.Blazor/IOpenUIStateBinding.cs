using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor;

/// <summary>
/// Keeps some of a program's <c>$variables</c> equal to values in a store outside it, such as a page's shared variables.
/// The host says how to read, hear and write its store; the renderer does the rest. It reads the store before the program
/// first evaluates, follows the store's changes, writes each variable the program changes back once, ignores a change that
/// only echoes what it last saw (values compare by structure, so lists and objects too), and writes back on the renderer
/// after the session has finished notifying, never from inside its <c>Invalidated</c> event. Values cross as they are:
/// a list is an <see cref="OuiArray"/> both ways.
/// </summary>
/// <remarks>Give one to <see cref="OpenUIProgram.StateBinding"/>, or to <see cref="OpenUIView.StateBinding"/> for a session the host owns.</remarks>
public interface IOpenUIStateBinding
{
    /// <summary>The <c>$variables</c> it binds, each with its <c>$</c>.</summary>
    IReadOnlyCollection<string> Keys { get; }

    /// <summary>The store's value for one of <see cref="Keys"/>, or <see cref="OuiValue.Null"/> when it has none, which leaves the program's default in place.</summary>
    /// <param name="key">One of <see cref="Keys"/>.</param>
    OuiValue Read(string key);

    /// <summary>
    /// Starts calling <paramref name="changed"/> with the key and the new value whenever the store's value for one of
    /// <see cref="Keys"/> changes, on any thread. A change to <see cref="OuiValue.Null"/> clears the variable.
    /// </summary>
    /// <param name="changed">Takes the key and its new value.</param>
    /// <returns>Stops the calls when disposed.</returns>
    IDisposable Subscribe(Action<string, OuiValue> changed);

    /// <summary>Writes a variable the program changed, or a default the store had no value for, to the store. Called on the renderer.</summary>
    /// <param name="key">One of <see cref="Keys"/>.</param>
    /// <param name="value">The program's value.</param>
    ValueTask WriteAsync(string key, OuiValue value);
}

/// <summary>Helpers for <see cref="IOpenUIStateBinding"/>.</summary>
public static class OpenUIStateBindingExtensions
{
    /// <summary>
    /// The store's values that are not null, for <c>OpenUISessionOptions.PersistedState</c>, so a session the host makes
    /// evaluates and fetches with them from the start. <see cref="OpenUIProgram"/> does this itself.
    /// </summary>
    /// <param name="binding">The binding.</param>
    public static IReadOnlyDictionary<string, OuiValue> ReadAll(this IOpenUIStateBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var values = new Dictionary<string, OuiValue>(StringComparer.Ordinal);
        foreach (var key in binding.Keys)
        {
            if (binding.Read(key) is { IsNull: false } value)
            {
                values[key] = value;
            }
        }

        return values;
    }
}
