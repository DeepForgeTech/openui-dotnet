namespace OpenUI.Lang.Values;

/// <summary>
/// What a value costs when written out in full, a shared reference counted each time it appears.
/// Kept on each composite when it is built, so it never takes a walk to
/// read and a walk can be priced before it starts.
/// </summary>
/// <param name="Size">The value nodes it expands to, saturating.</param>
/// <param name="Depth">The composites nested in it, itself included; 0 for a scalar.</param>
/// <param name="Chars">The characters of all the text it expands to, keys and names included, saturating.</param>
internal readonly record struct ValueMeasure(int Size, int Depth, long Chars)
{
    public static readonly ValueMeasure Scalar = new(1, 0, 0);
}

/// <summary>Adds up a composite's children into its <see cref="ValueMeasure"/>.</summary>
internal struct MeasureBuilder()
{
    private long _size = 1;
    private int _depth;
    private long _chars;

    public void Add(OuiValue child)
    {
        var measure = child.Measure;
        _size += measure.Size;
        _depth = Math.Max(_depth, measure.Depth);
        AddText(measure.Chars);
    }

    /// <summary>Counts a key or a name the composite holds besides its children.</summary>
    public void AddText(string? text) => AddText((long)(text?.Length ?? 0));

    public readonly ValueMeasure ToMeasure() => new((int)Math.Min(_size, int.MaxValue), _depth + 1, _chars);

    private void AddText(long chars) => _chars = chars > long.MaxValue - _chars ? long.MaxValue : _chars + chars;
}
