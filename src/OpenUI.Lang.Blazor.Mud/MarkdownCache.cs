namespace OpenUI.Lang.Blazor.Mud;

/// <summary>The markup of recent markdown, least recently used out first. A sibling that grows redraws its neighbours, which must not parse again.</summary>
/// <param name="maxEntries">The most strings it holds.</param>
/// <param name="maxChars">The most characters it holds, in the keys and the markup together.</param>
internal sealed class MarkdownCache(int maxEntries, int maxChars)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Key, LinkedListNode<(Key Key, string Html)>> _entries = [];
    private readonly LinkedList<(Key Key, string Html)> _recent = new();
    private long _chars;

    public bool TryGet(Key key, out string html)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                _recent.Remove(entry);
                _recent.AddFirst(entry);
                html = entry.Value.Html;
                return true;
            }
        }

        html = "";
        return false;
    }

    public void Add(Key key, string html)
    {
        lock (_gate)
        {
            if (_entries.ContainsKey(key))
            {
                return;
            }

            _entries[key] = _recent.AddFirst((key, html));
            _chars += Size(key, html);
            while (_entries.Count > maxEntries || _chars > maxChars)
            {
                var oldest = _recent.Last!;
                _recent.RemoveLast();
                _entries.Remove(oldest.Value.Key);
                _chars -= Size(oldest.Value.Key, oldest.Value.Html);
            }
        }
    }

    private static long Size(Key key, string html) => key.Text.Length + (long)html.Length;

    /// <summary>What decides the markup.</summary>
    internal readonly record struct Key(string Text, MarkdownStyle? Style, bool LineBreaks, bool Inline);
}
