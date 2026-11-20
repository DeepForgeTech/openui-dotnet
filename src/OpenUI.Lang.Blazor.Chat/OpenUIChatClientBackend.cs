using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace OpenUI.Lang.Blazor.Chat;

/// <summary>
/// Asks a Microsoft.Extensions.AI <see cref="IChatClient"/> in the process, which suits Blazor Server. The system prompt
/// leads every request, composed once from the registered components and <see cref="OpenUIBlazorChatOptions.PromptOptions"/>.
/// </summary>
public sealed class OpenUIChatClientBackend : IOpenUIChatBackend
{
    private readonly IChatClient _client;
    private readonly Lazy<string> _prompt;

    /// <summary>Creates the backend.</summary>
    /// <param name="client">The model.</param>
    /// <param name="components">The registered components, which the prompt teaches.</param>
    /// <param name="options">The chat's options, for the prompt options.</param>
    public OpenUIChatClientBackend(IChatClient client, OpenUIComponents components, OpenUIBlazorChatOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(options);
        _client = client;
        _prompt = new Lazy<string>(() => components.SystemPrompt(options.PromptOptions));
    }

    /// <summary>The system prompt that leads each request.</summary>
    public string SystemPrompt => _prompt.Value;

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatMessage> turns, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turns);
        List<ChatMessage> messages = [new(ChatRole.System, SystemPrompt), .. turns];
        await foreach (var update in _client.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken))
        {
            if (update.Text is { Length: > 0 } text)
            {
                yield return text;
            }
        }
    }
}
