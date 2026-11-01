using System.Net;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace OpenUI.Lang.AspNetCore.Tests;

/// <summary>The reply-completed hook is how a host sees what the library made of a reply.</summary>
public sealed class ReplyCompletedHookTests
{
    private const string Program = "root = Stack([title, tag])\ntitle = Text(\"Hello\", \"heading\")\ntag = Tag(\"new\", \"green\")";

    [Fact]
    public async Task The_hook_receives_the_reply_parsed_against_the_library()
    {
        OpenUIReplyCompletedContext? seen = null;
        string? path = null;
        var pieces = Program.Chunk(7).Select(characters => new string(characters)).ToArray();
        await using var host = await ChatHost.StartAsync(
            FakeChatClient.Replying(pieces),
            options => options.OnReplyCompleted = context =>
            {
                seen = context;
                path = context.HttpContext.Request.Path;
                return Task.CompletedTask;
            });

        using var response = await host.PostMessagesAsync(("user", "hi"));
        await response.Content.ReadAsStringAsync();

        Assert.NotNull(seen);
        Assert.Equal(OpenUIReplyOutcome.Completed, seen.Outcome);
        Assert.Equal(Program, seen.Reply);
        Assert.Equal("Stack", seen.Report.Render.Root?.Type);
        Assert.Equal(3, seen.Report.Document.Statements.Length);
        Assert.False(seen.Report.Document.Incomplete);
        Assert.Empty(seen.Report.Errors);
        Assert.Equal(ChatHost.Route, path);
    }

    [Fact]
    public async Task The_hook_sees_the_errors_the_library_finds()
    {
        OpenUIReplyCompletedContext? seen = null;
        await using var host = await ChatHost.StartAsync(
            FakeChatClient.Replying("root = Stack([x])\nx = Nope(\"a\")"),
            options => options.OnReplyCompleted = context =>
            {
                seen = context;
                return Task.CompletedTask;
            });

        using var response = await host.PostMessagesAsync(("user", "hi"));
        await response.Content.ReadAsStringAsync();

        Assert.NotNull(seen);
        Assert.NotEmpty(seen.Report.Errors);
    }

    [Fact]
    public async Task The_hook_runs_before_the_response_ends_so_a_host_can_rely_on_its_effects()
    {
        var finished = false;
        await using var host = await ChatHost.StartAsync(
            FakeChatClient.Replying(Program),
            options => options.OnReplyCompleted = async _ =>
            {
                await Task.Delay(50);
                finished = true;
            });

        using var response = await host.PostMessagesAsync(("user", "hi"));
        await response.Content.ReadAsStringAsync();

        Assert.True(finished);
    }

    [Fact]
    public async Task A_hook_that_throws_is_logged_and_the_client_still_gets_the_whole_reply()
    {
        var failure = new InvalidOperationException("SECRET hook detail");
        await using var host = await ChatHost.StartAsync(
            FakeChatClient.Replying("one", "two"),
            options => options.OnReplyCompleted = _ => throw failure);

        using var response = await host.PostMessagesAsync(("user", "hi"));
        var (raw, lines) = await response.ReadNdjsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, lines.Count);
        Assert.DoesNotContain("SECRET", raw);
        var logged = Assert.Single(host.Logs.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Same(failure, logged.Exception);
    }

    [Fact]
    public async Task After_a_mid_stream_failure_the_hook_gets_the_partial_reply_marked_failed()
    {
        OpenUIReplyCompletedContext? seen = null;
        var model = new FakeChatClient(_ => PartialThenFail());
        await using var host = await ChatHost.StartAsync(
            model,
            options => options.OnReplyCompleted = context =>
            {
                seen = context;
                return Task.CompletedTask;
            });

        using var response = await host.PostMessagesAsync(("user", "hi"));
        await response.Content.ReadAsStringAsync();

        Assert.NotNull(seen);
        Assert.Equal(OpenUIReplyOutcome.Failed, seen.Outcome);
        Assert.Equal("root = Stack([a", seen.Reply);
        Assert.True(seen.Report.Document.Incomplete);

        static async IAsyncEnumerable<ChatResponseUpdate> PartialThenFail()
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "root = Stack([a");
            await Task.Yield();
            throw new InvalidOperationException("boom");
        }
    }

    [Fact]
    public async Task A_model_failure_before_the_reply_does_not_run_the_hook()
    {
        var called = false;
        var model = new FakeChatClient(_ => throw new InvalidOperationException("boom"));
        await using var host = await ChatHost.StartAsync(
            model,
            options => options.OnReplyCompleted = _ =>
            {
                called = true;
                return Task.CompletedTask;
            });

        using var response = await host.PostMessagesAsync(("user", "hi"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.False(called);
    }
}
