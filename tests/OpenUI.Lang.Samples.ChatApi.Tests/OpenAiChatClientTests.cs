using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenUI.Lang.Samples.Llm;

namespace OpenUI.Lang.Samples.ChatApi.Tests;

/// <summary>The OpenAI provider against a scripted endpoint, and behind the real chat route.</summary>
public sealed class OpenAiChatClientTests
{
    private static readonly OpenAiSettings Settings = new(new Uri("http://model.test/v1"), "test-model", "secret");

    private static StringContent ChatBody() =>
        new("""{"messages":[{"role":"user","content":"hi"}]}""", Encoding.UTF8, "application/json");

    private static HttpResponseMessage Answer(HttpStatusCode status, string body, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    private static ChatApiFactory ChatApiWith(HttpResponseMessage answer) =>
        new(services: services => services.AddSingleton<IChatClient>(new OpenAiChatClient(new HttpClient(new Scripted(_ => answer)), Settings)));

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, 429, "The model is rate limited. Try again shortly.")]
    [InlineData(HttpStatusCode.InternalServerError, 502, "The model request failed.")]
    public async Task An_endpoint_failure_reaches_the_browser_as_its_status_with_a_fixed_message(HttpStatusCode upstream, int status, string message)
    {
        await using var api = ChatApiWith(Answer(upstream, """{"error":{"message":"secret detail from the provider"}}"""));

        using var response = await api.CreateClient().PostAsync("/api/chat", ChatBody());

        Assert.Equal(status, (int)response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(message, body["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_failure_keeps_the_status_and_the_provider_s_own_sentence_for_the_log()
    {
        using var client = new OpenAiChatClient(new HttpClient(new Scripted(_ => Answer(HttpStatusCode.TooManyRequests, """{"error":{"message":"Slow down."}}"""))), Settings);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetResponseAsync("hi"));

        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.Equal("Slow down.", error.Message);
    }

    [Fact]
    public async Task The_reply_is_the_text_of_the_event_stream_and_the_request_carries_the_whole_conversation()
    {
        string? sent = null;
        string? authorization = null;
        const string Stream = """
            event: ignored

            data: {"choices":[{"delta":{"role":"assistant","content":"Hel"}}]}

            data: not json

            data: {"choices":[{"delta":{"content":"lo"}}]}

            data: {"choices":[{"delta":{},"finish_reason":"stop"}]}

            data: [DONE]

            data: {"choices":[{"delta":{"content":"after the end"}}]}
            """;
        using var client = new OpenAiChatClient(
            new HttpClient(new Scripted(request =>
            {
                sent = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                authorization = request.Headers.Authorization?.ToString();
                return Answer(HttpStatusCode.OK, Stream, "text/event-stream");
            })),
            Settings);

        var reply = await client.GetResponseAsync([new(ChatRole.System, "Be brief."), new(ChatRole.User, "hi")]);

        Assert.Equal("Hello", reply.Text);
        Assert.Equal("Bearer secret", authorization);
        var body = JsonNode.Parse(sent!)!;
        Assert.Equal("test-model", body["model"]!.GetValue<string>());
        Assert.True(body["stream"]!.GetValue<bool>());
        Assert.Equal("""[{"role":"system","content":"Be brief."},{"role":"user","content":"hi"}]""", body["messages"]!.ToJsonString());
    }

    private sealed class Scripted(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }
}
