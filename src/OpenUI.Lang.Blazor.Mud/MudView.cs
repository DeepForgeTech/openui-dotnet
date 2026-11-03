using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor.Mud;

/// <summary>Base class of this package's views: what its definitions' fields hold, and how they seed.</summary>
public abstract partial class MudView : OpenUIComponent
{
    /// <summary>What an input with a <c>value</c> and a <c>defaultValue</c> shows: what <see cref="OpenUIComponent.Input"/> says, or else the default.</summary>
    protected OuiValue InputOrDefault() => Input("value") is { IsNull: false } value ? value : Value("defaultValue");
}
