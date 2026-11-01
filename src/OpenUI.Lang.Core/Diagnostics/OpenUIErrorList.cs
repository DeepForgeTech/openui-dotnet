using System.Buffers;
using System.Collections;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace OpenUI.Lang.Diagnostics;

/// <summary>
/// An immutable list of errors as the client emits it (language.md, section 8.3). Two lists are
/// equal when their errors match on every wire field, in order, which is the test for "the list
/// changed".
/// </summary>
public sealed class OpenUIErrorList : IReadOnlyList<OpenUIError>, IEquatable<OpenUIErrorList>
{
    private readonly ImmutableArray<OpenUIError> _errors;

    private OpenUIErrorList(ImmutableArray<OpenUIError> errors) => _errors = errors;

    /// <summary>The empty list, which signals recovery (language.md, section 8.3).</summary>
    public static OpenUIErrorList Empty { get; } = new([]);

    /// <summary>Creates a list holding <paramref name="errors"/> in order (language.md, section 8.3).</summary>
    public static OpenUIErrorList Create(IEnumerable<OpenUIError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var array = errors.ToImmutableArray();
        return array.IsEmpty ? Empty : new(array);
    }

    /// <summary>The number of errors (language.md, section 8.3).</summary>
    public int Count => _errors.Length;

    /// <summary>True when any error is fatal, so nothing renders (language.md, section 8.2).</summary>
    public bool HasFatal => _errors.Any(e => e.IsFatal);

    /// <summary>The error at <paramref name="index"/> (language.md, section 8.3).</summary>
    public OpenUIError this[int index] => _errors[index];

    /// <summary>Writes the list as a JSON array of wire-shape errors (language.md, section 8.3).</summary>
    public void WriteTo(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStartArray();
        foreach (var error in _errors)
        {
            error.WriteTo(writer);
        }

        writer.WriteEndArray();
    }

    /// <summary>True when both lists hold the same errors, compared on wire fields, in order (language.md, section 8.3).</summary>
    public bool Equals(OpenUIErrorList? other)
    {
        if (other is null || other.Count != Count)
        {
            return false;
        }

        for (var i = 0; i < Count; i++)
        {
            if (!_errors[i].WireEquals(other._errors[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as OpenUIErrorList);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var error in _errors)
        {
            hash.Add(error.Code);
            hash.Add(error.StatementId, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    /// <summary>The list as JSON text, ready to send to the model in the next request (prompt.md, section 6.2).</summary>
    public string ToModelFeedbackJson()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Returns an enumerator over the errors (language.md, section 8.3).</summary>
    public ImmutableArray<OpenUIError>.Enumerator GetEnumerator() => _errors.GetEnumerator();

    IEnumerator<OpenUIError> IEnumerable<OpenUIError>.GetEnumerator() => ((IEnumerable<OpenUIError>)_errors).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_errors).GetEnumerator();
}
