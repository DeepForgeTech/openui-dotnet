using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace OpenUI.Lang.AspNetCore.Tests;

/// <summary>When the model fails, or the client leaves.</summary>
public sealed class ChatFailureTests
{
    private const string Secret = "SECRET-host-db.internal:5432 key=sk-123";

    public static TheoryData<Exception, HttpStatusCode> BeforeTheReply() => new()
    {
        { new InvalidOperationException(Secret), HttpStatusCode.BadGateway },
        { new HttpRequestException(Secret, inner: null, HttpStatusCode.Unauthorized), HttpStatusCode.BadGateway },
        { new HttpRequestException(Secret, inner: null, HttpStatusCode.TooManyRequests), HttpStatusCode.TooManyRequests },
        { new TimeoutException(Secret), HttpStatusCode.GatewayTimeout },
        { new TaskCanceledException(Secret, new TimeoutException()), HttpStatusCode.GatewayTimeout },
    };

    [Theory]
    [MemberData(nameof(BeforeTheReply))]
    public async Task A_failure_before_the_first_text_gets_a_status_code_and_a_JSON_error_without_the_exception_text(
        Exception failure,
        HttpStatusCode expected)
    {
        var model = new FakeChatClient(_ => Fail(failure));
        await using var host = await ChatHost.StartAsync(model);

        using var response = await host.PostMessagesAsync(("user", "hi"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(ErrorOf(body));
        Assert.DoesNotContain("SECRET", body);
        Assert.DoesNotContain("sk-123", body);

        // The detail is for the operator.
        var logged = Assert.Single(host.Logs.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Same(failure, logged.Exception);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_reply_past_the_input_limit_stops_the_model_and_the_hook_is_told(bool withHook)
    {
        var chunk = new string('x', OpenUILimits.Default.MaxInputChars / 4);
        var chunksTaken = 0;
        OpenUIReplyCompletedContext? seen = null;
        var model = new FakeChatClient(_ => Endless());
        await using var host = await ChatHost.StartAsync(model, options =>
        {
            if (withHook)
            {
                options.OnReplyCompleted = context =>
                {
                    seen = context;
                    return Task.CompletedTask;
                };
            }
        });

        using var response = await host.PostMessagesAsync(("user", "hi"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(5, chunksTaken);
        Assert.DoesNotContain("finish_reason\":\"stop", body);
        Assert.Contains(host.Logs.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("characters"));
        if (withHook)
        {
            Assert.Equal(OpenUIReplyOutcome.TooLong, seen!.Outcome);
            Assert.Equal(4 * chunk.Length, seen.Reply.Length);
        }

        async IAsyncEnumerable<ChatResponseUpdate> Endless()
        {
            while (true)
            {
                await Task.Yield();
                chunksTaken++;
                yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
            }
        }
    }

    [Fact]
    public async Task A_model_that_never_answers_gets_a_504_when_the_time_limit_passes()
    {
        var model = new FakeChatClient(Silent);
        await using var host = await ChatHost.StartAsync(model, options => options.ReplyTimeout = TimeSpan.FromMilliseconds(100));

        using var response = await host.PostMessagesAsync(("user", "hi"));

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
    }

    [Fact]
    public async Task A_reply_that_stalls_ends_when_the_time_limit_passes_and_the_hook_is_told()
    {
        OpenUIReplyCompletedContext? seen = null;
        var model = new FakeChatClient(ct => Stalls(ct));
        await using var host = await ChatHost.StartAsync(model, options =>
        {
            options.ReplyTimeout = TimeSpan.FromMilliseconds(100);
            options.OnReplyCompleted = context =>
            {
                seen = context;
                return Task.CompletedTask;
            };
        });

        using var response = await host.PostMessagesAsync(("user", "hi"));
        await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OpenUIReplyOutcome.TimedOut, seen!.Outcome);
        Assert.Equal("start", seen.Reply);

        static async IAsyncEnumerable<ChatResponseUpdate> Stalls([EnumeratorCancellation] CancellationToken ct)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "start");
            await Task.Delay(Timeout.Infinite, ct);
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(30.0 * 24 * 60 * 60)]
    public void A_time_limit_that_cannot_be_used_is_refused_when_it_is_set(double seconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpenUIChatOptions { ReplyTimeout = TimeSpan.FromSeconds(seconds) });

    [Fact]
    public async Task Running_out_of_memory_is_not_answered_as_a_model_failure()
    {
        var model = new FakeChatClient(_ => throw new OutOfMemoryException());
        await using var host = await ChatHost.StartAsync(model);

        await Assert.ThrowsAsync<OutOfMemoryException>(() => host.PostMessagesAsync(("user", "hi")));
    }

    [Fact]
    public async Task Running_out_of_memory_mid_stream_is_not_logged_as_a_model_failure()
    {
        var model = new FakeChatClient(_ => StartThenOutOfMemory());
        await using var host = await ChatHost.StartAsync(model);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var response = await host.PostMessagesAsync(("user", "hi"));
            await response.Content.ReadAsStringAsync();
        });

        Assert.DoesNotContain(host.Logs.Entries, entry => entry.Exception is OutOfMemoryException);

        static async IAsyncEnumerable<ChatResponseUpdate> StartThenOutOfMemory()
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "start");
            await Task.Yield();
            throw new OutOfMemoryException();
        }
    }

    [Fact]
    public async Task Running_out_of_memory_in_the_hook_is_not_logged_as_a_hook_failure()
    {
        await using var host = await ChatHost.StartAsync(
            FakeChatClient.Replying("root = Stack([])"),
            options => options.OnReplyCompleted = _ => throw new OutOfMemoryException());

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var response = await host.PostMessagesAsync(("user", "hi"));
            await response.Content.ReadAsStringAsync();
        });

        Assert.DoesNotContain(host.Logs.Entries, entry => entry.Exception is OutOfMemoryException);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Silent([EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct);
        yield break;
    }

    [Fact]
    public async Task A_failure_after_updates_that_carry_no_text_is_still_before_the_reply()
    {
        var model = new FakeChatClient(_ => EmptyThenFail());
        await using var host = await ChatHost.StartAsync(model);

        using var response = await host.PostMessagesAsync(("user", "hi"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        static async IAsyncEnumerable<ChatResponseUpdate> EmptyThenFail()
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "");
            await Task.Yield();
            throw new InvalidOperationException(Secret);
        }
    }

    [Fact]
    public async Task A_failure_in_the_call_itself_is_handled_like_one_in_the_stream()
    {
        var model = new FakeChatClient(_ => throw new InvalidOperationException(Secret));
        await using var host = await ChatHost.StartAsync(model);

        using var response = await host.PostMessagesAsync(("user", "hi"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("SECRET", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_failure_mid_stream_ends_the_stream_without_a_stop_chunk_and_is_logged()
    {
        var failure = new InvalidOperationException(Secret);
        var model = new FakeChatClient(_ => PartialThenFail(failure));
        await using var host = await ChatHost.StartAsync(model);

        using var response = await host.PostMessagesAsync(("user", "hi"));
        var (raw, lines) = await response.ReadNdjsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);
        var only = Assert.Single(lines);
        Assert.Equal("partial", only.GetProperty("choices")[0].GetProperty("delta").GetProperty("content").GetString());
        Assert.DoesNotContain("stop", raw);
        Assert.DoesNotContain("SECRET", raw);

        var logged = Assert.Single(host.Logs.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Same(failure, logged.Exception);
    }

    [Fact]
    public async Task A_client_that_goes_away_cancels_the_model_call()
    {
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource<OpenUIReplyOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new FakeChatClient(token => UntilCancelled(token, cancelled));
        await using var host = await ChatHost.StartAsync(
            model,
            options => options.OnReplyCompleted = context =>
            {
                finished.SetResult(context.Outcome);
                return Task.CompletedTask;
            });

        var response = await host.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, ChatHost.Route)
            {
                Content = ChatHost.Json("""{"messages":[{"role":"user","content":"hi"}]}"""),
            },
            HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        Assert.Contains("first", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(cancelled.Task.IsCompleted);

        // Dropping the response is how the test server models the browser closing the connection.
        reader.Dispose();
        response.Dispose();

        Assert.True(await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(OpenUIReplyOutcome.Aborted, await finished.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Contains(host.Logs.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains("went away", StringComparison.Ordinal));
        Assert.DoesNotContain(host.Logs.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task A_client_that_leaves_before_the_reply_starts_cancels_the_call_and_gets_no_hook_call()
    {
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var asked = new TaskCompletionSource();
        var hookCalled = false;
        var model = new FakeChatClient(token => WaitForCancel(token, asked, cancelled));
        await using var host = await ChatHost.StartAsync(
            model,
            options => options.OnReplyCompleted = _ =>
            {
                hookCalled = true;
                return Task.CompletedTask;
            });
        using var client = new CancellationTokenSource();

        var sending = host.Client.PostAsync(ChatHost.Route, ChatHost.Json("""{"messages":[{"role":"user","content":"hi"}]}"""), client.Token);
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await client.CancelAsync();

        Assert.True(await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
        await Task.Delay(200);
        Assert.False(hookCalled);
    }

    private static string ErrorOf(string body) =>
        System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("error").GetString()!;

    private static async IAsyncEnumerable<ChatResponseUpdate> Fail(Exception failure)
    {
        await Task.Yield();
        if (failure is not null)
        {
            throw failure;
        }

        yield break;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> PartialThenFail(Exception failure)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "partial");
        await Task.Yield();
        throw failure;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> UntilCancelled(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token,
        TaskCompletionSource<bool> cancelled)
    {
        // The client can go between the line being written and its flush, which ends this
        // call at the yield, so only a finally sees every way the model call ends.
        try
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "first");
            await Task.Delay(Timeout.Infinite, token);
        }
        finally
        {
            cancelled.TrySetResult(true);
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> WaitForCancel(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token,
        TaskCompletionSource asked,
        TaskCompletionSource<bool> cancelled)
    {
        asked.SetResult();
        try
        {
            await Task.Delay(Timeout.Infinite, token);
        }
        catch (OperationCanceledException)
        {
            cancelled.SetResult(true);
            throw;
        }

        yield break;
    }
}
