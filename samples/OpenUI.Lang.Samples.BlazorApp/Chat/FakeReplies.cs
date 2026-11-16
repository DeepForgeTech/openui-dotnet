using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenUI.Lang.Samples.Llm;

namespace OpenUI.Lang.Samples.BlazorApp.Chat;

/// <summary>What the fake provider answers, so the whole loop runs with no model: each reply is a real program in the Mud library.</summary>
public static class FakeReplies
{
    /// <summary>Picks a program from the words of the last user message, and has it say it is canned.</summary>
    public static string For(IReadOnlyList<ChatMessage> messages) => $"""
        root = Stack([canned, reply])
        canned = Callout("neutral", "Fake model", {JsonSerializer.Serialize(LlmSettings.CannedReplyNotice)})
        {Pick(messages).Replace("root = ", "reply = ", StringComparison.Ordinal)}
        """;

    private static string Pick(IReadOnlyList<ChatMessage> messages)
    {
        var message = messages.LastOrDefault(item => item.Role == ChatRole.User)?.Text ?? "";
        if (message.StartsWith("Here is the plan", StringComparison.Ordinal))
        {
            return PlanReceived(message);
        }

        if (message.Contains("plan", StringComparison.OrdinalIgnoreCase) || message.Contains("job", StringComparison.OrdinalIgnoreCase))
        {
            return ProgramFiles.Read("plan-job.oui");
        }

        return message.Contains("forecast", StringComparison.OrdinalIgnoreCase)
            ? ProgramFiles.Read("forecast.oui")
            : ProgramFiles.Read("spray-window.oui");
    }

    // Shows that the form's fields really came back with the action.
    private static string PlanReceived(string message)
    {
        var lead = message.IndexOf(ChatTurns.ContextLead, StringComparison.Ordinal);
        var received = lead < 0 ? "nothing" : message[(lead + ChatTurns.ContextLead.Length)..].ReplaceLineEndings(" ");
        return $"""
            root = Card([Callout("success", "Plan received", "The fake provider got your plan with the form's fields."), TextContent({JsonSerializer.Serialize(received)}, "small"), again])
            again = Buttons([Button("Check the spray window", @ToAssistant("Can we spray today?"), "secondary")])
            """;
    }
}
