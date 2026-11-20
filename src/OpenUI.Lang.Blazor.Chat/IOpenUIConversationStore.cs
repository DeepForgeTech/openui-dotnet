namespace OpenUI.Lang.Blazor.Chat;

/// <summary>
/// Keeps conversations. It is registered scoped, so a host can keep each person's apart. How a conversation is stored is the
/// store's own business.
/// </summary>
/// <remarks>
/// A session saves the whole conversation, so two sessions with the same conversation open, such as two tabs, each save
/// their own copy, and the last save wins. A store that needs more should version its saves, such as with a version number
/// it checks on each save.
/// </remarks>
public interface IOpenUIConversationStore
{
    /// <summary>Raised after the list of conversations changes. It may be raised on any thread, such as another circuit's.</summary>
    event Action? Changed;

    /// <summary>Every conversation, most recently saved first.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<OpenUIConversation>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>The conversation with this id, or null.</summary>
    /// <param name="id">The id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<OpenUIConversation?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Adds or replaces the conversation, and sets its <see cref="OpenUIConversation.UpdatedAt"/>.</summary>
    /// <param name="conversation">The conversation.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task SaveAsync(OpenUIConversation conversation, CancellationToken cancellationToken = default);

    /// <summary>Removes the conversation. Removing one that is not there does nothing.</summary>
    /// <param name="id">The id.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
