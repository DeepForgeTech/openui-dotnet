using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor.Mud;

/// <summary>
/// Base class of this package's views: what its definitions' fields hold, and how they seed. The writable base holds what the user
/// writes until the program answers, so every view that writes keeps the user's change in the meantime.
/// </summary>
public abstract partial class MudView : OpenUIWritableComponent
{
    /// <summary>What an input with a <c>value</c> and a <c>defaultValue</c> shows: what <see cref="OpenUIComponent.Input"/> says, or else the default.</summary>
    protected OuiValue InputOrDefault() => Input("value") is { IsNull: false } value ? value : Value("defaultValue");
}
