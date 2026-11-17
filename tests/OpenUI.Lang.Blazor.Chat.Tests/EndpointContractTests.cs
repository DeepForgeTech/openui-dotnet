using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenUI.Lang.Blazor.Chat.Tests.Kit;

namespace OpenUI.Lang.Blazor.Chat.Tests;

/// <summary>EndpointBackend against a real MapOpenUIChat, so the two can't drift apart on the wire.</summary>
public sealed class EndpointContractTests : IAsyncLifetime
{
    private readonly ScriptedModel _model = new();
    private WebApplication _app = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IChatClient>(_model);
        builder.Services.AddOpenUI(new ChatKit().Library, configure: options => options.MaxMessages = 3);
        _app = builder.Build();
        _app.MapOpenUIChat("/api/chat");
        await _app.StartAsync();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private EndpointBackend Backend() => new(_app.GetTestClient(), new Uri("/api/chat", UriKind.Relative));

    private static async Task<string> ReadAll(IAsyncEnumerable<string> reply)
    {
        var text = "";
        await foreach (var chunk in reply)
        {
            text += chunk;
        }

        return text;
    }

    [Fact]
    public async Task A_reply_streams_back_piece_by_piece_and_the_server_leads_with_its_prompt()
    {
        await _model.Say("root = Stack([");
        await _model.Say("TextContent(\"It's 'quoted' & <fine>\")])\n");
        _model.Finish();

        var reply = await ReadAll(Backend().StreamReplyAsync(
            [new ChatMessage(ChatRole.User, "Hi"), new ChatMessage(ChatRole.Assistant, "root = Stack([])"), new ChatMessage(ChatRole.User, "Again")],
            CancellationToken.None));

        Assert.Equal("root = Stack([TextContent(\"It's 'quoted' & <fine>\")])\n", reply);
        var turn = Assert.Single(_model.Turns);
        Assert.Equal([ChatRole.System, ChatRole.User, ChatRole.Assistant, ChatRole.User], turn.Select(message => message.Role));
        Assert.Contains("Stack", turn[0].Text);
        Assert.Equal(["Hi", "root = Stack([])", "Again"], turn.Skip(1).Select(message => message.Text));
    }

    [Fact]
    public async Task A_request_the_server_refuses_fails_with_the_servers_sentence()
    {
        var turns = Enumerable.Range(0, 4).Select(index => new ChatMessage(ChatRole.User, $"{index}")).ToList();

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => ReadAll(Backend().StreamReplyAsync(turns, CancellationToken.None)));

        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Equal("'messages' must not have more than 3 entries.", error.Message);
    }

    [Fact]
    public async Task A_model_that_fails_before_the_reply_starts_fails_with_the_servers_sentence()
    {
        _model.Fail("Secret host name");

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            ReadAll(Backend().StreamReplyAsync([new ChatMessage(ChatRole.User, "Hi")], CancellationToken.None)));

        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
        Assert.Equal("The model request failed.", error.Message);
    }

    [Fact]
    public async Task A_model_that_fails_mid_reply_ends_the_stream_as_a_failure_after_what_came()
    {
        await _model.Say("root = Stack([])\n");
        _model.Fail("Gone");
        var received = new List<string>();

        await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (var chunk in Backend().StreamReplyAsync([new ChatMessage(ChatRole.User, "Hi")], CancellationToken.None))
            {
                received.Add(chunk);
            }
        });

        Assert.Equal(["root = Stack([])\n"], received);
    }

    [Fact]
    public async Task Cancelling_the_reply_ends_the_model_call()
    {
        await _model.Say("root = Stack([])\n");
        using var stop = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in Backend().StreamReplyAsync([new ChatMessage(ChatRole.User, "Hi")], stop.Token))
            {
                await stop.CancelAsync();
            }
        });
    }
}
