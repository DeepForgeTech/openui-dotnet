using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Services;
using OpenUI.Lang.Blazor.Mud;

namespace OpenUI.Lang.Blazor.Tests.Mud;

/// <summary>The light and dark choice, with the browser's side faked here and driven for real in the sample's checks.</summary>
public sealed class ThemeModeTests
{
    private const string Page = "the page";

    private static BunitContext NewContext(ThemeMode defaultMode = ThemeMode.System, bool interactive = true)
    {
        var context = TestLibrary.NewContext(
            services => services.AddMudServices().AddMudThemeMode(defaultMode).AddOpenUIComponents(TestLibrary.Mud),
            interactive);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Render<MudPopoverProvider>();
        return context;
    }

    private static BunitJSModuleInterop Browser(BunitContext context, string? stored, bool systemIsDark = false)
    {
        var browser = context.JSInterop.SetupModule(ThemeModeState.ModulePath);
        browser.Setup<StoredThemeMode?>("start", _ => true).SetResult(new StoredThemeMode(stored, systemIsDark));
        browser.SetupVoid("write", _ => true).SetVoidResult();
        browser.SetupVoid("paint", _ => true).SetVoidResult();
        return browser;
    }

    private static IRenderedComponent<ThemeModeProvider> Provider(BunitContext context) =>
        context.Render<ThemeModeProvider>(parameters => parameters.AddChildContent($"<p>{Page}</p>").AddChildContent<ThemeSwitcher>());

    private static bool DrawnDark<TProvider>(IRenderedComponent<TProvider> provider)
        where TProvider : ThemeModeProvider =>
        provider.FindComponent<MudThemeProvider>().Instance.GetState(mud => mud.IsDarkMode);

    private static ThemeMode Switched(IRenderedComponent<ThemeModeProvider> provider) =>
        provider.FindComponent<MudToggleGroup<ThemeMode>>().Instance.GetState(toggle => toggle.Value);

    private static Task Choose(IRenderedComponent<ThemeModeProvider> provider, string label) =>
        provider.InvokeAsync(() => provider.Find($"[aria-label={label}]").Click());

    [Fact]
    public async Task Nothing_is_drawn_until_the_stored_choice_is_known_so_the_first_draw_is_right()
    {
        await using var context = NewContext();
        var browser = context.JSInterop.SetupModule(ThemeModeState.ModulePath);
        var read = browser.Setup<StoredThemeMode?>("start", _ => true);
        browser.SetupVoid("paint", _ => true).SetVoidResult();

        // A live circuit reports this mode whether or not a server pass ran, so it mustn't count as one.
        var provider = context.Render<ThemeModeProvider>(parameters => parameters
            .SetAssignedRenderMode(RenderMode.InteractiveServer)
            .AddChildContent($"<p>{Page}</p>"));

        Assert.DoesNotContain(Page, provider.Markup);
        Assert.Empty(provider.FindComponents<MudThemeProvider>());

        read.SetResult(new StoredThemeMode("dark", SystemIsDark: false));

        provider.WaitForAssertion(() => Assert.Contains(Page, provider.Markup));
        Assert.True(DrawnDark(provider));
    }

    [Fact]
    public async Task A_choice_applies_at_once_and_is_stored_for_the_next_visit()
    {
        await using var context = NewContext();
        var browser = Browser(context, stored: null);
        var provider = Provider(context);
        provider.WaitForAssertion(() => Assert.Contains(Page, provider.Markup));
        Assert.False(DrawnDark(provider));

        await Choose(provider, "Dark");

        provider.WaitForAssertion(() => Assert.True(DrawnDark(provider)));
        Assert.Equal([ThemeModeState.StorageKey, "dark"], browser.VerifyInvoke("write").Arguments);
        Assert.Equal(ThemeMode.Dark, context.Services.GetRequiredService<ThemeModeState>().Mode);
    }

    [Theory]
    [InlineData("light", ThemeMode.Dark, false)]
    [InlineData("dark", ThemeMode.Light, true)]
    [InlineData("system", ThemeMode.Light, true)]
    public async Task A_stored_choice_beats_the_default_on_the_next_visit(string stored, ThemeMode defaultMode, bool dark)
    {
        await using var context = NewContext(defaultMode);
        Browser(context, stored, systemIsDark: true);

        var provider = Provider(context);

        provider.WaitForAssertion(() => Assert.Equal(dark, DrawnDark(provider)));
    }

    [Theory]
    [InlineData("purple")]
    [InlineData("")]
    [InlineData("DARK")]
    [InlineData("<script>")]
    [InlineData(null)]
    public async Task A_stored_value_that_makes_no_sense_falls_back_to_the_default(string? stored)
    {
        await using var context = NewContext(ThemeMode.Dark);
        Browser(context, stored);

        var provider = Provider(context);

        provider.WaitForAssertion(() => Assert.True(DrawnDark(provider)));
        Assert.Equal(ThemeMode.Dark, context.Services.GetRequiredService<ThemeModeState>().Mode);
    }

    [Fact]
    public async Task Following_the_system_changes_with_it_and_a_fixed_choice_does_not()
    {
        await using var context = NewContext();
        Browser(context, "system", systemIsDark: true);
        var provider = Provider(context);
        provider.WaitForAssertion(() => Assert.True(DrawnDark(provider)));
        var state = context.Services.GetRequiredService<ThemeModeState>();

        await provider.InvokeAsync(() => state.SystemChangedAsync(false));
        provider.WaitForAssertion(() => Assert.False(DrawnDark(provider)));

        await Choose(provider, "Light");
        await provider.InvokeAsync(() => state.SystemChangedAsync(true));
        Assert.False(state.IsDark);
        Assert.False(DrawnDark(provider));
    }

    [Fact]
    public async Task With_no_browser_storage_the_default_draws_and_a_choice_still_applies()
    {
        await using var context = NewContext(ThemeMode.Light);
        var browser = context.JSInterop.SetupModule(ThemeModeState.ModulePath);
        browser.Setup<StoredThemeMode?>("start", _ => true).SetException(new JSException("The operation is insecure."));
        browser.SetupVoid("write", _ => true).SetException(new JSException("The operation is insecure."));
        browser.SetupVoid("paint", _ => true).SetVoidResult();

        var provider = Provider(context);
        provider.WaitForAssertion(() => Assert.Contains(Page, provider.Markup));
        Assert.False(DrawnDark(provider));

        await Choose(provider, "Dark");

        provider.WaitForAssertion(() => Assert.True(DrawnDark(provider)));
    }

    [Fact]
    public async Task A_browser_that_answers_nothing_still_leaves_a_working_page()
    {
        await using var context = TestLibrary.NewContext(services =>
        {
            services.AddMudServices().AddMudThemeMode(ThemeMode.Dark);
            services.AddSingleton<IJSRuntime>(new UnreachableBrowser());
        });

        var provider = Provider(context);

        provider.WaitForAssertion(() => Assert.Contains(Page, provider.Markup));
        Assert.True(DrawnDark(provider));
        await Choose(provider, "Light");
        provider.WaitForAssertion(() => Assert.False(DrawnDark(provider)));
    }

    [Fact]
    public async Task A_program_can_draw_the_choice_and_the_app_bar_s_copy_follows()
    {
        await using var context = NewContext();
        Browser(context, stored: "light");
        var provider = context.Render<ThemeModeProvider>(parameters => parameters
            .AddChildContent<ThemeSwitcher>(compact => compact.Add(switcher => switcher.Compact, true))
            .AddChildContent<OpenUIProgram>(program => program.Add(component => component.Program, "root = Stack([ThemeSwitcher(\"Appearance\")])\n")));
        provider.WaitForAssertion(() => Assert.Contains("Appearance", provider.Markup));
        Assert.Empty(provider.FindComponent<OpenUIProgram>().Instance.Session.Current.Errors);

        var inProgram = provider.FindComponent<ThemeSwitcherView>();
        await provider.InvokeAsync(() => inProgram.Find("[aria-label=Dark]").Click());

        provider.WaitForAssertion(() =>
        {
            Assert.True(DrawnDark(provider));
            Assert.All(provider.FindComponents<MudToggleGroup<ThemeMode>>(), group => Assert.Equal(ThemeMode.Dark, group.Instance.GetState(toggle => toggle.Value)));
        });
    }

    [Fact]
    public async Task A_host_with_no_theme_choice_still_draws_a_program_that_asks_for_one()
    {
        await using var context = TestLibrary.NewContext(services => services.AddMudServices().AddOpenUIComponents(TestLibrary.Mud));
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = context.Render<OpenUIProgram>(parameters => parameters.Add(component => component.Program, "root = ThemeSwitcher(\"Appearance\")\n"));

        Assert.Empty(cut.Instance.Session.Current.Errors);
        Assert.Contains("no theme choice", cut.Markup);
    }

    [Fact]
    public async Task A_server_pass_draws_the_default_and_never_calls_the_browser()
    {
        await using var context = NewContext(ThemeMode.Dark, interactive: false);

        var provider = Provider(context);

        Assert.Contains(Page, provider.Markup);
        Assert.True(DrawnDark(provider));
        Assert.True(provider.Instance.WasPrerendered);
        Assert.DoesNotContain(context.JSInterop.Invocations, invocation => invocation.Identifier == "import");
    }

    [Fact]
    public async Task A_page_the_server_already_drew_stays_up_until_the_choice_is_known()
    {
        await using var context = NewContext(ThemeMode.Light);
        var browser = context.JSInterop.SetupModule(ThemeModeState.ModulePath);
        var read = browser.Setup<StoredThemeMode?>("start", _ => true);

        browser.SetupVoid("paint", _ => true).SetVoidResult();
        browser.SetupVoid("write", _ => true).SetVoidResult();

        var provider = context.Render<AfterAServerPass>(parameters => parameters.AddChildContent($"<p>{Page}</p>").AddChildContent<ThemeSwitcher>());

        Assert.Contains(Page, provider.Markup);
        Assert.False(DrawnDark(provider));

        read.SetResult(new StoredThemeMode("dark", SystemIsDark: false));

        provider.WaitForAssertion(() =>
        {
            Assert.True(DrawnDark(provider));
            Assert.Equal(ThemeMode.Dark, Switched(provider));
        });

        await Choose(provider, "System");

        provider.WaitForAssertion(() => Assert.Equal(ThemeMode.System, context.Services.GetRequiredService<ThemeModeState>().Mode));
        Assert.Equal(ThemeMode.System, Switched(provider));
    }

    [Fact]
    public async Task A_browser_whose_calls_never_finish_leaves_a_page_that_draws_the_default_and_takes_the_answer_when_it_comes()
    {
        await using var context = NewContext(ThemeMode.Light);
        var browser = context.JSInterop.SetupModule(ThemeModeState.ModulePath);
        var read = browser.Setup<StoredThemeMode?>("start", _ => true);
        browser.SetupVoid("paint", _ => true).SetVoidResult();

        var provider = Provider(context);

        provider.WaitForAssertion(() => Assert.Contains(Page, provider.Markup));
        Assert.False(DrawnDark(provider));
        Assert.Equal(ThemeMode.Light, Switched(provider));

        read.SetResult(new StoredThemeMode("dark", SystemIsDark: false));

        provider.WaitForAssertion(() =>
        {
            Assert.True(DrawnDark(provider));
            Assert.Equal(ThemeMode.Dark, Switched(provider));
        });
    }

    [Fact]
    public async Task A_runtime_that_never_completes_a_task_still_draws_the_page()
    {
        await using var context = TestLibrary.NewContext(services =>
        {
            services.AddMudServices().AddMudThemeMode(ThemeMode.Dark);
            services.AddSingleton<IJSRuntime>(new SilentBrowser());
        });

        var provider = Provider(context);

        provider.WaitForAssertion(() => Assert.Contains(Page, provider.Markup));
        Assert.True(DrawnDark(provider));
    }

    [Fact]
    public async Task A_provider_coming_back_does_not_ask_the_browser_again_or_forget_a_choice_that_could_not_be_stored()
    {
        await using var context = NewContext(ThemeMode.Light);
        var browser = context.JSInterop.SetupModule(ThemeModeState.ModulePath);
        browser.Setup<StoredThemeMode?>("start", _ => true).SetResult(new StoredThemeMode(null, SystemIsDark: false));
        browser.SetupVoid("write", _ => true).SetException(new JSException("The operation is insecure."));
        browser.SetupVoid("paint", _ => true).SetVoidResult();
        var first = Provider(context);
        first.WaitForAssertion(() => Assert.Contains(Page, first.Markup));
        await Choose(first, "Dark");
        first.WaitForAssertion(() => Assert.True(DrawnDark(first)));

        var second = Provider(context);

        second.WaitForAssertion(() => Assert.Contains(Page, second.Markup));
        Assert.True(DrawnDark(second));
        Assert.Single(browser.Invocations, invocation => invocation.Identifier == "start");
    }

    [Theory]
    [InlineData(ThemeMode.System, "system")]
    [InlineData(ThemeMode.Dark, "dark")]
    public async Task The_head_script_knows_the_key_the_default_and_both_backgrounds(ThemeMode defaultMode, string stored)
    {
        await using var context = NewContext(defaultMode);
        var theme = context.Services.GetRequiredService<ThemeModeOptions>();

        var head = context.Render<ThemeModeHead>();

        Assert.Contains($"\"{ThemeModeState.StorageKey}\"", head.Markup);
        Assert.Contains($"mode = \"{stored}\"", head.Markup);
        Assert.Contains($"\"{theme.Background(dark: false)}\"", head.Markup);
        Assert.Contains($"\"{theme.Background(dark: true)}\"", head.Markup);
    }

    // bUnit can't supply a persisted value, so this starts where the framework's restore leaves the provider. The real round trip is driven in the browser.
    private sealed class AfterAServerPass : ThemeModeProvider
    {
        public AfterAServerPass() => WasPrerendered = true;
    }

    [Fact]
    public async Task Two_choices_made_while_the_module_loads_store_the_last_one_and_the_stored_answer_does_not_undo_it()
    {
        var browser = new LateBrowser();
        await using var context = TestLibrary.NewContext(services => services.AddMudServices().AddMudThemeMode(ThemeMode.System).AddSingleton<IJSRuntime>(browser));
        var state = context.Services.GetRequiredService<ThemeModeState>();
        var loading = state.LoadAsync();

        var dark = state.SetModeAsync(ThemeMode.Dark);
        var light = state.SetModeAsync(ThemeMode.Light);
        await browser.FinishAsync();
        await Task.WhenAll(dark, light);
        browser.Module.Stored.SetResult(new StoredThemeMode("system", SystemIsDark: true));
        await loading;

        Assert.Equal("light", browser.Module.Writes.Last());
        Assert.Equal(ThemeMode.Light, state.Mode);
    }

    [Fact]
    public async Task An_import_that_timed_out_is_tried_again_by_the_next_call_and_the_choice_is_stored()
    {
        var browser = new TimesOutOnceBrowser();
        await using var context = TestLibrary.NewContext(services => services.AddMudServices().AddMudThemeMode(ThemeMode.System).AddSingleton<IJSRuntime>(browser));
        var state = context.Services.GetRequiredService<ThemeModeState>();

        await state.SetModeAsync(ThemeMode.Dark);

        Assert.Equal(2, browser.Imports);
        Assert.Equal(["dark"], browser.Module.Writes);
    }

    private sealed class TimesOutOnceBrowser : IJSRuntime
    {
        public int Imports { get; private set; }

        public LateModule Module { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ++Imports == 1
                ? new(Task.FromCanceled<TValue>(new CancellationToken(canceled: true)))
                : new((TValue)(object)Module);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    // The imports that are waiting finish last first, as separate imports could in a browser, and later ones at once. The module
    // answers for start when the test says.
    private sealed class LateBrowser : IJSRuntime
    {
        private readonly List<TaskCompletionSource<IJSObjectReference>> _imports = [];
        private bool _finished;

        public LateModule Module { get; } = new();

        public async Task FinishAsync()
        {
            _finished = true;
            foreach (var import in Enumerable.Reverse(_imports))
            {
                import.SetResult(Module);

                // Whatever waited on it has run before the next one finishes.
                await Task.Delay(100);
            }
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            var import = new TaskCompletionSource<IJSObjectReference>(TaskCreationOptions.RunContinuationsAsynchronously);
            _imports.Add(import);
            if (_finished)
            {
                import.SetResult(Module);
            }

            return new((Task<TValue>)(object)import.Task);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class LateModule : IJSObjectReference
    {
        public TaskCompletionSource<StoredThemeMode?> Stored { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> Writes { get; } = [];

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "start")
            {
                var stored = await Stored.Task;
                return stored is null ? default! : (TValue)(object)stored;
            }

            if (identifier == "write")
            {
                Writes.Add((string)args![1]!);
            }

            return default!;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);

        public ValueTask DisposeAsync() => default;
    }

    // A browser that is there for everything else and never answers for the theme's module.
    private sealed class SilentBrowser : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            identifier == "import" ? new(new TaskCompletionSource<TValue>().Task) : default;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class UnreachableBrowser : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new JSException("No such module.");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new JSException("No such module.");
    }
}
