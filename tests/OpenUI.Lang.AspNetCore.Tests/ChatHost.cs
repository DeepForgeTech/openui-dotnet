using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenUI.Lang.AspNetCore;
using OpenUI.Lang.Library;
using OpenUI.Lang.Prompting;

namespace OpenUI.Lang.AspNetCore.Tests;

internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<LogEntry> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), exception));
    }
}

/// <summary>A scripted <see cref="IChatClient"/> that records what it was asked.</summary>
internal sealed class FakeChatClient(Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> reply) : IChatClient
{
    public List<List<ChatMessage>> Calls { get; } = [];

    public static FakeChatClient Replying(params string[] pieces) => new(_ => Updates(pieces));

    public static async IAsyncEnumerable<ChatResponseUpdate> Updates(IEnumerable<string> pieces)
    {
        foreach (var piece in pieces)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, piece) { ModelId = "test-model" };
        }
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add([.. messages]);
        return reply(cancellationToken);
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The endpoint streams.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>The real endpoint, in process on a <see cref="TestServer"/>, in front of a fake model.</summary>
internal sealed class ChatHost : IAsyncDisposable
{
    public const string Route = "/api/chat";

    private readonly WebApplication _app;

    private ChatHost(WebApplication app, HttpClient client, FakeChatClient model, CapturingLoggerProvider logs)
    {
        _app = app;
        Client = client;
        Model = model;
        Logs = logs;
    }

    public HttpClient Client { get; }

    public FakeChatClient Model { get; }

    public CapturingLoggerProvider Logs { get; }

    public static LibrarySpec Spec() =>
        LibrarySpec.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "library.json")));

    /// <summary>The spec with the <c>promptOptions</c> member that <c>openui generate --spec</c> writes.</summary>
    public static LibrarySpec WithPromptOptions(LibrarySpec spec, string json) =>
        spec with { AdditionalProperties = [.. spec.AdditionalProperties, new("promptOptions", JsonDocument.Parse(json).RootElement)] };

    public static async Task<ChatHost> StartAsync(
        FakeChatClient model,
        Action<OpenUIChatOptions>? configure = null,
        PromptOptions? promptOptions = null,
        LibrarySpec? spec = null,
        Action<IEndpointConventionBuilder>? conventions = null)
    {
        var logs = new CapturingLoggerProvider();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddSingleton<IChatClient>(model);
        builder.Services.AddOpenUI(spec ?? Spec(), promptOptions, configure);

        var app = builder.Build();
        var endpoint = app.MapOpenUIChat(Route);
        conventions?.Invoke(endpoint);
        await app.StartAsync();
        return new ChatHost(app, app.GetTestClient(), model, logs);
    }

    public static StringContent Json(string json, string mediaType = "application/json") =>
        new(json, new MediaTypeHeaderValue(mediaType) { CharSet = "utf-8" });

    public Task<HttpResponseMessage> PostAsync(string json) => Client.PostAsync(Route, Json(json));

    public Task<HttpResponseMessage> PostMessagesAsync(params (string Role, string Content)[] messages) =>
        PostAsync(new JsonObject
        {
            ["messages"] = new JsonArray([.. messages.Select(message => (JsonNode)new JsonObject
            {
                ["role"] = message.Role,
                ["content"] = message.Content,
            })]),
        }.ToJsonString());

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }
}

internal static class ResponseReading
{
    /// <summary>The lines of an NDJSON body, with the raw text kept so its framing can be checked.</summary>
    public static async Task<(string Raw, List<JsonElement> Lines)> ReadNdjsonAsync(this HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToList();
        return (raw, lines);
    }

    public static async Task<string> ReadErrorAsync(this HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("error").GetString()!;
    }

    public static string Bytes(string text) => Convert.ToHexString(Encoding.UTF8.GetBytes(text));
}
