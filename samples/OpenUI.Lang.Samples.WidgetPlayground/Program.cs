using System.Globalization;
using OpenUI.Lang.Samples.Llm;
using OpenUI.Lang.Samples.WidgetPlayground.Catalog;
using OpenUI.Lang.Samples.WidgetPlayground.Components;
using OpenUI.Lang.Samples.WidgetPlayground.Examples;
using OpenUI.Lang.Samples.WidgetPlayground.Generation;

var builder = WebApplication.CreateBuilder(args);

// Only Development turns this on by itself, and without it a run from the build output serves no
// framework script, so the page draws but nothing on it responds.
builder.WebHost.UseStaticWebAssets();

var port = builder.Configuration["OPENUI_PLAYGROUND_PORT"] is { Length: > 0 } portText
    ? int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed is > 0 and < 65536
        ? parsed
        : throw new InvalidOperationException($"OPENUI_PLAYGROUND_PORT must be a port number, not '{portText}'.")
    : 5290;
var llm = LlmSettings.Read(builder.Configuration);

// Localhost only: a real provider spends its API key on every prompt.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenLocalhost(port));

var libraries = PlaygroundLibraries.Build();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton(libraries);
builder.Services.AddSingleton(ExampleCatalog.Load());
builder.Services.AddSingleton(llm);
builder.Services.AddChatModel(llm, messages => FakeReplies.For(messages, libraries));
builder.Services.AddSingleton<ProgramDesigner>();

var app = builder.Build();

app.Logger.LogInformation("playground_ready provider={Provider} port={Port}", llm.Provider, port);

app.UseLocalOriginsOnly();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

/// <summary>Public so the tests can start the real app.</summary>
public partial class Program;
