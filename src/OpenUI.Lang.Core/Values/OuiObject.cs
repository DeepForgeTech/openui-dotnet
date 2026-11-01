using System.Collections;
using System.Collections.Immutable;

namespace OpenUI.Lang.Values;

/// <summary>
/// An immutable object value (language.md, section 3.1). Keys keep ECMAScript own-property order:
/// array-index keys first in ascending numeric order, then the other keys in insertion order. A key
/// given twice keeps its first position and takes the later value, as in an ECMAScript object literal.
/// </summary>
public sealed class OuiObject : IReadOnlyList<KeyValuePair<string, OuiValue>>
{
    private const int LinearLookupLimit = 8;

    private readonly ImmutableArray<KeyValuePair<string, OuiValue>> _entries;
    private Dictionary<string, int>? _index;

    private OuiObject(ImmutableArray<KeyValuePair<string, OuiValue>> entries)
    {
        _entries = entries;
        var measure = new MeasureBuilder();
        foreach (var entry in entries)
        {
            measure.AddText(entry.Key);
            measure.Add(entry.Value);
        }

        Measure = measure.ToMeasure();
    }

    /// <summary>The object with no keys (language.md, section 3.1).</summary>
    public static OuiObject Empty { get; } = new([]);

    /// <summary>
    /// An object with <paramref name="entries"/>, in ECMAScript own-property order (section 3.1).
    /// A repeated key keeps its first position and the last value.
    /// </summary>
    public static OuiObject Create(IEnumerable<KeyValuePair<string, OuiValue>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var builder = new Builder();
        foreach (var (key, value) in entries)
        {
            builder.Set(key, value);
        }

        return builder.ToObject();
    }

    /// <summary>The entries in ECMAScript own-property order (language.md, section 3.1).</summary>
    public ImmutableArray<KeyValuePair<string, OuiValue>> Entries => _entries;

    /// <summary>The number of keys (language.md, section 3.1).</summary>
    public int Count => _entries.Length;

    /// <summary>The entry at <paramref name="index"/> in own-property order (language.md, section 3.1).</summary>
    public KeyValuePair<string, OuiValue> this[int index] => _entries[index];

    /// <summary>The value of <paramref name="key"/>, or null when the key is absent (section 3.3).</summary>
    public OuiValue this[string key] => TryGetValue(key, out var value) ? value : OuiValue.Null;

    /// <summary>The keys in own-property order (language.md, section 3.1).</summary>
    public IEnumerable<string> Keys => _entries.Select(entry => entry.Key);

    /// <summary>What this object costs when expanded.</summary>
    internal ValueMeasure Measure { get; }

    /// <summary>True when the object has <paramref name="key"/> (language.md, section 3.1).</summary>
    public bool ContainsKey(string key) => IndexOf(key) >= 0;

    /// <summary>Reads <paramref name="key"/> (section 3.3).</summary>
    public bool TryGetValue(string key, out OuiValue value)
    {
        var index = IndexOf(key);
        value = index >= 0 ? _entries[index].Value : OuiValue.Null;
        return index >= 0;
    }

    /// <summary>Enumerates the entries in own-property order (language.md, section 3.1).</summary>
    public ImmutableArray<KeyValuePair<string, OuiValue>>.Enumerator GetEnumerator() => _entries.GetEnumerator();

    IEnumerator<KeyValuePair<string, OuiValue>> IEnumerable<KeyValuePair<string, OuiValue>>.GetEnumerator() =>
        ((IEnumerable<KeyValuePair<string, OuiValue>>)_entries).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_entries).GetEnumerator();

    /// <summary>The ECMAScript <c>String(object)</c>, <c>[object Object]</c> (section 3.1).</summary>
    public override string ToString() => "[object Object]";

    internal bool StructurallyEquals(OuiObject other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (Count != other.Count)
        {
            return false;
        }

        foreach (var (key, value) in _entries)
        {
            if (!other.TryGetValue(key, out var otherValue) || !value.Equals(otherValue))
            {
                return false;
            }
        }

        return true;
    }

    private int IndexOf(string key)
    {
        if (_entries.Length <= LinearLookupLimit)
        {
            for (var i = 0; i < _entries.Length; i++)
            {
                if (string.Equals(_entries[i].Key, key, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        var index = _index;
        if (index is null)
        {
            index = new Dictionary<string, int>(_entries.Length, StringComparer.Ordinal);
            for (var i = 0; i < _entries.Length; i++)
            {
                index[_entries[i].Key] = i;
            }

            _index = index;
        }

        return index.TryGetValue(key, out var found) ? found : -1;
    }

    /// <summary>
    /// True when <paramref name="key"/> is an ECMAScript array index: the canonical decimal form of
    /// an integer from 0 to 2^32 - 2.
    /// </summary>
    internal static bool IsArrayIndex(string key, out uint index)
    {
        index = 0;
        if (key.Length is 0 or > 10 || (key.Length > 1 && key[0] == '0'))
        {
            return false;
        }

        ulong value = 0;
        foreach (var c in key)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }

            value = (value * 10) + (ulong)(c - '0');
        }

        if (value >= uint.MaxValue)
        {
            return false;
        }

        index = (uint)value;
        return true;
    }

    /// <summary>Collects entries in ECMAScript own-property order; the last value of a repeated key wins (language.md, section 3.1).</summary>
    public sealed class Builder
    {
        private readonly List<KeyValuePair<string, OuiValue>> _indexKeys = [];
        private readonly List<KeyValuePair<string, OuiValue>> _stringKeys = [];
        private readonly Dictionary<string, (bool IsIndex, int Position)> _positions = new(StringComparer.Ordinal);

        /// <summary>The number of distinct keys so far (language.md, section 3.1).</summary>
        public int Count => _positions.Count;

        /// <summary>Sets <paramref name="key"/>; a repeated key keeps its first position (language.md, section 3.1).</summary>
        public Builder Set(string key, OuiValue value)
        {
            ArgumentNullException.ThrowIfNull(key);
            if (_positions.TryGetValue(key, out var slot))
            {
                var list = slot.IsIndex ? _indexKeys : _stringKeys;
                list[slot.Position] = new(key, value);
                return this;
            }

            var isIndex = IsArrayIndex(key, out _);
            var target = isIndex ? _indexKeys : _stringKeys;
            _positions[key] = (isIndex, target.Count);
            target.Add(new(key, value));
            return this;
        }

        /// <summary>The finished object (language.md, section 3.1).</summary>
        public OuiObject ToObject()
        {
            var entries = ImmutableArray.CreateBuilder<KeyValuePair<string, OuiValue>>(_positions.Count);
            entries.AddRange(_indexKeys.OrderBy(entry => uint.Parse(entry.Key, System.Globalization.CultureInfo.InvariantCulture)));
            entries.AddRange(_stringKeys);
            return new OuiObject(entries.MoveToImmutable());
        }
    }
}
