using OpenUI.Lang.Values;

namespace OpenUI.Lang.Samples.BlazorApp.Playback;

/// <summary>
/// Which value a player writes next. It knows nothing of how the player is drawn, so it could serve
/// any component set.
/// </summary>
public static class PlaybackSteps
{
    /// <summary>
    /// The value after <paramref name="current"/> in <paramref name="values"/>, or null at the end when
    /// it doesn't loop. A value that isn't in the list starts from the first. A one-item list, such as
    /// a Slider writes, matches a list that holds it as it is, or else is matched by its item and
    /// written back as one, so a Slider bound to the same <c>$variable</c> keeps working either way.
    /// </summary>
    public static OuiValue? Next(OuiValue current, IReadOnlyList<OuiValue> values, bool loop)
    {
        if (values.Count == 0)
        {
            return null;
        }

        // A list that holds the value as it is comes first; only then is a one-item list matched by its item.
        var at = IndexOf(values, current);
        var wrapped = false;
        if (at < 0 && current.TryGetArray(out var items) && items.Count == 1)
        {
            wrapped = true;
            at = IndexOf(values, items[0]);
        }

        var next = at < 0 ? 0 : at + 1;
        if (next >= values.Count)
        {
            if (!loop)
            {
                return null;
            }

            next = 0;
        }

        return wrapped ? OuiArray.Create(values[next]) : values[next];
    }

    /// <summary>True when <paramref name="current"/> is the last value, so a player that doesn't loop has nothing left to play.</summary>
    public static bool AtEnd(OuiValue current, IReadOnlyList<OuiValue> values) =>
        values.Count > 0 && Next(current, values, loop: false) is null;

    private static int IndexOf(IReadOnlyList<OuiValue> values, OuiValue value)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i].Equals(value))
            {
                return i;
            }
        }

        return -1;
    }
}
