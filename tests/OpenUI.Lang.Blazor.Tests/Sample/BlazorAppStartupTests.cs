using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using OpenUI.Lang.Samples.BlazorApp.Chat;

namespace OpenUI.Lang.Blazor.Tests.Sample;

/// <summary>The sample's own Program.cs and appsettings.json.</summary>
public sealed class BlazorAppStartupTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("openui-blazor-startup-").FullName;

    public void Dispose() => Directory.Delete(_data, recursive: true);

    // Any public type of the app names its entry point. This test project references two apps, so
    // "Program" alone wouldn't.
    private sealed class BlazorApp(string data) : WebApplicationFactory<ConversationStore>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("OPENUI_BLAZOR_DATA", data);
        }
    }

    [Theory]
    [InlineData("localhost", HttpStatusCode.OK)]
    [InlineData("127.0.0.1", HttpStatusCode.OK)]
    [InlineData("evil.example", HttpStatusCode.BadRequest)]
    public async Task It_starts_and_only_local_host_names_are_answered(string host, HttpStatusCode expected)
    {
        await using var app = new BlazorApp(_data);
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://{host}") });

        using var response = await client.GetAsync("/");

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("http://localhost:5380", HttpStatusCode.OK)]
    [InlineData("https://evil.example", HttpStatusCode.Forbidden)]
    public async Task A_circuit_cannot_be_opened_from_another_site(string origin, HttpStatusCode expected)
    {
        await using var app = new BlazorApp(_data);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_blazor/negotiate?negotiateVersion=1");
        request.Headers.Add("Origin", origin);

        using var response = await app.CreateClient().SendAsync(request);

        Assert.Equal(expected, response.StatusCode);
    }
}
