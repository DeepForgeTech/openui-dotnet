using MudBlazor;

namespace OpenUI.Lang.Blazor.Mud;

/// <summary>Stack's and Card's flex props, as MudStack takes them.</summary>
internal static class MudFlex
{
    /// <summary>
    /// The custom property by which a Stack tells the Cards in it how narrow they may get. Cards share a row and wrap once
    /// each would be narrower than 18rem, so only a Stack that wraps asks for it: in a row that does not wrap, a minimum would
    /// push the row wider than a phone.
    /// </summary>
    public const string CardMinWidth = "--oui-card-min-width";

    // MudStack counts in steps of 4px.
    public static int Spacing(string gap) => gap switch
    {
        "none" => 0,
        "xs" => 1,
        "s" => 2,
        "l" => 4,
        "xl" => 6,
        "2xl" => 8,
        _ => 3,
    };

    public static AlignItems? Align(string align) => align switch
    {
        "start" => AlignItems.Start,
        "center" => AlignItems.Center,
        "end" => AlignItems.End,
        "stretch" => AlignItems.Stretch,
        "baseline" => AlignItems.Baseline,
        _ => null,
    };

    public static Justify? Justify(string justify) => justify switch
    {
        "start" => MudBlazor.Justify.FlexStart,
        "center" => MudBlazor.Justify.Center,
        "end" => MudBlazor.Justify.FlexEnd,
        "between" => MudBlazor.Justify.SpaceBetween,
        "around" => MudBlazor.Justify.SpaceAround,
        "evenly" => MudBlazor.Justify.SpaceEvenly,
        _ => null,
    };
}
