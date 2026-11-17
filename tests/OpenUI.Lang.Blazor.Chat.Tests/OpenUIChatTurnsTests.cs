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
}
