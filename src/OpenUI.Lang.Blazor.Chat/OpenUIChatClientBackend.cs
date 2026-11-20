using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;

namespace OpenUI.Lang.Blazor.Chat;

/// <summary>
/// Asks a Microsoft.Extensions.AI <see cref="IChatClient"/> in the process, which suits Blazor Server. The system prompt
/// leads every request, composed once from the registered components and <see cref="OpenUIBlazorChatOptions.PromptOptions"/>.
/// The client's exceptions pass through, and the session shows them as a fixed sentence.
/// </summary>
public sealed class OpenUIChatClientBackend : IOpenUIChatBackend
{
    /// <summary>The most turns sent by default.</summary>
    public const int DefaultMaxHistoryTurns = 100;

    /// <summary>The most UTF-8 bytes of turn text sent by default, about a hundred thousand tokens.</summary>
    public const int DefaultMaxHistoryBytes = 512 * 1024;

    private readonly IChatClient _client;
    private readonly OpenUIBlazorChatOptions _options;
    private readonly Lazy<string> _prompt;

    /// <summary>Creates the backend.</summary>
    /// <param name="client">The model.</param>
    /// <param name="components">The registered components, which the prompt teaches.</param>
    /// <param name="options">The chat's options, for the prompt options and the history limits.</param>
    public OpenUIChatClientBackend(IChatClient client, OpenUIComponents components, OpenUIBlazorChatOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(options);
        _client = client;
        _options = options;
        _prompt = new Lazy<string>(() => components.SystemPrompt(options.PromptOptions));
    }

    /// <summary>The system prompt that leads each request.</summary>
    public string SystemPrompt => _prompt.Value;

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatMessage> turns, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turns);
        var start = OpenUIChatTurns.FirstThatFits(
            turns,
            turns.Select(turn => (long)Encoding.UTF8.GetByteCount(turn.Text)).ToList(),
            0,
            _options.MaxHistoryTurns ?? DefaultMaxHistoryTurns,
            _options.MaxHistoryBytes ?? DefaultMaxHistoryBytes);
        List<ChatMessage> messages = [new(ChatRole.System, SystemPrompt), .. turns.Skip(start)];
        await foreach (var update in _client.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken))
        {
            if (update.Text is { Length: > 0 } text)
            {
                yield return text;
            }
        }
    }
}
