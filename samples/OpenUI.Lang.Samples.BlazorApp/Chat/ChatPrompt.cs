using OpenUI.Lang.Blazor.Chat;
using OpenUI.Lang.Prompting;
using OpenUI.Lang.Samples.BlazorApp.Weather;

namespace OpenUI.Lang.Samples.BlazorApp.Chat;

/// <summary>What the chat teaches beside the components, and how the sample sets up the chat package.</summary>
public static class ChatPrompt
{
    /// <summary>How to wire a choice to a Query so the first fetch has a value and a change fetches again (spec §5.1, §6.1).</summary>
    public const string QueryRule = "When a choice picks what a Query fetches, declare its $variable with a real value as the default, such as a field id, and write that $variable directly in the Query's args. A Query runs again only when a $variable written in its args changes.";

    /// <summary>The weather tools and the chat's rules. The package composes the system prompt from these and the registered components.</summary>
    public static PromptOptions Options { get; } = new()
    {
        Tools = WeatherTools.Descriptors,
        AdditionalRules =
        [
            "Reply only with a program; explain things in TextContent components.",
            "End a reply with one or two Buttons that use @ToAssistant to offer the likely next questions.",
            QueryRule,
        ],
    };

    /// <summary>The sample's chat options: its prompt, with the wiring check on.</summary>
    public static void Configure(OpenUIBlazorChatOptions options)
    {
        options.PromptOptions = Options;
        options.CheckWiring = true;
    }
}
