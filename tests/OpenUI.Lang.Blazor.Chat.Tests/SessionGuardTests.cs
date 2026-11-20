using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.AI;
using OpenUI.Lang.Blazor.Chat.Tests.Kit;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor.Chat.Tests;

/// <summary>The session under load, with backends and stores that misbehave.</summary>
public sealed class SessionGuardTests
{
    private readonly OpenUIInMemoryConversationStore _store = new();
    private readonly OpenUIBlazorChatOptions _options = new();

    [Fact]
    public async Task A_reply_in_many_small_pieces_raises_few_changes_and_arrives_whole()
    {
        var model = new ScriptedModel();
        using var session = new OpenUIChatSession(new OpenUIChatClientBackend(model, new OpenUIComponents([new ChatKit()]), _options), _store, _options);
        var changes = 0;
        session.Changed += () => Interlocked.Increment(ref changes);
        var clock = Stopwatch.StartNew();

        var sending = session.SendAsync("Go");
        for (var index = 0; index < 20_000; index++)
        {
            await model.Say("x");
        }

        model.Finish();
        await sending;

        Assert.Equal(new string('x', 20_000), (await _store.ListAsync())[0].Messages[1].Text);
        // One for the message, one when the reply ends, and at most one each 50 milliseconds between.
        Assert.True(changes <= 3 + clock.ElapsedMilliseconds / 50, $"{changes} changes in {clock.ElapsedMilliseconds} ms.");
        Assert.True(changes < 1000, $"{changes} changes.");
    }

    [Fact]
    public async Task A_piece_that_arrives_alone_is_shown_without_waiting_for_the_next()
    {
        var model = new ScriptedModel();
        using var session = new OpenUIChatSession(new OpenUIChatClientBackend(model, new OpenUIComponents([new ChatKit()]), _options), _store, _options);
        var shown = new TaskCompletionSource();
        session.Changed += () =>
        {
            if (session.Streaming?.Text == "root = Stack([])\n")
            {
                shown.TrySetResult();
            }
        };

        var sending = session.SendAsync("Go");
        await model.Say("root = Stack([])\n");

        await shown.Task.WaitAsync(TimeSpan.FromSeconds(10));
        model.Finish();
        await sending;
    }

    [Fact]
    public async Task A_replaced_run_that_keeps_streaming_does_not_touch_the_conversation_opened_since()
    {
        var other = new OpenUIConversation { Id = Guid.NewGuid(), Title = "Other" };
        await _store.SaveAsync(other);
        var backend = new DeafBackend();
        using var session = new OpenUIChatSession(backend, _store, _options);
        var sending = session.SendAsync("Go");
        await backend.Say("root = Stack([])\n");

        await session.OpenAsync(other.Id);
        var changes = 0;
        session.Changed += () => changes++;
        await backend.Say("more");
        backend.Fail(new OpenUIChatException("Should not show."));
        await sending;

        Assert.Equal(other.Id, session.Conversation?.Id);
        Assert.Null(session.Failure);
        Assert.False(session.IsStreaming);
        Assert.Equal(0, changes);
        Assert.Empty((await _store.FindAsync(other.Id))!.Messages);
    }

    [Fact]
    public async Task A_store_that_fails_to_open_a_conversation_says_so_and_leaves_a_new_one()
    {
        using var session = new OpenUIChatSession(new DeafBackend(), new BrokenStore(), _options);

        await session.OpenAsync(Guid.NewGuid());

        Assert.Equal("The conversation could not be opened.", session.Failure);
        Assert.Null(session.Conversation);
    }

    [Theory]
    [InlineData("Spray 🌽 today", 7, "Spray ")]
    [InlineData("Spray 🌽 today", 8, "Spray 🌽")]
    [InlineData("Short", 60, "Short")]
    public void A_title_is_never_cut_inside_a_character(string message, int length, string title) =>
        Assert.Equal(title, OpenUIChatSession.TitleOf(message, length));

    [Fact]
    public async Task In_process_the_history_sent_is_trimmed_to_its_limits_and_the_stored_one_is_not()
    {
        var model = new ScriptedModel();
        _options.MaxHistoryBytes = 30;
        using var session = new OpenUIChatSession(new OpenUIChatClientBackend(model, new OpenUIComponents([new ChatKit()]), _options), _store, _options);
        foreach (var message in new[] { "First question", "Second question", "Third question" })
        {
            var sending = session.SendAsync(message);
            await model.Say("root = Stack([])\n");
            model.Finish();
            await sending;
        }

        Assert.Equal(["Third question"], model.Turns[^1].Skip(1).Select(turn => turn.Text));
        Assert.Equal(6, Assert.Single(await _store.ListAsync()).Messages.Count);
    }

    /// <summary>A backend that ignores its cancellation token.</summary>
    private sealed class DeafBackend : IOpenUIChatBackend
    {
        private readonly Channel<string> _pieces = Channel.CreateUnbounded<string>();

        public async Task Say(string text) => await _pieces.Writer.WriteAsync(text);

        public void Fail(Exception error) => _pieces.Writer.TryComplete(error);

        public async IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatMessage> turns, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var piece in _pieces.Reader.ReadAllAsync(CancellationToken.None))
            {
                yield return piece;
            }
        }
    }

    private sealed class BrokenStore : IOpenUIConversationStore
    {
        public event Action? Changed { add { } remove { } }

        public Task<IReadOnlyList<OpenUIConversation>> ListAsync(CancellationToken cancellationToken = default) => throw new IOException("Disk gone.");

        public Task<OpenUIConversation?> FindAsync(Guid id, CancellationToken cancellationToken = default) => throw new IOException("Disk gone.");

        public Task SaveAsync(OpenUIConversation conversation, CancellationToken cancellationToken = default) => throw new IOException("Disk gone.");

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new IOException("Disk gone.");
    }
}
