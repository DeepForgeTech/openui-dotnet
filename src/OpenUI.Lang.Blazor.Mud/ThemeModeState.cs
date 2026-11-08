using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace OpenUI.Lang.Blazor.Mud;

/// <summary>
/// One person's light or dark choice, for the length of a circuit. A browser that won't store it (a private window, blocked site
/// data) still gets the choice until the page reloads, because the stored copy is only a convenience.
/// </summary>
public sealed class ThemeModeState(IJSRuntime js, ThemeModeOptions options, ILogger<ThemeModeState> logger) : IAsyncDisposable
{
    internal const string StorageKey = "openui-theme-mode";
    internal const string ModulePath = "./_content/OpenUI.Lang.Blazor.Mud/theme-mode.js";

    private Task? _loading;
    private bool _hasChosen;
    private DotNetObjectReference<ThemeModeState>? _self;
    private Task<IJSObjectReference>? _import;
    private bool _systemIsDark;

    /// <summary>Raised when <see cref="Mode"/> or <see cref="IsDark"/> changes, on whichever thread changed it.</summary>
    public event Action? Changed;

    /// <summary>What the person chose, or the host's default until they do.</summary>
    public ThemeMode Mode { get; private set; } = options.DefaultMode;

    /// <summary>True when the dark palette applies now.</summary>
    public bool IsDark => Mode == ThemeMode.Dark || (Mode == ThemeMode.System && _systemIsDark);

    internal ThemeModeOptions Options => options;

    /// <summary>Applies a choice and keeps it for the next visit.</summary>
    public async Task SetModeAsync(ThemeMode mode)
    {
        _hasChosen = true;
        Mode = mode;
        Changed?.Invoke();
        await PaintAsync();
        // The mode as it stands once the browser is there, so that choices made while waiting for it store the last one.
        await TryBrowserAsync(browser => browser.InvokeVoidAsync("write", StorageKey, Stored(Mode)));
    }

    /// <summary>The name a mode is stored under. It is what the head script compares against too.</summary>
    internal static string Stored(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => "light",
        ThemeMode.Dark => "dark",
        _ => "system",
    };

    // Before the first draw, so that draw already has the right palette. Once for each state, however many providers come and go.
    // An answer that comes after the provider gave up waiting applies through Changed.
    internal Task LoadAsync() => _loading ??= LoadOnceAsync();

    private async Task LoadOnceAsync()
    {
        _self ??= DotNetObjectReference.Create(this);
        await TryBrowserAsync(async browser =>
        {
            if (await browser.InvokeAsync<StoredThemeMode?>("start", StorageKey, _self) is { } stored)
            {
                _systemIsDark = stored.SystemIsDark;

                // A person who chose while this was still waiting has chosen after what was stored.
                if (!_hasChosen)
                {
                    Mode = stored.Mode switch
                    {
                        "light" => ThemeMode.Light,
                        "dark" => ThemeMode.Dark,
                        "system" => ThemeMode.System,
                        _ => options.DefaultMode,
                    };
                }
            }
        });
        Changed?.Invoke();
        await PaintAsync();
    }

    /// <summary>Called from the browser when the system's preference changes.</summary>
    /// <param name="isDark">True when it now asks for dark.</param>
    [JSInvokable]
    public async Task SystemChangedAsync(bool isDark)
    {
        _systemIsDark = isDark;
        Changed?.Invoke();
        await PaintAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _self?.Dispose();
        try
        {
            if (_import is { IsCompletedSuccessfully: true })
            {
                await _import.Result.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // The circuit went first, and the module with it.
        }
    }

    // The page's own colour, and the native parts of it such as scrollbars, which no MudBlazor palette reaches.
    internal Task PaintAsync() => TryBrowserAsync(browser => browser.InvokeVoidAsync("paint", IsDark, options.Background(IsDark)));

    // The browser is only ever an improvement: whatever goes wrong there, the page draws with what it knows.
    private async Task TryBrowserAsync(Func<IJSObjectReference, ValueTask> call)
    {
        try
        {
            // One import for every call, so calls made while it loads carry on in the order they were made.
            _import ??= js.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask();
            await call(await _import);
        }
        catch (Exception error) when (error is JSException or JSDisconnectedException or OperationCanceledException or InvalidOperationException)
        {
            logger.LogWarning("theme_mode_browser_unavailable reason={Reason}", error.GetType().Name);

            // An import that failed or timed out is tried again by the next call. One that worked is kept.
            if (_import is { IsCompleted: true, IsCompletedSuccessfully: false })
            {
                _import = null;
            }
        }
    }

}

/// <summary>What the browser knows when a circuit starts.</summary>
/// <param name="Mode">The value stored, which may be anything, or null.</param>
/// <param name="SystemIsDark">True when the system asks for dark.</param>
internal sealed record StoredThemeMode(string? Mode, bool SystemIsDark);
