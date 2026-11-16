using MudBlazor;

namespace OpenUI.Lang.Blazor.Mud;

/// <summary>MudBlazor parameters whose names differ by line, as attributes a view splats onto the component.</summary>
internal static class MudLine
{
    /// <summary>
    /// Stops MudThemeProvider following the system's dark mode itself. MudBlazor 8 calls it ObserveSystemThemeChange, and the 9
    /// name throws there, because MudThemeProvider takes no unmatched attributes.
    /// </summary>
    public static IReadOnlyDictionary<string, object> NotFollowingSystemDarkMode { get; } =
        new Dictionary<string, object> { [nameof(MudThemeProvider.ObserveSystemThemeChange)] = false };
}
