namespace OpenUI.Lang.Blazor.Mud;

/// <summary>Date fields: the day or range a picker shows, and what the user picked.</summary>
public abstract partial class MudView
{
    /// <summary>The day in the input's value, or null.</summary>
    protected DateTime? Day() => DateValues.Read(Input("value"));

    /// <summary>
    /// Writes the day the user picked. A picker that echoes the day it was given writes nothing: the stored time of
    /// day need not be midnight, and rendering must not rewrite what the user never touched.
    /// </summary>
    protected void SetDay(DateTime? day)
    {
        if (day != Day())
        {
            Write("value", DateValues.Write(day));
        }
    }

    /// <summary>The range in the input's value, or null.</summary>
    protected DayRange? Range() => DateValues.ReadRange(Input("value"));

    /// <summary>Writes the range the user picked, or nothing when a picker echoes the one it was given; see <see cref="SetDay"/>.</summary>
    protected void SetRange(DayRange? range)
    {
        if ((range ?? new DayRange(null, null)) != (Range() ?? new DayRange(null, null)))
        {
            Write("value", DateValues.WriteRange(range));
        }
    }
}
