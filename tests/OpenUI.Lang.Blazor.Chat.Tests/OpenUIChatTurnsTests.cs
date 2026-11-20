using Microsoft.Extensions.AI;

namespace OpenUI.Lang.Blazor.Chat.Tests;

public sealed class OpenUIChatTurnsTests
{
    private static OpenUIChatEntry User(string text, string? context = null) => new() { Role = OpenUIChatRoles.User, Text = text, Context = context };

    private static OpenUIChatEntry Reply(string text, string? errors = null) => new() { Role = OpenUIChatRoles.Assistant, Text = text, Errors = errors };

    [Fact]
    public void The_last_replys_errors_go_with_the_next_message_and_an_empty_reply_is_left_out()
    {
        var turns = OpenUIChatTurns.For(
        [
            User("One"),
            Reply("root = A()", errors: "[old]"),
            User("Two"),
            Reply("root = B()", errors: "[new]"),
            User("Three", context: "[\"x\"]"),
            Reply(""),
            User("Four"),
        ]);

        Assert.Equal([ChatRole.User, ChatRole.Assistant, ChatRole.User, ChatRole.Assistant, ChatRole.User, ChatRole.User], turns.Select(turn => turn.Role));
        Assert.Equal("Two\nYour last program had these errors: [old]", turns[2].Text);
        Assert.Equal("Three\ncontext and form values = [\"x\"]\nYour last program had these errors: [new]", turns[4].Text);
        Assert.Equal("Four", turns[5].Text);
    }

    [Fact]
    public void An_actions_context_keeps_its_text_as_written_as_the_endpoint_does()
    {
        var e = new OpenUI.Lang.Runtime.OpenUIHostEvent("continue_conversation", "Plan <corn> & 'beans'",
            OpenUI.Lang.Values.OuiObject.Create([new KeyValuePair<string, OpenUI.Lang.Values.OuiValue>("context", "Café")]), null, OpenUI.Lang.Values.OuiObject.Empty);

        var message = OpenUIChatTurns.FromEvent(e);

        Assert.Equal("[\"User clicked: Plan <corn> & 'beans'\",{},\"Café\"]", message?.Context);
    }

    [Fact]
    public void Trimming_keeps_the_newest_turn_however_large_and_starts_with_a_user_turn()
    {
        List<ChatMessage> turns = [new(ChatRole.User, "a"), new(ChatRole.Assistant, "b"), new(ChatRole.User, "c")];

        Assert.Equal(2, OpenUIChatTurns.FirstThatFits(turns, [1, 1, 100], 0, 100, 10));
        Assert.Equal(2, OpenUIChatTurns.FirstThatFits(turns, [1, 1, 1], 0, 2, 100));
        Assert.Equal(0, OpenUIChatTurns.FirstThatFits(turns, [1, 1, 1], 0, 3, 3));
    }
}
