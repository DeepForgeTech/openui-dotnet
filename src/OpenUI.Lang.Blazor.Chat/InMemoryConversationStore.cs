namespace OpenUI.Lang.Blazor.Chat;

/// <summary>
/// Keeps conversations in memory, for as long as the store lives. It is the default, registered scoped, so under Blazor Server
/// the conversations last as long as the circuit, and under WebAssembly as long as the tab. It keeps a copy of what it is
/// given and hands out copies, so a conversation changes here only when it is saved.
/// </summary>
public sealed class InMemoryConversationStore : IOpenUIConversationStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, OpenUIConversation> _conversations = [];

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public Task<IReadOnlyList<OpenUIConversation>> ListAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<OpenUIConversation> list = _conversations.Values
                .OrderByDescending(conversation => conversation.UpdatedAt)
                .Select(conversation => conversation.Copy())
                .ToList();
            return Task.FromResult(list);
        }
    }

    /// <inheritdoc />
    public Task<OpenUIConversation?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_conversations.TryGetValue(id, out var conversation) ? conversation.Copy() : null);
        }
    }

    /// <inheritdoc />
    public Task SaveAsync(OpenUIConversation conversation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        lock (_gate)
        {
            // Two saves in the same tick still list in the order they were made.
            var now = DateTimeOffset.UtcNow;
            var latest = _conversations.Values.Select(stored => stored.UpdatedAt).DefaultIfEmpty(now).Max();
            conversation.UpdatedAt = now > latest ? now : latest.AddTicks(1);
            _conversations[conversation.Id] = conversation.Copy();
        }

        Changed?.Invoke();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        bool removed;
        lock (_gate)
        {
            removed = _conversations.Remove(id);
        }

        if (removed)
        {
            Changed?.Invoke();
        }

        return Task.CompletedTask;
    }
}
