using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenUI.Lang.Blazor.Chat.Tests.Kit;

namespace OpenUI.Lang.Blazor.Chat.Tests;

/// <summary>What the endpoint backend sends and reads stays inside the limits on both ends.</summary>
public sealed class EndpointLimitTests : IAsyncLifetime
{
    private readonly ScriptedModel _model = new();
    private WebApplication _app = null!;

    // MapOpenUIChat with its default limits: 100 messages and a 1 MiB body.
    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IChatClient>(_model);
        builder.Services.AddOpenUI(new ChatKit().Library);
        _app = builder.Build();
        _app.MapOpenUIChat("/api/chat");
        await _app.StartAsync();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private OpenUIChatEndpointBackend Backend(OpenUIBlazorChatOptions? options = null) =>
        new(_app.GetTestClient(), new Uri("/api/chat", UriKind.Relative), options);

    private static List<ChatMessage> Exchanges(int count, string reply = "root = Stack([])")
    {
        var turns = new List<ChatMessage>();
        for (var index = 0; index < count; index++)
        {
            turns.Add(new ChatMessage(ChatRole.User, $"Question {index}"));
            turns.Add(new ChatMessage(ChatRole.Assistant, reply));
        }

        turns.Add(new ChatMessage(ChatRole.User, "Last question\nYour last program had these errors: [\"x\"]"));
        return turns;
    }

    private static async Task Drain(IAsyncEnumerable<string> reply)
    {
        await foreach (var _ in reply)
        {
        }
    }

    [Fact]
    public async Task A_conversation_longer_than_the_endpoint_takes_sends_its_newest_turns_from_a_user_turn()
    {
        _model.Finish();

        await Drain(Backend().StreamReplyAsync(Exchanges(80), CancellationToken.None));

        var sent = Assert.Single(_model.Turns).Skip(1).ToList();
        Assert.Equal(99, sent.Count);
        Assert.Equal(ChatRole.User, sent[0].Role);
        Assert.Equal("Question 31", sent[0].Text);
        Assert.Equal("Last question\nYour last program had these errors: [\"x\"]", sent[^1].Text);
    }

    [Fact]
    public async Task A_history_larger_than_the_endpoint_takes_drops_its_oldest_turns_to_fit()
    {
        _model.Finish();
        // Six replies of 300 KB, with characters that take two bytes each, are more than the 1 MiB the endpoint reads.
        var turns = Exchanges(6, new string('é', 150_000));

        await Drain(Backend().StreamReplyAsync(turns, CancellationToken.None));

        var sent = Assert.Single(_model.Turns).Skip(1).ToList();
        Assert.Equal(7, sent.Count);
        Assert.Equal("Question 3", sent[0].Text);
        Assert.Equal(turns[^1].Text, sent[^1].Text);
    }

    [Fact]
    public async Task The_history_limits_can_be_lowered()
    {
        _model.Finish();

        await Drain(Backend(new OpenUIBlazorChatOptions { MaxHistoryTurns = 4 }).StreamReplyAsync(Exchanges(5), CancellationToken.None));

        Assert.Equal(["Question 4", "root = Stack([])", "Last question\nYour last program had these errors: [\"x\"]"],
            Assert.Single(_model.Turns).Skip(1).Select(turn => turn.Text));
    }

    [Fact]
    public async Task A_line_longer_than_a_whole_reply_could_be_fails_with_a_sentence()
    {
        var backend = new OpenUIChatEndpointBackend(
            new HttpClient(new Answer(HttpStatusCode.OK, new string('x', 20_000))),
            new Uri("http://localhost/api/chat"),
            new OpenUIBlazorChatOptions { MaxReplyChars = 100 });

        var error = await Assert.ThrowsAsync<OpenUIChatException>(() => Drain(backend.StreamReplyAsync([new ChatMessage(ChatRole.User, "Hi")], CancellationToken.None)));

        Assert.Equal("The chat endpoint sent a line longer than 1624 characters.", error.Message);
    }

    [Fact]
    public async Task Only_the_start_of_an_error_body_is_read()
    {
        var body = $$"""{"error":"{{new string('x', 1024 * 1024)}}"}""";
        var backend = new OpenUIChatEndpointBackend(new HttpClient(new Answer(HttpStatusCode.InternalServerError, body)), new Uri("http://localhost/api/chat"));

        var error = await Assert.ThrowsAsync<OpenUIChatException>(() => Drain(backend.StreamReplyAsync([new ChatMessage(ChatRole.User, "Hi")], CancellationToken.None)));

        Assert.Equal("The chat endpoint answered with status 500.", error.Message);
        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);
    }

    [Fact]
    public async Task An_endpoint_that_cannot_be_reached_fails_with_a_sentence_and_not_the_address()
    {
        var backend = new OpenUIChatEndpointBackend(new HttpClient(new Unreachable()), new Uri("http://secret.example/api/chat"));

        var error = await Assert.ThrowsAsync<OpenUIChatException>(() => Drain(backend.StreamReplyAsync([new ChatMessage(ChatRole.User, "Hi")], CancellationToken.None)));

        Assert.Equal("The chat endpoint could not be reached.", error.Message);
    }

    private sealed class Answer(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8) });
    }

    private sealed class Unreachable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused (secret.example:80)");
    }
}
