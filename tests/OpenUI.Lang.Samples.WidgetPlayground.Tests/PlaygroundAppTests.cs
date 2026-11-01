using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OpenUI.Lang.Samples.WidgetPlayground.Tests;

/// <summary>The sample's own Program.cs.</summary>
public sealed class PlaygroundAppTests
{
    private sealed class Playground : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Production");
    }

    [Theory]
    [InlineData("localhost", HttpStatusCode.OK)]
    [InlineData("127.0.0.1", HttpStatusCode.OK)]
    [InlineData("evil.example", HttpStatusCode.BadRequest)]
    public async Task Only_local_host_names_are_answered(string host, HttpStatusCode expected)
    {
        await using var app = new Playground();
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://{host}") });

        using var response = await client.GetAsync("/");

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("http://localhost:5290", HttpStatusCode.OK)]
    [InlineData("https://evil.example", HttpStatusCode.Forbidden)]
    public async Task A_circuit_cannot_be_opened_from_another_site(string origin, HttpStatusCode expected)
    {
        await using var app = new Playground();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_blazor/negotiate?negotiateVersion=1");
        request.Headers.Add("Origin", origin);

        using var response = await app.CreateClient().SendAsync(request);

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("/_framework/blazor.web.js")]
    public async Task The_script_that_makes_the_page_interactive_is_served_outside_Development(string path)
    {
        await using var app = new Playground();

        using var response = await app.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
