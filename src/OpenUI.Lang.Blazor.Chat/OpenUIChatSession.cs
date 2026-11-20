using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenUI.Lang.Runtime;

namespace OpenUI.Lang.Blazor.Chat;

/// <summary>
/// One open conversation and the reply streaming into it, with no UI. <c>OpenUIChat</c> draws one; a host that draws
/// its own chat can use it directly. Its members are meant for one thread at a time, such as a component's renderer.
/// </summary>
/// <remarks>
/// A reply's errors go back to the model with the next message (prompt.md §6.2), and they come from the program as it is
/// drawn: parse errors, and what failed while it ran, such as a query. <c>OpenUIChatReply</c> sets them on the entry's
/// <see cref="OpenUIChatEntry.Errors"/>. A host that draws replies some other way sets <see cref="OpenUIChatEntry.Errors"/>
/// itself, such as from <c>OpenUIProgram</c>'s <c>OnErrors</c> with <c>ToModelFeedbackJson()</c>, or the model never hears them.
/// </remarks>
public sealed class OpenUIChatSession : IDisposable
{
    /// <summary>What the person sees for a failure that is not an <see cref="OpenUIChatException"/>.</summary>
    public const string FailedSentence = "The model request failed.";

    private readonly IOpenUIChatBackend _backend;
    private readonly IOpenUIConversationStore _store;
    private readonly OpenUIBlazorChatOptions _options;
    private readonly ILogger _logger;
    private CancellationTokenSource? _run;
    private int _opening;
    private bool _disposed;

    /// <summary>Creates a session with no conversation open.</summary>
    /// <param name="backend">Where replies come from.</param>
    /// <param name="store">Where conversations are kept.</param>
    /// <param name="options">The chat's options.</param>
    /// <param name="logger">Hears about failed, cut off and timed out replies.</param>
    public OpenUIChatSession(IOpenUIChatBackend backend, IOpenUIConversationStore store, OpenUIBlazorChatOptions options, ILogger<OpenUIChatSession>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        _backend = backend;
        _store = store;
        _options = options;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <summary>
    /// Raised when anything here changes: a message, the reply's text, the end of a reply, or a failure. While a reply streams
    /// it is raised at most about every 50 milliseconds, with one more when the reply ends.
    /// </summary>
    public event Action? Changed;

    /// <summary>The open conversation, or null before the first message of a new one.</summary>
    public OpenUIConversation? Conversation { get; private set; }

    /// <summary>The reply that is streaming, or null.</summary>
    public OpenUIChatEntry? Streaming { get; private set; }

    /// <summary>True while a reply streams.</summary>
    public bool IsStreaming => Streaming is not null;

    /// <summary>What went wrong with the last reply or action, as a sentence for the person, or null.</summary>
    public string? Failure { get; private set; }

    /// <summary>Called with the address of an <c>open_url</c> action. Nothing opens unless the host does it.</summary>
    public Func<string, Task>? OnOpenUrl { get; set; }

    /// <summary>Called with any action other than <c>continue_conversation</c> and <c>open_url</c>.</summary>
    public Func<OpenUIHostEvent, Task>? OnHostEvent { get; set; }

    /// <summary>
    /// Opens a stored conversation, or a new one when <paramref name="id"/> is null or not stored. A reply that is streaming
    /// stops, keeps what it has and is saved, but changes nothing here. A store that fails leaves a new conversation open
    /// and says so in <see cref="Failure"/>.
    /// </summary>
    /// <param name="id">The conversation's id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task OpenAsync(Guid? id, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Stop();
        _run = null;
        var opening = ++_opening;
        Streaming = null;
        Failure = null;
        Conversation = null;
        OpenUIConversation? found = null;
        string? failure = null;
        try
        {
            found = id is { } stored ? await _store.FindAsync(stored, cancellationToken) : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _logger.LogError(error, "chat_open_failed conversation={Conversation}", id);
            failure = "The conversation could not be opened.";
        }

        // Another conversation was opened, or a message sent, while this one was read.
        if (opening == _opening && Conversation is null)
        {
            Conversation = found;
            Failure = failure;
        }

        RaiseChanged();
    }

    /// <summary>Stops the reply that is streaming. It keeps what it has, and its program finishes with the text so far.</summary>
    public void Stop() => _run?.Cancel();

    /// <summary>Sends what the person wrote and streams the reply. Blank text, or text while a reply streams, does nothing.</summary>
    /// <param name="text">The message.</param>
    /// <returns>A task that ends with the reply.</returns>
    public Task SendAsync(string text) =>
        string.IsNullOrWhiteSpace(text) || IsStreaming ? Task.CompletedTask : SendAsync(new OpenUIChatEntry { Role = OpenUIChatRoles.User, Text = text.Trim() });

    /// <summary>
    /// Handles an action from a reply's program: <c>continue_conversation</c> sends its message with the context and form
    /// values, unless a reply is streaming or they are too large to send; <c>open_url</c> goes to <see cref="OnOpenUrl"/>; any
    /// other to <see cref="OnHostEvent"/>.
    /// </summary>
    /// <param name="e">The action.</param>
    /// <returns>A task that ends when the action is handled, which for a message is when its reply ends.</returns>
    public async Task HandleHostEventAsync(OpenUIHostEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        switch (e.Type)
        {
            case "continue_conversation":
                if (IsStreaming)
                {
                    return;
                }

                if (OpenUIChatTurns.FromEvent(e) is { } message)
                {
                    await SendAsync(message);
                }
                else
                {
                    Failure = "That action carries too much data to send to the model.";
                    RaiseChanged();
                }

                break;
            case "open_url":
                if (OnOpenUrl is { } open && e.Params.TryGetValue("url", out var url) && url.TryGetString(out var address))
                {
                    await open(address);
                }

                break;
            default:
                if (OnHostEvent is { } handler)
                {
                    await handler(e);
                }

                break;
        }
    }

    /// <summary>Stops a reply that is streaming, which still saves what it has.</summary>
    public void Dispose()
    {
        _disposed = true;
        Stop();
    }

    private async Task SendAsync(OpenUIChatEntry message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _opening++;
        var conversation = Conversation ??= Start(message.Text);
        conversation.Messages.Add(message);
        var turns = OpenUIChatTurns.For(conversation.Messages);
        var reply = new OpenUIChatEntry { Role = OpenUIChatRoles.Assistant };
        conversation.Messages.Add(reply);

        using var run = new CancellationTokenSource();
        using var replyLimit = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
        _run = run;
        Streaming = reply;
        Failure = null;

        // Every effect here is for the run that is current: one that was replaced, and keeps streaming because its backend
        // ignores the token, must not touch the conversation opened since.
        bool Current() => ReferenceEquals(_run, run);
        void Fail(string sentence)
        {
            if (Current())
            {
                Failure = sentence;
            }
        }

        var text = new ReplyBuffer(reply, () =>
        {
            if (Current())
            {
                RaiseChanged();
            }
        });
        try
        {
            await _store.SaveAsync(conversation);
            if (Current())
            {
                RaiseChanged();
            }

            replyLimit.CancelAfter(_options.ReplyTimeout);
            await foreach (var piece in _backend.StreamReplyAsync(turns, replyLimit.Token))
            {
                if (run.IsCancellationRequested || !Current())
                {
                    break;
                }

                if (piece.Length == 0)
                {
                    continue;
                }

                if (text.Length + piece.Length > _options.MaxReplyChars)
                {
                    // Leaving the loop disposes the stream, which ends the model call.
                    _logger.LogError("chat_reply_too_long conversation={Conversation} limit={Limit}", conversation.Id, _options.MaxReplyChars);
                    Fail($"The reply was cut off after {_options.MaxReplyChars} characters.");
                    break;
                }

                text.Append(piece);
            }
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            // Stopped: the reply keeps what it has.
        }
        catch (OperationCanceledException error)
        {
            // Our own limit, or the backend's client giving up on its own, which is a TaskCanceledException.
            _logger.LogError(error, "chat_reply_timed_out conversation={Conversation}", conversation.Id);
            Fail("The model took too long to answer.");
        }
        catch (OpenUIChatException error)
        {
            _logger.LogError(error, "chat_reply_failed conversation={Conversation}", conversation.Id);
            Fail(error.Message);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Its text may name hosts, keys or prompts, so the person gets a fixed sentence.
            _logger.LogError(error, "chat_reply_failed conversation={Conversation}", conversation.Id);
            Fail(FailedSentence);
        }
        finally
        {
            text.Complete();
            if (Current())
            {
                _run = null;
                Streaming = null;
            }
        }

        await SaveUnlessDeletedAsync(conversation, Current);
        if (ReferenceEquals(Conversation, conversation) && _run is null)
        {
            RaiseChanged();
        }
    }

    // A conversation deleted while its reply streams must not come back when the reply ends.
    private async Task SaveUnlessDeletedAsync(OpenUIConversation conversation, Func<bool> current)
    {
        try
        {
            if (await _store.FindAsync(conversation.Id) is not null)
            {
                await _store.SaveAsync(conversation);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _logger.LogError(error, "chat_save_failed conversation={Conversation}", conversation.Id);
            if (ReferenceEquals(Conversation, conversation) && (current() || _run is null))
            {
                Failure ??= "The conversation could not be saved.";
            }
        }
    }

    private OpenUIConversation Start(string firstMessage) => new()
    {
        Id = Guid.NewGuid(),
        Title = TitleOf(firstMessage, _options.TitleLength),
    };

    // The start of the message, without half of a character that takes two.
    internal static string TitleOf(string message, int length)
    {
        if (message.Length <= length)
        {
            return message;
        }

        return char.IsHighSurrogate(message[length - 1]) ? message[..(length - 1)] : message[..length];
    }

    private void RaiseChanged()
    {
        if (!_disposed)
        {
            Changed?.Invoke();
        }
    }
}
