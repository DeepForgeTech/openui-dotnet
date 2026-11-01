using System.Globalization;
using MudBlazor.Services;
using OpenUI.Lang.Samples.BlazorApp;
using OpenUI.Lang.Samples.BlazorApp.Chat;
using OpenUI.Lang.Samples.BlazorApp.Components;
using OpenUI.Lang.Samples.BlazorApp.Weather;
using OpenUI.Lang.Samples.Llm;

var builder = WebApplication.CreateBuilder(args);

// Only Development turns this on by itself, and without it a run from the build output serves no
// _content/MudBlazor files, so the pages draw but nothing on them responds.
builder.WebHost.UseStaticWebAssets();

var port = builder.Configuration["OPENUI_BLAZOR_PORT"] is { Length: > 0 } portText
    ? int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed is > 0 and < 65536
        ? parsed
        : throw new InvalidOperationException($"OPENUI_BLAZOR_PORT must be a port number, not '{portText}'.")
    : 5380;
var weatherApi = builder.Configuration["OPENUI_WEATHER_API"] is { Length: > 0 } url ? url : "http://localhost:5381";
var dataDirectory = builder.Configuration["OPENUI_BLAZOR_DATA"] is { Length: > 0 } data
    ? data
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "openui-blazor-sample");
var llm = LlmSettings.Read(builder.Configuration);

// Localhost only: a real provider spends its API key on every prompt.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenLocalhost(port));

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddSingleton(llm);
builder.Services.AddChatModel(llm, FakeReplies.For);
builder.Services.AddSingleton(services => new ConversationStore(dataDirectory, services.GetRequiredService<ILogger<ConversationStore>>()));
builder.Services.AddScoped<ToolCallLog>();
builder.Services.AddHttpClient<WeatherTools>(http => http.BaseAddress = new Uri(weatherApi));

var app = builder.Build();

app.Logger.LogInformation("blazor_app_ready provider={Provider} port={Port} weatherApi={WeatherApi} data={Data}", llm.Provider, port, weatherApi, dataDirectory);

app.UseLocalOriginsOnly();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
