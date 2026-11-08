using MudBlazor;
using MudBlazor.Utilities;

namespace OpenUI.Lang.Blazor.Mud;

/// <summary>What a host chose with <c>AddMudThemeMode</c>.</summary>
/// <param name="DefaultMode">The mode of someone with no stored choice, or a stored value that makes no sense.</param>
/// <param name="Theme">The theme whose palettes the choice picks between.</param>
public sealed record ThemeModeOptions(ThemeMode DefaultMode, MudTheme Theme)
{
    /// <summary>The page's own colour under a palette, which shows before anything is drawn.</summary>
    internal string Background(bool dark)
    {
        // Typed as their base, because MudBlazor 8 gives the two palettes types of their own.
        Palette palette = dark ? Theme.PaletteDark : Theme.PaletteLight;
        return palette.Background.ToString(MudColorOutputFormats.Hex);
    }
}
