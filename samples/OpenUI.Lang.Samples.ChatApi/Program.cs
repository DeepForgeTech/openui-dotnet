using OpenUI.Lang.AspNetCore;
using OpenUI.Lang.Samples.ChatApi;
using OpenUI.Lang.Samples.ChatApi.Weather;
using OpenUI.Lang.Samples.Llm;

var builder = WebApplication.CreateBuilder(args);
var settings = SampleSettings.Read(builder.Configuration, builder.Environment.ContentRootPath);

// Localhost only: a real provider spends its API key on every request.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenLocalhost(settings.Port));

if (!File.Exists(settings.SpecPath))
{
    throw new InvalidOperationException($"Missing {settings.SpecPath}. In samples/react-chat, run: bun install && bun run generate");
}

builder.Services.AddOpenUI(settings.SpecPath, configure: options => options.OnReplyCompleted = LastReply.OnReplyCompletedAsync);
builder.Services.AddSingleton<LastReply>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<WeatherService>();

builder.Services.AddChatModel(settings.Llm, _ => settings.FakeReplyFile is { } file ? File.ReadAllText(file) : FakeReply.Text);

var app = builder.Build();

app.Logger.LogInformation(
    "chat_api_ready provider={Provider} spec={SpecPath} port={Port}",
    settings.Llm.Provider,
    settings.SpecPath,
    settings.Port);

app.UseLocalOriginsOnly();
app.MapOpenUIChat("/api/chat");
app.MapWeatherApi();

// The browser cannot tell a canned reply from a real one, so it asks what to warn about.
app.MapGet("/api/model", () => new { notice = settings.Llm.IsFake ? LlmSettings.FakeNotice : null });

// The whole last reply is for whoever is working on this app, so it stays out of anything deployed.
if (app.Environment.IsDevelopment())
{
    app.MapGet("/api/debug/last", (LastReply last) =>
        last.Report is { } report ? Results.Json(report) : Results.Json(new { error = "No reply has finished yet." }, statusCode: StatusCodes.Status404NotFound));
}

app.Run();

/// <summary>Public so the tests can start the real app.</summary>
public partial class Program;
