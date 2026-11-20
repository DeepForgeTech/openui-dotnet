using Microsoft.Extensions.AI;
using OpenUI.Lang.Blazor.Chat.Tests.Kit;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor.Chat.Tests;

/// <summary>The send loop with no UI, over an IChatClient the test feeds.</summary>
public sealed class OpenUIChatSessionTests
{
    private readonly ScriptedModel _model = new();
    private readonly OpenUIInMemoryConversationStore _store = new();
    private readonly OpenUIBlazorChatOptions _options = new();

    private OpenUIChatSession NewSession() =>
        new(new OpenUIChatClientBackend(_model, new OpenUIComponents([new ChatKit()]), _options), _store, _options);

    private static OpenUIHostEvent Event(string type, string message, OuiObject? args = null) =>
        new(type, message, args ?? OuiObject.Empty, null, OuiObject.Empty);

    [Fact]
    public async Task A_message_starts_a_conversation_titled_by_it_and_the_reply_is_saved_when_it_ends()
    {
        _options.TitleLength = 5;
        using var session = NewSession();

        var sending = session.SendAsync("  Spray today?  ");
        await _model.Say("root = Stack([])\n");
        _model.Finish();
        await sending;

        var stored = Assert.Single(await _store.ListAsync());
        Assert.Equal("Spray", stored.Title);
        Assert.Equal(["Spray today?", "root = Stack([])\n"], stored.Messages.Select(entry => entry.Text));
        Assert.False(session.IsStreaming);
        Assert.Null(session.Failure);
        var turn = Assert.Single(_model.Turns);
        Assert.Equal(ChatRole.System, turn[0].Role);
        Assert.Equal("Spray today?", turn[1].Text);
    }

    [Fact]
    public async Task Stop_ends_the_reply_and_keeps_what_arrived()
    {
        using var session = NewSession();
        var heard = new TaskCompletionSource();
        session.Changed += () =>
        {
            if (session.Streaming is { Text.Length: > 0 })
            {
                heard.TrySetResult();
            }
        };

        var sending = session.SendAsync("Go");
        await _model.Say("root = Stack([])\n");
        await heard.Task;
        session.Stop();
        await sending;

        Assert.Null(session.Failure);
        Assert.Equal("root = Stack([])\n", Assert.Single(await _store.ListAsync()).Messages[1].Text);
    }

    [Fact]
    public async Task A_reply_longer_than_the_limit_is_cut_off_there_and_says_so()
    {
        _options.MaxReplyChars = 10;
        using var session = NewSession();

        var sending = session.SendAsync("Go");
        await _model.Say("12345");
        await _model.Say("678901");
        await sending;

        Assert.Equal("The reply was cut off after 10 characters.", session.Failure);
        Assert.Equal("12345", Assert.Single(await _store.ListAsync()).Messages[1].Text);
    }

    [Fact]
    public async Task A_backend_that_throws_something_unexpected_shows_a_fixed_sentence()
    {
        using var session = NewSession();

        var sending = session.SendAsync("Go");
        _model.Fail(new InvalidOperationException("key=secret"));
        await sending;

        Assert.Equal("The model request failed.", session.Failure);
    }

    [Fact]
    public async Task Opening_a_conversation_stops_the_reply_in_the_last_one_and_keeps_it()
    {
        var other = new OpenUIConversation { Id = Guid.NewGuid(), Title = "Other" };
        await _store.SaveAsync(other);
        using var session = NewSession();
        var sending = session.SendAsync("Go");
        await _model.Say("root = Stack([])\n");

        await session.OpenAsync(other.Id);
        await sending;

        Assert.Equal(other.Id, session.Conversation?.Id);
        Assert.False(session.IsStreaming);
        Assert.Equal(2, (await _store.ListAsync()).Count);
    }

    [Fact]
    public async Task An_open_url_action_goes_to_the_host_and_another_action_to_its_own_handler()
    {
        using var session = NewSession();
        string? opened = null;
        OpenUIHostEvent? other = null;
        session.OnOpenUrl = url =>
        {
            opened = url;
            return Task.CompletedTask;
        };
        session.OnHostEvent = e =>
        {
            other = e;
            return Task.CompletedTask;
        };

        await session.HandleHostEventAsync(Event("open_url", "Read", OuiObject.Create([new KeyValuePair<string, OuiValue>("url", "https://example.com/drift")])));
        await session.HandleHostEventAsync(Event("custom", "Custom"));

        Assert.Equal("https://example.com/drift", opened);
        Assert.Equal("custom", other?.Type);
        Assert.Empty(_model.Turns);
    }

    [Fact]
    public async Task A_continue_conversation_action_sends_its_message_with_the_context()
    {
        using var session = NewSession();

        var sending = session.HandleHostEventAsync(Event("continue_conversation", "Tell me more", OuiObject.Create([new KeyValuePair<string, OuiValue>("context", "why")])));
        _model.Finish();
        await sending;

        var message = Assert.Single(_model.Turns)[^1];
        Assert.Equal("Tell me more\ncontext and form values = [\"User clicked: Tell me more\",{},\"why\"]", message.Text);
    }
}
