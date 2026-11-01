using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace OpenUI.Lang.Samples.ChatApi.Tests;

/// <summary>The real ChatApi, its Program.cs and all, with the fake provider and in the given environment.</summary>
internal sealed class ChatApiFactory(
    string environment = "Development",
    Action<IServiceCollection>? services = null,
    Dictionary<string, string>? settings = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("OPENUI_LLM_PROVIDER", "fake");
        foreach (var (name, value) in settings ?? [])
        {
            builder.UseSetting(name, value);
        }

        if (services is not null)
        {
            builder.ConfigureTestServices(services);
        }
    }
}
