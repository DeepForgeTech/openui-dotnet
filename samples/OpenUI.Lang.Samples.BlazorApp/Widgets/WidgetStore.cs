using System.Text.Json;

namespace OpenUI.Lang.Samples.BlazorApp.Widgets;

/// <summary>
/// Keeps saved widgets as one JSON file each, in a folder of their own, so they outlast a restart.
/// Like the conversations, this is for one person on one machine: every browser sees the same widgets.
/// </summary>
public sealed class WidgetStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly Lock _gate = new();
    private readonly string _directory;
    private readonly ILogger<WidgetStore> _logger;

    /// <summary>Creates the store.</summary>
    /// <param name="directory">Where the files go. Created if missing.</param>
    /// <param name="logger">Hears about files it cannot read.</param>
    public WidgetStore(string directory, ILogger<WidgetStore> logger)
    {
        _directory = directory;
        _logger = logger;
        Directory.CreateDirectory(directory);
    }

    /// <summary>Raised after a widget is saved or deleted, on the thread that did it.</summary>
    public event Action? Changed;

    /// <summary>Every widget, most recently saved first.</summary>
    public IReadOnlyList<Widget> List()
    {
        lock (_gate)
        {
            return Directory.EnumerateFiles(_directory, "*.json")
                .Select(Read)
                .OfType<Widget>()
                .OrderByDescending(widget => widget.UpdatedAt)
                .ToList();
        }
    }

    /// <summary>The widget with this id, or null.</summary>
    public Widget? Find(Guid id)
    {
        lock (_gate)
        {
            return Read(PathOf(id));
        }
    }

    /// <summary>Writes the widget over any earlier save of it. A crash mid-write leaves the last good file.</summary>
    public void Save(Widget widget)
    {
        lock (_gate)
        {
            widget.UpdatedAt = DateTimeOffset.UtcNow;
            var path = PathOf(widget.Id);
            var partial = path + ".partial";
            File.WriteAllText(partial, JsonSerializer.Serialize(widget, Json));
            File.Move(partial, path, overwrite: true);
        }

        Changed?.Invoke();
    }

    /// <summary>Removes the widget.</summary>
    public void Delete(Guid id)
    {
        lock (_gate)
        {
            File.Delete(PathOf(id));
        }

        Changed?.Invoke();
    }

    private string PathOf(Guid id) => Path.Combine(_directory, $"{id:N}.json");

    private Widget? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Widget>(File.ReadAllText(path), Json);
        }
        catch (JsonException error)
        {
            _logger.LogWarning(error, "widget_unreadable path={Path}", path);
            return null;
        }
    }
}
