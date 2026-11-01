using Microsoft.Extensions.Configuration;
using OpenUI.Lang.Samples.Llm;

namespace OpenUI.Lang.Samples.ChatApi.Tests;

public sealed class ProviderSettingsTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value)).Build();

    [Fact]
    public void With_nothing_set_the_provider_is_the_fake_one() =>
        Assert.Equal(LlmSettings.FakeProvider, LlmSettings.Read(Config()).Provider);

    [Fact]
    public void The_openai_provider_is_only_used_when_asked_for()
    {
        var settings = LlmSettings.Read(Config(
            ("OPENUI_LLM_PROVIDER", "openai"),
            ("OPENUI_LLM_BASE_URL", "https://api.openai.com/v1"),
            ("OPENUI_LLM_MODEL", "gpt-4.1-mini")));

        Assert.Equal(LlmSettings.OpenAiProvider, settings.Provider);
        Assert.Equal("gpt-4.1-mini", settings.OpenAi?.Model);
    }

    [Fact]
    public void An_unknown_provider_fails_at_startup() =>
        Assert.Throws<InvalidOperationException>(() => LlmSettings.Read(Config(("OPENUI_LLM_PROVIDER", "nope"))));
}
