using System.Diagnostics.CodeAnalysis;

namespace OpenUI.Lang.Values;

/// <summary>The kinds of runtime value (language.md, section 3.1).</summary>
public enum OuiValueKind
{
    /// <summary>The <c>null</c> value. <c>default(OuiValue)</c> is null (language.md, section 3.1).</summary>
    Null,

    /// <summary><c>true</c> or <c>false</c> (language.md, section 3.1).</summary>
    Boolean,

    /// <summary>An IEEE 754 double, as in ECMAScript (language.md, section 3.1).</summary>
    Number,

    /// <summary>A string of UTF-16 code units, as in ECMAScript (language.md, section 3.1).</summary>
    String,

    /// <summary>An <see cref="OuiArray"/> (language.md, section 3.1).</summary>
    Array,

    /// <summary>An <see cref="OuiObject"/> (language.md, section 3.1).</summary>
    Object,

    /// <summary>An evaluated component instance, <see cref="OuiComponent"/> (language.md, section 3.1).</summary>
    Component,

    /// <summary>An action value, <see cref="OuiAction"/> (language.md, section 6.3).</summary>
    Action,
}

/// <summary>
/// One runtime value of OpenUI Lang (language.md, section 3.1): null, a boolean, a number, a string,
/// an array, an object, a component instance or an action. Numbers and booleans are held inline, so
/// arithmetic never boxes; composite payloads are immutable, which keeps every value acyclic.
/// </summary>
/// <remarks>
/// <see cref="Equals(OuiValue)"/> is structural deep equality. ECMAScript loose equality (<c>==</c>
/// in the language, section 3.2) is <see cref="Ecma.LooselyEquals"/>; this type does not overload
/// the <c>==</c> operator so the two cannot be confused.
/// </remarks>
public readonly partial struct OuiValue : IEquatable<OuiValue>
{
    private static readonly object NumberTag = new();
    private static readonly object TrueTag = new();
    private static readonly object FalseTag = new();

    private readonly object? _payload;
    private readonly double _number;

    private OuiValue(object? payload, double number = 0)
    {
        _payload = payload;
        _number = number;
    }

    /// <summary>The null value (section 3.1).</summary>
    public static OuiValue Null => default;

    /// <summary>The boolean <c>true</c> (language.md, section 3.1).</summary>
    public static OuiValue True { get; } = new(TrueTag);

    /// <summary>The boolean <c>false</c> (language.md, section 3.1).</summary>
    public static OuiValue False { get; } = new(FalseTag);

    /// <summary>The kind of this value (section 3.1).</summary>
    public OuiValueKind Kind => _payload switch
    {
        null => OuiValueKind.Null,
        string => OuiValueKind.String,
        OuiArray => OuiValueKind.Array,
        OuiObject => OuiValueKind.Object,
        OuiComponent => OuiValueKind.Component,
        OuiAction => OuiValueKind.Action,
        _ when ReferenceEquals(_payload, NumberTag) => OuiValueKind.Number,
        _ => OuiValueKind.Boolean,
    };

    /// <summary>True for the null value (language.md, section 3.1).</summary>
    public bool IsNull => _payload is null;

    /// <summary>
    /// A number value (section 3.1). NaN never appears in the language, so it becomes 0 here, the one
    /// place every number passes through. Everything else, negative zero included, is kept exactly.
    /// </summary>
    public static OuiValue Number(double value) => new(NumberTag, double.IsNaN(value) ? 0 : value);

    /// <summary>A string value (section 3.1).</summary>
    public static OuiValue String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(value);
    }

    /// <summary>A boolean value (section 3.1).</summary>
    public static OuiValue Boolean(bool value) => value ? True : False;

    /// <summary>Wraps a composite or null payload; internal callers know the payload type.</summary>
    internal static OuiValue FromPayload(object? payload) => new(payload);

    /// <summary>A number value (section 3.1).</summary>
    public static implicit operator OuiValue(double value) => Number(value);

    /// <summary>A string value, or null for a null string (section 3.1).</summary>
    public static implicit operator OuiValue(string? value) => value is null ? Null : new(value);

    /// <summary>A boolean value (section 3.1).</summary>
    public static implicit operator OuiValue(bool value) => Boolean(value);

    /// <summary>An array value, or null for a null array (section 3.1).</summary>
    public static implicit operator OuiValue(OuiArray? value) => new(value);

    /// <summary>An object value, or null for a null object (section 3.1).</summary>
    public static implicit operator OuiValue(OuiObject? value) => new(value);

    /// <summary>A component value, or null for a null component (section 3.1).</summary>
    public static implicit operator OuiValue(OuiComponent? value) => new(value);

    /// <summary>An action value, or null for a null action (section 6.3).</summary>
    public static implicit operator OuiValue(OuiAction? value) => new(value);

    /// <summary>Reads a number (section 3.1). No coercion: use <see cref="Ecma.ToNumber"/> for that.</summary>
    public bool TryGetNumber(out double value)
    {
        value = _number;
        return ReferenceEquals(_payload, NumberTag);
    }

    /// <summary>Reads a boolean (section 3.1). No coercion: use <see cref="Ecma.ToBoolean"/> for truthiness.</summary>
    public bool TryGetBoolean(out bool value)
    {
        value = ReferenceEquals(_payload, TrueTag);
        return value || ReferenceEquals(_payload, FalseTag);
    }

    /// <summary>Reads a string (section 3.1). No coercion: use <see cref="Ecma.ToString(OuiValue)"/> for that.</summary>
    public bool TryGetString([NotNullWhen(true)] out string? value)
    {
        value = _payload as string;
        return value is not null;
    }

    /// <summary>Reads an array (section 3.1).</summary>
    public bool TryGetArray([NotNullWhen(true)] out OuiArray? value)
    {
        value = _payload as OuiArray;
        return value is not null;
    }

    /// <summary>Reads an object (section 3.1).</summary>
    public bool TryGetObject([NotNullWhen(true)] out OuiObject? value)
    {
        value = _payload as OuiObject;
        return value is not null;
    }

    /// <summary>Reads a component instance (section 3.1).</summary>
    public bool TryGetComponent([NotNullWhen(true)] out OuiComponent? value)
    {
        value = _payload as OuiComponent;
        return value is not null;
    }

    /// <summary>Reads an action (section 6.3).</summary>
    public bool TryGetAction([NotNullWhen(true)] out OuiAction? value)
    {
        value = _payload as OuiAction;
        return value is not null;
    }

    /// <summary>The payload of a string or composite value, or the tag object of a scalar. Internal fast path.</summary>
    internal object? Payload => _payload;

    /// <summary>The number of a number value; 0 for other kinds. Internal fast path.</summary>
    internal double RawNumber => _number;

    /// <summary>What this value costs when written out in full.</summary>
    internal ValueMeasure Measure => _payload switch
    {
        string text => new ValueMeasure(1, 0, text.Length),
        OuiArray array => array.Measure,
        OuiObject obj => obj.Measure,
        OuiComponent component => component.Measure,
        OuiAction action => action.Measure,
        _ => ValueMeasure.Scalar,
    };

    /// <summary>
    /// Structural deep equality: same kind, and equal numbers, strings or members. 0 equals -0 here.
    /// This is not the language's <c>==</c> (see <see cref="Ecma.LooselyEquals"/>) (language.md, section 3.1).
    /// </summary>
    public bool Equals(OuiValue other)
    {
        if (ReferenceEquals(_payload, other._payload))
        {
            return !ReferenceEquals(_payload, NumberTag) || _number.Equals(other._number);
        }

        return (_payload, other._payload) switch
        {
            (string a, string b) => string.Equals(a, b, StringComparison.Ordinal),
            (OuiArray a, OuiArray b) => a.StructurallyEquals(b),
            (OuiObject a, OuiObject b) => a.StructurallyEquals(b),
            (OuiComponent a, OuiComponent b) => a.StructurallyEquals(b),
            (OuiAction a, OuiAction b) => a.StructurallyEquals(b),
            _ => false,
        };
    }

    /// <inheritdoc cref="Equals(OuiValue)"/>
    public override bool Equals(object? obj) => obj is OuiValue other && Equals(other);

    /// <summary>A hash consistent with structural <see cref="Equals(OuiValue)"/> (language.md, section 3.1).</summary>
    public override int GetHashCode() => _payload switch
    {
        null => 0,
        string s => StringComparer.Ordinal.GetHashCode(s),
        _ when ReferenceEquals(_payload, NumberTag) => _number == 0 ? 17 : _number.GetHashCode(),
        OuiArray array => array.Count,
        OuiObject obj => obj.Count + 31,
        OuiComponent component => StringComparer.Ordinal.GetHashCode(component.Type),
        OuiAction action => action.Steps.Length + 41,
        _ => _payload.GetHashCode(),
    };

    /// <summary>The ECMAScript <c>String(value)</c> of this value (language.md, section 3.1).</summary>
    public override string ToString() => Ecma.ToString(this);
}
