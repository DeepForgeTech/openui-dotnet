using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor;

/// <summary>
/// Base class for a view the user writes through, such as an input or a select. A write reaches the props only once the
/// session evaluates it, and the view draws again before that, so a view drawing its props would draw the old value in
/// between, and a widget handed the old value can report it back as the user's and undo the change. This one draws what
/// the user wrote, through <see cref="Input"/>, until the program answers with new props, whatever the answer is.
/// </summary>
public abstract class OpenUIWritableComponent : OpenUIComponent
{
    private readonly Dictionary<string, OuiValue> _held = new(StringComparer.Ordinal);

    // The props the held values were written against. New props are the program's answer, so they end the hold.
    private OuiComponent? _heldAgainst;
    private bool _heldChanged;

    /// <summary>What the user last wrote to the prop, until the program answers; otherwise what <see cref="OpenUIComponent.Input"/> says.</summary>
    protected override OuiValue Input(string prop) =>
        IsHolding && _held.TryGetValue(prop, out var held) ? held : base.Input(prop);

    /// <summary>Holds the value for <see cref="Input"/>, then writes it as <see cref="OpenUIComponent.Write"/> does.</summary>
    protected override void Write(string prop, OuiValue value)
    {
        if (!IsHolding)
        {
            _held.Clear();
            _heldAgainst = Node;
        }

        _held[prop] = value;
        _heldChanged = true;
        base.Write(prop, value);
    }

    private bool IsHolding => ReferenceEquals(_heldAgainst, Node);

    // The end of a hold forces a draw. It matters only when the program's answer equals the props drawn before the write, as
    // with a view that has no binding and no field, or a value the session rejected: without it, the view would keep showing
    // the value the user wrote though nothing took it.
    private protected override bool HeldChanged()
    {
        if (_heldAgainst is not null && !IsHolding)
        {
            _held.Clear();
            _heldAgainst = null;
            _heldChanged = true;
        }

        var changed = _heldChanged;
        _heldChanged = false;
        return changed;
    }
}
