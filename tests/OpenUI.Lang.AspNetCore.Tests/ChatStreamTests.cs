using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;

namespace OpenUI.Lang.AspNetCore.Tests;

/// <summary>The response is what react-headless's <c>openAIReadableStreamAdapter</c> reads.</summary>
public sealed class ChatStreamTests
{
    [Fact]
    public async Task Streams_one_chunk_object_per_line_with_role_first_and_stop_last()
    {
        await using var host = await ChatHost.StartAsync(FakeChatClient.Replying("Hello, ", "world"));

        using var response = await host.PostMessagesAsync(("user", "hi"));
        var (raw, lines) = await response.ReadNdjsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);
        Assert.EndsWith("\n", raw);
        Assert.DoesNotContain("data:", raw);
        Assert.DoesNotContain("[DONE]", raw);
        Assert.Equal(3, lines.Count);

        Assert.All(lines, line =>
        {
            Assert.Equal("chat.completion.chunk", line.GetProperty("object").GetString());
            Assert.Equal("test-model", line.GetProperty("model").GetString());
            Assert.True(line.GetProperty("created").GetInt64() > 0);
            var choice = Assert.Single(line.GetProperty("choices").EnumerateArray());
            Assert.Equal(0, choice.GetProperty("index").GetInt32());
        });
        Assert.Single(lines.Select(line => line.GetProperty("id").GetString()).Distinct());

        var deltas = lines.Select(line => line.GetProperty("choices")[0]).ToList();
        Assert.Equal("assistant", deltas[0].GetProperty("delta").GetProperty("role").GetString());
        Assert.Equal("Hello, ", deltas[0].GetProperty("delta").GetProperty("content").GetString());
        Assert.False(deltas[1].GetProperty("delta").TryGetProperty("role", out _));
        Assert.Equal("world", deltas[1].GetProperty("delta").GetProperty("content").GetString());
        Assert.All(deltas.Take(2), choice => Assert.Equal(JsonValueKind.Null, choice.GetProperty("finish_reason").ValueKind));

        Assert.Empty(deltas[2].GetProperty("delta").EnumerateObject());
        Assert.Equal("stop", deltas[2].GetProperty("finish_reason").GetString());
    }

    [Fact]
    public async Task Skips_updates_that_carry_no_text()
    {
        var model = new FakeChatClient(_ => Updates());
        await using var host = await ChatHost.StartAsync(model);

        using var response = await host.PostMessagesAsync(("user", "hi"));
        var (_, lines) = await response.ReadNdjsonAsync();

        Assert.Equal(2, lines.Count);
        Assert.Equal("only text", lines[0].GetProperty("choices")[0].GetProperty("delta").GetProperty("content").GetString());
        Assert.Equal("stop", lines[1].GetProperty("choices")[0].GetProperty("finish_reason").GetString());

        static async IAsyncEnumerable<ChatResponseUpdate> Updates()
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "");
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(new UsageDetails { InputTokenCount = 3 })]);
            yield return new ChatResponseUpdate(ChatRole.Assistant, "only text");
        }
    }

    [Fact]
    public async Task A_reply_with_no_text_still_ends_with_a_stop_chunk()
    {
        await using var host = await ChatHost.StartAsync(FakeChatClient.Replying());

        using var response = await host.PostMessagesAsync(("user", "hi"));
        var (_, lines) = await response.ReadNdjsonAsync();

        var line = Assert.Single(lines);
        Assert.Equal("stop", line.GetProperty("choices")[0].GetProperty("finish_reason").GetString());
    }

    [Fact]
    public async Task Text_survives_quotes_newlines_and_non_ascii()
    {
        const string text = "She said \"hi\"\n\tà la <b>carte</b> ☃ 😀 \\";
        await using var host = await ChatHost.StartAsync(FakeChatClient.Replying(text));

        using var response = await host.PostMessagesAsync(("user", "hi"));
        var (raw, lines) = await response.ReadNdjsonAsync();

        Assert.Equal(text, lines[0].GetProperty("choices")[0].GetProperty("delta").GetProperty("content").GetString());
        Assert.Equal(2, raw.Count(character => character == '\n'));
    }

    [Fact]
    public async Task Each_chunk_is_flushed_as_it_is_produced()
    {
        var release = new TaskCompletionSource();
        var model = new FakeChatClient(_ => Slow());
        await using var host = await ChatHost.StartAsync(model);

        using var response = await host.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, ChatHost.Route)
            {
                Content = ChatHost.Json("""{"messages":[{"role":"user","content":"hi"}]}"""),
            },
            HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        var first = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));

        // The model has not finished, yet the first line is already here.
        Assert.False(release.Task.IsCompleted);
        Assert.Contains("\"first\"", first);
        release.SetResult();
        Assert.Contains("\"second\"", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));

        async IAsyncEnumerable<ChatResponseUpdate> Slow()
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "first");
            await release.Task;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "second");
        }
    }

    [Fact]
    public async Task Only_post_is_mapped()
    {
        await using var host = await ChatHost.StartAsync(FakeChatClient.Replying("x"));

        using var response = await host.Client.GetAsync(ChatHost.Route);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Empty(host.Model.Calls);
    }

    [Fact]
    public async Task The_returned_builder_applies_conventions_to_the_endpoint()
    {
        await using var host = await ChatHost.StartAsync(
            FakeChatClient.Replying("x"),
            conventions: builder => builder.Add(endpoint => endpoint.Metadata.Add(new HostAttribute("elsewhere.example"))));

        using var response = await host.PostMessagesAsync(("user", "hi"));

        // The test client's host is localhost, so a host constraint added through the builder rejects it.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(host.Model.Calls);
    }
}
