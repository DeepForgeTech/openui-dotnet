using System.Collections;
using System.Collections.Immutable;

namespace OpenUI.Lang.Values;

/// <summary>An immutable array value (language.md, section 3.1).</summary>
public sealed class OuiArray : IReadOnlyList<OuiValue>
{
    private readonly ImmutableArray<OuiValue> _items;

    private OuiArray(ImmutableArray<OuiValue> items)
    {
        _items = items;
        var measure = new MeasureBuilder();
        foreach (var item in items)
        {
            measure.Add(item);
        }

        Measure = measure.ToMeasure();
    }

    /// <summary>The empty array (language.md, section 3.1).</summary>
    public static OuiArray Empty { get; } = new([]);

    /// <summary>An array holding a copy of <paramref name="items"/> (section 3.1).</summary>
    public static OuiArray Create(params ReadOnlySpan<OuiValue> items) => new([.. items]);

    /// <summary>An array holding <paramref name="items"/> without copying (section 3.1).</summary>
    public static OuiArray Create(ImmutableArray<OuiValue> items) => new(items.IsDefault ? [] : items);

    /// <summary>The elements, in order (language.md, section 3.1).</summary>
    public ImmutableArray<OuiValue> Items => _items;

    /// <summary>The element count; <c>.length</c> in the language (section 3.3).</summary>
    public int Count => _items.Length;

    /// <summary>The element at <paramref name="index"/> (language.md, section 3.1).</summary>
    public OuiValue this[int index] => _items[index];

    /// <summary>What this array costs when expanded.</summary>
    internal ValueMeasure Measure { get; }

    /// <summary>Enumerates the elements in order (language.md, section 3.1).</summary>
    public ImmutableArray<OuiValue>.Enumerator GetEnumerator() => _items.GetEnumerator();

    IEnumerator<OuiValue> IEnumerable<OuiValue>.GetEnumerator() => ((IEnumerable<OuiValue>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_items).GetEnumerator();

    internal bool StructurallyEquals(OuiArray other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (Count != other.Count)
        {
            return false;
        }

        for (var i = 0; i < Count; i++)
        {
            if (!_items[i].Equals(other._items[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The ECMAScript <c>String(array)</c>: the elements joined by commas (section 3.1).</summary>
    public override string ToString() => Ecma.ToString(this);
}
