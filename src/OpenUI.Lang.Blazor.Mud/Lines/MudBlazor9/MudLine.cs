using MudBlazor;

namespace OpenUI.Lang.Blazor.Mud;

/// <summary>MudBlazor parameters whose names differ by line, as attributes a view splats onto the component.</summary>
internal static class MudLine
{
    /// <summary>Stops MudThemeProvider following the system's dark mode itself. MudBlazor 9 calls it ObserveSystemDarkModeChange.</summary>
    public static IReadOnlyDictionary<string, object> NotFollowingSystemDarkMode { get; } =
        new Dictionary<string, object> { [nameof(MudThemeProvider.ObserveSystemDarkModeChange)] = false };
}
