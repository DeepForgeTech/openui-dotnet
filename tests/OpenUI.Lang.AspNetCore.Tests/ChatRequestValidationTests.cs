using System.Net;
using System.Text;

namespace OpenUI.Lang.AspNetCore.Tests;

/// <summary>The request body is untrusted: bad ones get a 400 with a fixed message, and never reach the model.</summary>
public sealed class ChatRequestValidationTests
{
    public static TheoryData<string, string> BadBodies() => new()
    {
        { "not json", "{nope" },
        { "empty body", "" },
        { "truncated json", """{"messages":[{"role":"user","content":"hi"}""" },
        { "array body", "[]" },
        { "null body", "null" },
        { "no messages", "{}" },
        { "empty messages", """{"messages":[]}""" },
        { "messages not an array", """{"messages":{"role":"user"}}""" },
        { "message not an object", """{"messages":["hi"]}""" },
        { "role missing", """{"messages":[{"content":"hi"}]}""" },
        { "role not a string", """{"messages":[{"role":1,"content":"hi"}]}""" },
        { "unknown role", """{"messages":[{"role":"tool","content":"hi"}]}""" },
        { "unknown role among valid messages", """{"messages":[{"role":"user","content":"hi"},{"role":"function","content":"x"}]}""" },
        { "tool role among valid messages", """{"messages":[{"role":"tool","content":"x"},{"role":"user","content":"hi"}]}""" },
        { "content a number", """{"messages":[{"role":"user","content":5}]}""" },
        { "content an object", """{"messages":[{"role":"user","content":{"text":"hi"}}]}""" },
        { "content a bool", """{"messages":[{"role":"user","content":true}]}""" },
        { "part not an object", """{"messages":[{"role":"user","content":["hi"]}]}""" },
        { "part without a type", """{"messages":[{"role":"user","content":[{"text":"hi"}]}]}""" },
        { "text part without text", """{"messages":[{"role":"user","content":[{"type":"text"}]}]}""" },
        { "text part with non-string text", """{"messages":[{"role":"user","content":[{"type":"text","text":3}]}]}""" },
        { "only system messages", """{"messages":[{"role":"system","content":"x"}]}""" },
        { "bad content in a dropped message", """{"messages":[{"role":"system","content":7},{"role":"user","content":"hi"}]}""" },
        { "lone surrogate in the content", """{"messages":[{"role":"user","content":"\uD800"}]}""" },
        { "lone surrogate in the role", """{"messages":[{"role":"\uDC00","content":"hi"}]}""" },
        { "lone surrogate in a text part", """{"messages":[{"role":"user","content":[{"type":"text","text":"\uD800"}]}]}""" },
    };

    [Fact]
    public async Task Bytes_that_are_not_UTF8_inside_a_string_get_the_same_fixed_answer_as_a_lone_surrogate()
    {
        await using var host = await ChatHost.StartAsync(FakeChatClient.Replying("never"));
        // Latin-1 writes this character as the byte C3, which the next byte, 28, cannot follow in UTF-8.
        var body = Encoding.Latin1.GetBytes("""{"messages":[{"role":"user","content":"Ã("}]}""");
        using var bytes = new ByteArrayContent(body);
        bytes.Headers.ContentType = new("application/json");

        using var response = await host.Client.PostAsync(ChatHost.Route, bytes);
        using var surrogate = await host.PostAsync("""{"messages":[{"role":"user","content":"\uD800"}]}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(await surrogate.ReadErrorAsync(), await response.ReadErrorAsync());
        Assert.Empty(host.Model.Calls);
    }

    [Theory]
    [MemberData(nameof(BadBodies))]
    public async Task A_bad_body_gets_400_with_an_error_and_never_reaches_the_model(string name, string body)
    {
        Assert.NotEmpty(name);
        await using var host = await ChatHost.StartAsync(FakeChatClient.Replying("never"));

        using var response = await host.PostAsync(body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty(await response.ReadErrorAsync());
        Assert.Empty(host.Model.Calls);
    }

    [Fact]
    public async Task Error_messages_do_not_echo_what_the_client_sent()
    {
        await using var host = await ChatHost.StartAsync(FakeChatClient.Replying("never"));

        using var response = await host.PostAsync("""{"messages":[{"role":"SECRET_ROLE","content":"SECRET_CONTENT"}]}""");

        var error = await response.ReadErrorAsync();
        Assert.DoesNotContain("SECRET", error);
    }

    [Fact]
    public async Task A_body_over_the_limit_gets_413_and_one_at_the_limit_is_served()
    {
        const string body = """{"messages":[{"role":"user","content":"hi"}]}""";
        var size = Encoding.UTF8.GetByteCount(body);

        await using var atLimit = await ChatHost.StartAsync(FakeChatClient.Replying("ok"), options => options.MaxRequestBodyBytes = size);
        using var served = await atLimit.PostAsync(body);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);

        await using var overLimit = await ChatHost.StartAsync(FakeChatClient.Replying("ok"), options => options.MaxRequestBodyBytes = size - 1);
        using var rejected = await overLimit.PostAsync(body);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rejected.StatusCode);
        Assert.Contains($"{size - 1} bytes", await rejected.ReadErrorAsync());
        Assert.Empty(overLimit.Model.Calls);
    }

    [Fact]
    public async Task The_limit_holds_for_a_body_that_does_not_declare_its_length()
    {
        await using var host = await ChatHost.StartAsync(FakeChatClient.Replying("ok"), options => options.MaxRequestBodyBytes = 64);
        var padded = """{"messages":[{"role":"user","content":"hi"}],"pad":"%s"}""".Replace("%s", new string('x', 200));

        using var content = new UnknownLengthContent(Encoding.UTF8.GetBytes(padded));
        content.Headers.ContentType = new("application/json");
        Assert.Null(content.Headers.ContentLength);
        using var response = await host.Client.PostAsync(ChatHost.Route, content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.NotEmpty(await response.ReadErrorAsync());
        Assert.Empty(host.Model.Calls);
    }

    [Fact]
    public async Task More_messages_than_the_limit_gets_400_and_the_limit_itself_is_served()
    {
        await using var host = await ChatHost.StartAsync(FakeChatClient.Replying("ok"), options => options.MaxMessages = 3);

        using var served = await host.PostMessagesAsync(("user", "1"), ("assistant", "2"), ("user", "3"));
        using var rejected = await host.PostMessagesAsync(("user", "1"), ("assistant", "2"), ("user", "3"), ("assistant", "4"));

        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains("3", await rejected.ReadErrorAsync());
        Assert.Single(host.Model.Calls);
    }

    [Fact]
    public async Task Dropped_messages_count_toward_the_message_limit()
    {
        await using var host = await ChatHost.StartAsync(FakeChatClient.Replying("ok"), options => options.MaxMessages = 2);

        using var response = await host.PostMessagesAsync(("system", "1"), ("system", "2"), ("user", "3"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    public async Task A_body_that_is_not_declared_as_JSON_gets_415(string mediaType)
    {
        await using var host = await ChatHost.StartAsync(FakeChatClient.Replying("never"));

        // A cross-site form post can only send these types, and would skip the CORS preflight.
        using var response = await host.Client.PostAsync(
            ChatHost.Route,
            ChatHost.Json("""{"messages":[{"role":"user","content":"hi"}]}""", mediaType));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.NotEmpty(await response.ReadErrorAsync());
        Assert.Empty(host.Model.Calls);
    }

    [Fact]
    public async Task A_rejected_request_does_not_run_the_reply_hook()
    {
        var called = false;
        await using var host = await ChatHost.StartAsync(
            FakeChatClient.Replying("never"),
            options => options.OnReplyCompleted = _ =>
            {
                called = true;
                return Task.CompletedTask;
            });

        using var response = await host.PostAsync("{}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(called);
    }
}

/// <summary>A body with no Content-Length, as with chunked transfer.</summary>
internal sealed class UnknownLengthContent(byte[] bytes) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
        stream.WriteAsync(bytes).AsTask();

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}
