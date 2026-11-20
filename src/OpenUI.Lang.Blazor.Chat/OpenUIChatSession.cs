using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenUI.Lang.Runtime;

namespace OpenUI.Lang.Blazor.Chat;

/// <summary>
/// One open conversation and the reply streaming into it, with no UI. <c>OpenUIChat</c> draws one; a host that draws
/// its own chat can use it directly. Its members are meant for one thread at a time, such as a component's renderer.
/// </summary>
public sealed class OpenUIChatSession : IDisposable
{
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

    /// <summary>Raised when anything here changes: a message, a piece of a reply, the end of one, or a failure.</summary>
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
    /// stops and keeps what it has.
    /// </summary>
    /// <param name="id">The conversation's id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task OpenAsync(Guid? id, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Stop();
        var opening = ++_opening;
        Streaming = null;
        Failure = null;
        Conversation = null;
        var found = id is { } stored ? await _store.FindAsync(stored, cancellationToken) : null;

        // Another conversation was opened, or a message sent, while this one was read.
        if (opening == _opening && Conversation is null)
        {
            Conversation = found;
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
        try
        {
            await _store.SaveAsync(conversation);
            RaiseChanged();
            replyLimit.CancelAfter(_options.ReplyTimeout);
            await foreach (var text in _backend.StreamReplyAsync(turns, replyLimit.Token))
            {
                if (text.Length == 0)
                {
                    continue;
                }

                if (reply.Text.Length + text.Length > _options.MaxReplyChars)
                {
                    // Leaving the loop disposes the stream, which ends the model call.
                    _logger.LogError("chat_reply_too_long conversation={Conversation} limit={Limit}", conversation.Id, _options.MaxReplyChars);
                    Failure = $"The reply was cut off after {_options.MaxReplyChars} characters.";
                    break;
                }

                reply.Text += text;
                RaiseChanged();
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
            Failure = "The model took too long to answer.";
        }
        catch (Exception error) when (error is IOException or HttpRequestException)
        {
            _logger.LogError(error, "chat_reply_failed conversation={Conversation}", conversation.Id);
            Failure = error.Message;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Any other exception's text may name hosts, keys or prompts, so the person gets a fixed sentence.
            _logger.LogError(error, "chat_reply_failed conversation={Conversation}", conversation.Id);
            Failure = "The model request failed.";
        }
        finally
        {
            // Opening another conversation can start a new reply before this one has noticed it was stopped.
            if (ReferenceEquals(_run, run))
            {
                _run = null;
            }

            if (ReferenceEquals(Streaming, reply))
            {
                Streaming = null;
            }
        }

        await SaveUnlessDeletedAsync(conversation);
        RaiseChanged();
    }

    // A conversation deleted while its reply streams must not come back when the reply ends.
    private async Task SaveUnlessDeletedAsync(OpenUIConversation conversation)
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
            Failure ??= "The conversation could not be saved.";
        }
    }

    private OpenUIConversation Start(string firstMessage) => new()
    {
        Id = Guid.NewGuid(),
        Title = firstMessage.Length <= _options.TitleLength ? firstMessage : firstMessage[.._options.TitleLength],
    };

    private void RaiseChanged()
    {
        if (!_disposed)
        {
            Changed?.Invoke();
        }
    }
}
