using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OpenUI.Lang.Samples.Llm;

/// <summary>Where an OpenAI-compatible endpoint lives and which model to ask for.</summary>
public sealed record OpenAiSettings(Uri BaseUrl, string Model, string? ApiKey)
{
    // The default record ToString would print the key into logs.
    public override string ToString() => $"OpenAiSettings {{ BaseUrl = {BaseUrl}, Model = {Model} }}";
}

/// <summary>Which model provider to use, read from environment variables at startup.</summary>
public sealed record LlmSettings(string Provider, OpenAiSettings? OpenAi)
{
    public const string OpenAiProvider = "openai";
    public const string FakeProvider = "fake";

    /// <summary>What an app shows by its prompt box on the fake provider, in one line.</summary>
    public const string FakeNotice = "Fake model: nothing you type here is read, and every reply is canned. For a real model, restart with OPENUI_LLM_PROVIDER=openai (see samples/README.md).";

    /// <summary>The prompt box's placeholder on the fake provider.</summary>
    public const string FakePlaceholder = "Not read: replies are canned";

    /// <summary>What goes with each reply from the fake provider.</summary>
    public const string CannedReplyNotice = "Canned reply: the fake model did not read your message.";

    /// <summary>True when replies are canned and no model reads the messages.</summary>
    public bool IsFake => Provider == FakeProvider;

    /// <summary>
    /// Reads <c>OPENUI_LLM_PROVIDER</c> and, for openai, the <c>OPENUI_LLM_*</c> endpoint.
    /// With none of them set the provider is the fake one, so starting a sample never spends anything.
    /// </summary>
    /// <exception cref="InvalidOperationException">A setting is missing or has a value the sample does not know.</exception>
    public static LlmSettings Read(IConfiguration config)
    {
        var provider = config["OPENUI_LLM_PROVIDER"] is { Length: > 0 } name ? name : FakeProvider;
        if (provider is not (OpenAiProvider or FakeProvider))
        {
            throw new InvalidOperationException($"OPENUI_LLM_PROVIDER must be {OpenAiProvider} or {FakeProvider}, not '{provider}'.");
        }

        return new LlmSettings(
            provider,
            provider == OpenAiProvider ? ReadOpenAi(config) : null);
    }

    private static OpenAiSettings ReadOpenAi(IConfiguration config)
    {
        var baseUrl = config["OPENUI_LLM_BASE_URL"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("OPENUI_LLM_BASE_URL must be an http(s) URL, such as https://api.openai.com/v1.");
        }

        var model = config["OPENUI_LLM_MODEL"] is { Length: > 0 } name
            ? name
            : throw new InvalidOperationException("OPENUI_LLM_MODEL must name the model to call.");

        return new OpenAiSettings(uri, model, config["OPENUI_LLM_API_KEY"] is { Length: > 0 } key ? key : null);
    }
}

/// <summary>Registers the configured provider as the app's <see cref="IChatClient"/>.</summary>
public static class ChatModelRegistration
{
    /// <param name="services">The app's services.</param>
    /// <param name="settings">The provider to register.</param>
    /// <param name="fakeReply">What the fake provider answers, since the canned reply depends on the app's component library.</param>
    public static IServiceCollection AddChatModel(this IServiceCollection services, LlmSettings settings, Func<IReadOnlyList<ChatMessage>, string> fakeReply)
    {
        if (settings.OpenAi is { } openAi)
        {
            services.AddHttpClient<IChatClient, OpenAiChatClient>(http => new OpenAiChatClient(http, openAi));
        }
        else
        {
            services.AddSingleton<IChatClient>(new FakeChatClient(fakeReply));
        }

        return services;
    }
}
