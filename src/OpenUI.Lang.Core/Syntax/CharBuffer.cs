namespace OpenUI.Lang.Syntax;

/// <summary>A growable character buffer with O(1) indexing, for the prepared text the scanners share.</summary>
internal sealed class CharBuffer
{
    private char[] _chars = new char[256];

    public int Length { get; private set; }

    public char this[int index] => _chars[index];

    public CharBuffer Append(char c)
    {
        if (Length == _chars.Length)
        {
            Array.Resize(ref _chars, _chars.Length * 2);
        }

        _chars[Length++] = c;
        return this;
    }

    public CharBuffer Append(CharBuffer other)
    {
        for (var i = 0; i < other.Length; i++)
        {
            Append(other[i]);
        }

        return this;
    }

    public void Clear() => Length = 0;

    public string Substring(int start, int length) => new(_chars, start, length);

    public override string ToString() => new(_chars, 0, Length);
}
