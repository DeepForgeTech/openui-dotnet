using OpenUI.Lang.Values;

namespace OpenUI.Lang.Runtime;

/// <summary>Writes a <c>$variable</c> back, as a bound input does (language.md, section 5.2).</summary>
public interface IStateWriter
{
    /// <summary>Sets <paramref name="key"/> to <paramref name="value"/> (language.md, section 5.2).</summary>
    void Write(StateKey key, OuiValue value);
}
