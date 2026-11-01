using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenUI.Lang.Samples.Llm;

namespace OpenUI.Lang.Samples.ChatApi.Tests;

/// <summary>The sample's own Program.cs, with its routes, services, spec file and fake provider.</summary>
public sealed class ChatApiAppTests
{
    private static StringContent ChatBody() =>
        new("""{"messages":[{"role":"user","content":"hi"}]}""", Encoding.UTF8, "application/json");

    private static async Task<string> ChatAsync(HttpClient client)
    {
        using var response = await client.PostAsync("/api/chat", ChatBody());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        return string.Concat(body
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonNode.Parse(line)!["choices"]![0]!["delta"]!["content"]?.GetValue<string>()));
    }

    private static async Task<JsonNode> LastReplyAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/debug/last");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    private static ChatApiFactory Replying(string reply) =>
        new(services: services => services.AddSingleton<IChatClient>(new FakeChatClient(_ => reply)));

    [Theory]
    [InlineData(LlmSettings.FakeProvider, LlmSettings.FakeNotice)]
    [InlineData(LlmSettings.OpenAiProvider, null)]
    public async Task Only_the_fake_provider_gives_the_browser_a_notice(string provider, string? notice)
    {
        await using var api = new ChatApiFactory(settings: new()
        {
            ["OPENUI_LLM_PROVIDER"] = provider,
            ["OPENUI_LLM_BASE_URL"] = "http://localhost:1/v1",
            ["OPENUI_LLM_MODEL"] = "test-model",
        });

        var model = JsonNode.Parse(await api.CreateClient().GetStringAsync("/api/model"))!;

        Assert.Equal(notice, model["notice"]?.GetValue<string>());
    }

    [Fact]
    public async Task The_fake_provider_streams_its_canned_reply_and_the_report_finds_a_valid_program()
    {
        await using var api = new ChatApiFactory();
        var client = api.CreateClient();

        var text = await ChatAsync(client);
        var report = await LastReplyAsync(client);

        Assert.Equal(FakeReply.Text, text);
        Assert.Equal(FakeReply.Text, report["reply"]!.GetValue<string>());
        Assert.Equal("Card", report["root"]!.GetValue<string>());
        Assert.Equal(9, report["statements"]!.GetValue<int>());
        Assert.Empty(report["errors"]!.AsArray());
    }

    [Fact]
    public async Task The_report_says_what_is_wrong_with_a_bad_reply()
    {
        await using var api = Replying("root = Stack([x])\nx = Nope(\"a\")");
        var client = api.CreateClient();

        await ChatAsync(client);
        var report = await LastReplyAsync(client);

        var error = Assert.Single(report["errors"]!.AsArray());
        Assert.False(string.IsNullOrEmpty(error!["code"]!.GetValue<string>()));
    }

    [Fact]
    public async Task There_is_no_report_until_a_reply_has_finished()
    {
        await using var api = new ChatApiFactory();

        using var response = await api.CreateClient().GetAsync("/api/debug/last");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_model_gets_the_prompt_upstream_generated_from_the_default_spec_ahead_of_the_conversation()
    {
        IReadOnlyList<ChatMessage>? seen = null;
        await using var api = new ChatApiFactory(services: services => services.AddSingleton<IChatClient>(new FakeChatClient(messages =>
        {
            seen = messages;
            return "ok";
        })));

        await ChatAsync(api.CreateClient());

        var prompt = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "generated", "prompt.txt")).TrimEnd('\n');
        Assert.Equal([ChatRole.System, ChatRole.User], seen!.Select(message => message.Role));
        Assert.Equal(prompt, seen![0].Text);
    }

    [Fact]
    public async Task The_weather_routes_are_served_from_the_real_service()
    {
        await using var api = new ChatApiFactory();
        var client = api.CreateClient();

        using var locations = await client.GetAsync("/api/locations");
        using var jobs = await client.GetAsync("/api/spray-jobs");

        Assert.Equal(HttpStatusCode.OK, locations.StatusCode);
        Assert.NotEmpty(JsonNode.Parse(await locations.Content.ReadAsStringAsync())!["locations"]!.AsArray());
        Assert.Equal(HttpStatusCode.OK, jobs.StatusCode);
    }

    [Fact]
    public async Task A_reply_file_replaces_the_canned_reply_and_is_read_for_each_one()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "root = Text(\"first\")");
            await using var api = new ChatApiFactory(settings: new() { ["OPENUI_FAKE_REPLY_FILE"] = file });
            var client = api.CreateClient();

            Assert.Equal("root = Text(\"first\")", await ChatAsync(client));

            File.WriteAllText(file, "root = Text(\"second\")");
            Assert.Equal("root = Text(\"second\")", await ChatAsync(client));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void The_defaults_are_the_ones_the_README_gives()
    {
        var settings = SampleSettings.Read(new ConfigurationBuilder().Build(), Path.Combine(Path.GetTempPath(), "root"));

        Assert.Equal(5280, settings.Port);
        Assert.Equal(LlmSettings.FakeProvider, settings.Llm.Provider);
        Assert.Null(settings.FakeReplyFile);
    }
}
