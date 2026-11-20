using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor.Chat;

/// <summary>Turns a conversation into what the model reads, by the reference format of prompt.md §6.2 and §6.3. There is no system prompt here: the backend adds it.</summary>
public static class OpenUIChatTurns
{
    /// <summary>Comes before the context and form values in a user message.</summary>
    public const string ContextLead = "context and form values = ";

    /// <summary>Comes before a reply's errors in the user message after it.</summary>
    public const string ErrorsLead = "Your last program had these errors: ";

    // The model is not a browser, so the default encoder's escaping of non-ASCII text only makes the message longer.
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// The turns for the next reply: each user message with its context, and each reply with any text. The errors of the
    /// last reply go with the user message after it.
    /// </summary>
    /// <param name="history">The conversation so far, oldest first.</param>
    public static IReadOnlyList<ChatMessage> For(IEnumerable<OpenUIChatEntry> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        var messages = new List<ChatMessage>();
        string? lastErrors = null;
        foreach (var entry in history)
        {
            if (entry.Role == OpenUIChatRoles.Assistant)
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
            messages.Add(new ChatMessage(ChatRole.User, lastErrors is null ? text : $"{text}\n{ErrorsLead}{lastErrors}"));
            lastErrors = null;
        }

        return messages;
    }

    /// <summary>
    /// Where the turns that fit start: the oldest are dropped until at most <paramref name="maxTurns"/> are left and their sizes,
    /// with <paramref name="fixedSize"/>, come to at most <paramref name="maxSize"/>, and then until the first is a user turn. The
    /// last turn, the newest message with the last reply's errors, is always kept.
    /// </summary>
    internal static int FirstThatFits(IReadOnlyList<ChatMessage> turns, IReadOnlyList<long> sizes, long fixedSize, int maxTurns, long maxSize)
    {
        var total = fixedSize + sizes.Sum();
        var start = 0;
        while (start < turns.Count - 1 && (turns.Count - start > maxTurns || total > maxSize || turns[start].Role != ChatRole.User))
        {
            total -= sizes[start];
            start++;
        }

        return start;
    }

    /// <summary>
    /// The user message for a <c>continue_conversation</c> action: its message, with the JSON
    /// <c>["User clicked: …", formState, context]</c>. Null when those come to more than a click may carry, the text a page
    /// holds (<see cref="OpenUIProgram.DefaultMaxTextChars"/>), in characters. The JSON escapes only what it must, as
    /// OpenUI.Lang.AspNetCore's does, but a quote or a control character still comes out longer, so state the session allows
    /// can come out larger than it.
    /// </summary>
    /// <param name="e">The action.</param>
    public static OpenUIChatEntry? FromEvent(OpenUIHostEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
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

        var json = Encoding.UTF8.GetString(buffer.ToArray());
        return e.HumanFriendlyMessage.Length + json.Length > OpenUIProgram.DefaultMaxTextChars
            ? null
            : new OpenUIChatEntry { Role = OpenUIChatRoles.User, Text = e.HumanFriendlyMessage, Context = json };
    }
}
