using Microsoft.Extensions.DependencyInjection.Extensions;
using MudBlazor;
using OpenUI.Lang.Blazor.Mud;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the light and dark choice, for a host that has <see cref="ThemeModeProvider"/> in its layout.</summary>
public static class ThemeModeRegistration
{
    /// <summary>
    /// Lets each person choose light, dark or their system's setting: <see cref="ThemeSwitcher"/> draws the choice and a program can
    /// draw it as <c>ThemeSwitcher</c>. The choice is kept in the browser's <c>localStorage</c>.
    /// </summary>
    /// <param name="services">The app's services.</param>
    /// <param name="defaultMode">The mode of someone who has not chosen.</param>
    /// <param name="theme">The theme to draw, or null for MudBlazor's own.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddMudThemeMode(this IServiceCollection services, ThemeMode defaultMode = ThemeMode.System, MudTheme? theme = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(new ThemeModeOptions(defaultMode, theme ?? new MudTheme()));
        services.TryAddScoped<ThemeModeState>();
        return services;
    }
}
