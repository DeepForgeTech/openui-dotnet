using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenUI.Lang.Blazor;
using OpenUI.Lang.Prompting;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Samples.BlazorApp.Weather;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Samples.BlazorApp.Chat;

/// <summary>Turns a conversation into what the model reads, by the reference format of prompt.md §6.3.</summary>
public static class ChatTurns
{
    /// <summary>Comes before the context and form values in a user message.</summary>
    public const string ContextLead = "context and form values = ";

    /// <summary>How to wire a choice to a Query so the first fetch has a value and a change fetches again (spec §5.1, §6.1).</summary>
    public const string QueryRule = "When a choice picks what a Query fetches, declare its $variable with a real value as the default, such as a field id, and write that $variable directly in the Query's args. A Query runs again only when a $variable written in its args changes.";

    /// <summary>The system prompt, composed from the registered component sources the parser also reads.</summary>
    public static string SystemPrompt(OpenUIComponents components) => components.SystemPrompt(new PromptOptions
    {
        Tools = WeatherTools.Descriptors,
        AdditionalRules =
        [
            "Reply only with a program; explain things in TextContent components.",
            "End a reply with one or two Buttons that use @ToAssistant to offer the likely next questions.",
            QueryRule,
        ],
    });

    /// <summary>The request for the next reply.</summary>
    public static IReadOnlyList<ChatMessage> For(OpenUIComponents components, IEnumerable<ChatEntry> history)
    {
        var messages = new List<ChatMessage> { new(ChatRole.System, SystemPrompt(components)) };
        string? lastErrors = null;
        foreach (var entry in history)
        {
            if (entry.Role == "assistant")
            {
                // A reply stopped before its first word is nothing the model can read.
                if (entry.Text.Length > 0)
                {
                    messages.Add(new ChatMessage(ChatRole.Assistant, entry.Text));
                    lastErrors = entry.Errors;
                }

                continue;
            }

            var text = entry.Context is null ? entry.Text : $"{entry.Text}\n{ContextLead}{entry.Context}";
            messages.Add(new ChatMessage(ChatRole.User, lastErrors is null ? text : $"{text}\nYour last program had these errors: {lastErrors}"));
            lastErrors = null;
        }

        return messages;
    }

    /// <summary>
    /// A user message from an action: the message, and the JSON the model gets with it. Null when those
    /// come to more than a click may carry, the text a page holds (<see cref="OpenUIProgram.DefaultMaxTextChars"/>). The JSON escapes
    /// every non-ASCII character to six, so state the session allows can come out several times its size.
    /// </summary>
    public static ChatEntry? FromEvent(OpenUIHostEvent e)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            writer.WriteStringValue($"User clicked: {e.HumanFriendlyMessage}");
            ((OuiValue)e.FormState).WriteTo(writer);
            if (e.Params.TryGetValue("context", out var context))
            {
                context.WriteTo(writer);
            }

            writer.WriteEndArray();
        }

        return e.HumanFriendlyMessage.Length + buffer.Length > OpenUIProgram.DefaultMaxTextChars
            ? null
            : new ChatEntry { Role = "user", Text = e.HumanFriendlyMessage, Context = Encoding.UTF8.GetString(buffer.ToArray()) };
    }
}
