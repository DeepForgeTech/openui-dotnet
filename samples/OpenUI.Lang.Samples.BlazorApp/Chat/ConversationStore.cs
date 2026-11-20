using System.Text.Json;
using OpenUI.Lang.Blazor.Chat;

namespace OpenUI.Lang.Samples.BlazorApp.Chat;

/// <summary>
/// Keeps conversations as one JSON file each, so they outlast a reload and a restart. This is a
/// sample for one person on one machine: every browser sees the same list.
/// </summary>
public sealed class ConversationStore : IOpenUIConversationStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly Lock _gate = new();
    private readonly string _directory;
    private readonly ILogger<ConversationStore> _logger;

    /// <summary>Creates the store.</summary>
    /// <param name="directory">Where the files go. Created if missing.</param>
    /// <param name="logger">Hears about files it cannot read.</param>
    public ConversationStore(string directory, ILogger<ConversationStore> logger)
    {
        _directory = directory;
        _logger = logger;
        Directory.CreateDirectory(directory);
    }

    /// <summary>Raised after the list of conversations changes, on the thread that changed it.</summary>
    public event Action? Changed;

    /// <summary>Every conversation, most recent first.</summary>
    public Task<IReadOnlyList<OpenUIConversation>> ListAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<OpenUIConversation> list = Directory.EnumerateFiles(_directory, "*.json")
                .Select(Read)
                .OfType<OpenUIConversation>()
                .OrderByDescending(conversation => conversation.UpdatedAt)
                .ToList();
            return Task.FromResult(list);
        }
    }

    /// <summary>The conversation with this id, or null.</summary>
    public Task<OpenUIConversation?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(Read(PathOf(id)));
        }
    }

    /// <summary>Writes the conversation. A crash mid-write leaves the last good file.</summary>
    public Task SaveAsync(OpenUIConversation conversation, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            conversation.UpdatedAt = DateTimeOffset.UtcNow;
            var path = PathOf(conversation.Id);
            var partial = path + ".partial";
            File.WriteAllText(partial, JsonSerializer.Serialize(conversation, Json));
            File.Move(partial, path, overwrite: true);
        }

        Changed?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>Removes the conversation.</summary>
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            File.Delete(PathOf(id));
        }

        Changed?.Invoke();
        return Task.CompletedTask;
    }

    private string PathOf(Guid id) => Path.Combine(_directory, $"{id:N}.json");

    private OpenUIConversation? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<OpenUIConversation>(File.ReadAllText(path), Json);
        }
        catch (JsonException error)
        {
            _logger.LogWarning(error, "conversation_unreadable path={Path}", path);
            return null;
        }
    }
}
