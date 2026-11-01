using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OpenUI.Lang.Samples.ChatApi.Tests;

/// <summary>A site that rebinds its name to this machine, or a page that posts to it, gets nothing.</summary>
public sealed class LocalOnlyTests
{
    private const string Chat = """{"messages":[{"role":"user","content":"hi"}]}""";

    private static HttpClient ClientFor(ChatApiFactory api, string host) =>
        api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://{host}") });

    private static async Task<HttpStatusCode> PostFromAsync(HttpClient client, string path, string? origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(Chat, Encoding.UTF8, "application/json") };
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [Theory]
    [InlineData("localhost", HttpStatusCode.OK)]
    [InlineData("127.0.0.1", HttpStatusCode.OK)]
    [InlineData("[::1]", HttpStatusCode.OK)]
    [InlineData("evil.example", HttpStatusCode.BadRequest)]
    [InlineData("localhost.evil.example", HttpStatusCode.BadRequest)]
    public async Task Only_local_host_names_are_answered(string host, HttpStatusCode expected)
    {
        await using var api = new ChatApiFactory();

        using var response = await ClientFor(api, host).GetAsync("/api/locations");

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.OK)]
    [InlineData("http://localhost:5173", HttpStatusCode.OK)]
    [InlineData("http://127.0.0.1:5173", HttpStatusCode.OK)]
    [InlineData("https://evil.example", HttpStatusCode.Forbidden)]
    [InlineData("http://localhost.evil.example", HttpStatusCode.Forbidden)]
    [InlineData("null", HttpStatusCode.Forbidden)]
    public async Task A_post_from_another_site_never_reaches_the_model(string? origin, HttpStatusCode expected)
    {
        await using var api = new ChatApiFactory();

        Assert.Equal(expected, await PostFromAsync(api.CreateClient(), "/api/chat", origin));
    }

    [Fact]
    public async Task A_read_is_not_refused_for_its_origin()
    {
        await using var api = new ChatApiFactory();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/locations");
        request.Headers.Add("Origin", "https://evil.example");

        using var response = await api.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_last_reply_is_only_served_in_Development()
    {
        await using var production = new ChatApiFactory("Production");
        var client = production.CreateClient();
        Assert.Equal(HttpStatusCode.OK, await PostFromAsync(client, "/api/chat", origin: null));

        using var response = await client.GetAsync("/api/debug/last");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }
}
