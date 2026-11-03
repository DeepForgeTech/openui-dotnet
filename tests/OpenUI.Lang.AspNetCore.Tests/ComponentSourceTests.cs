using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenUI.Lang.Library;
using OpenUI.Lang.Prompting;

namespace OpenUI.Lang.AspNetCore.Tests;

/// <summary>The endpoint's prompt and validator, composed from whatever sources the container holds.</summary>
public sealed class ComponentSourceTests
{
    private static ComponentSpec Component(string name, string prop = "text", TypeShape? type = null) =>
        new(name, $"A {name}.", [new PropSpec(prop, type ?? TypeShape.String, Required: true)]);

    private static IComponentSource Source(string name, string id = "one") =>
        ComponentSources.FromSpec(OpenUILibrary.CreateBuilder(id).Add(Component(name)).Root(name).Build().ToSpec());

    private static async Task<WebApplication> StartAsync(FakeChatClient model, Action<IServiceCollection> register)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IChatClient>(model);
        register(builder.Services);
        builder.Services.AddOpenUI();
        var app = builder.Build();
        app.MapOpenUIChat(ChatHost.Route);
        await app.StartAsync();
        return app;
    }

    private static async Task<string> SystemMessageAsync(Action<IServiceCollection> register)
    {
        var model = FakeChatClient.Replying("ok");
        await using var app = await StartAsync(model, register);

        using var response = await app.GetTestClient().PostAsync(ChatHost.Route, ChatHost.Json("""{"messages":[{"role":"user","content":"hi"}]}"""));
        await response.Content.ReadAsStringAsync();

        return model.Calls[0][0].Text;
    }

    [Fact]
    public async Task The_prompt_comes_from_the_registered_source()
    {
        var source = Source("Alpha");

        var prompt = await SystemMessageAsync(services => services.AddSingleton(source));

        Assert.Equal(SystemPrompt.Generate(source.Spec), prompt);
        Assert.Contains("Alpha(text: string)", prompt);
    }

    [Fact]
    public async Task Swapping_the_source_changes_the_prompt()
    {
        var prompt = await SystemMessageAsync(services => services.AddSingleton(Source("Beta")));

        Assert.Contains("Beta(text: string)", prompt);
        Assert.DoesNotContain("Alpha(", prompt);
    }

    [Fact]
    public async Task Several_sources_are_taught_together_and_the_later_one_wins_a_clash()
    {
        var replacement = ComponentSources.FromSpec(OpenUILibrary.CreateBuilder("two").Add(Component("Alpha", "count", TypeShape.Number)).Add(Component("Gamma")).Build().ToSpec());

        var prompt = await SystemMessageAsync(services => services.AddSingleton(Source("Alpha")).AddSingleton(replacement));

        Assert.Contains("Alpha(count: number)", prompt);
        Assert.DoesNotContain("Alpha(text: string)", prompt);
        Assert.Contains("Gamma(text: string)", prompt);
    }

    [Fact]
    public async Task Prompt_options_and_chat_options_still_apply_and_the_options_run_once()
    {
        var model = FakeChatClient.Replying("ok");
        var runs = 0;
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IChatClient>(model);
        builder.Services.AddSingleton(Source("Alpha"));
        builder.Services.AddOpenUI(new PromptOptions { AdditionalRules = ["Answer in French."] }, _ => runs++);
        await using var app = builder.Build();
        app.MapOpenUIChat(ChatHost.Route);
        app.MapOpenUIChat(ChatHost.Route + "/again");
        await app.StartAsync();

        using var response = await app.GetTestClient().PostAsync(ChatHost.Route, ChatHost.Json("""{"messages":[{"role":"user","content":"hi"}]}"""));
        await response.Content.ReadAsStringAsync();

        Assert.Contains("Answer in French.", model.Calls[0][0].Text);
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task With_no_source_mapping_the_endpoint_says_what_to_register()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton<IChatClient>(FakeChatClient.Replying("ok"));
        builder.Services.AddOpenUI();
        await using var app = builder.Build();

        var error = Assert.Throws<InvalidOperationException>(() => app.MapOpenUIChat(ChatHost.Route));

        Assert.Contains("IComponentSource", error.Message);
    }
}
