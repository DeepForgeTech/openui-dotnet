using System.Collections.Frozen;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace OpenUI.Lang.Values;

/// <summary>
/// The ECMA-262 semantics the language borrows (language.md, section 3.1): string-to-number parsing,
/// loose equality and number-to-string formatting, plus the pieces of <c>Math</c>, <c>parseFloat</c>
/// and <c>String.prototype</c> that built-ins and validation rules use. Every rule lives here, so the
/// evaluator never reimplements one.
/// </summary>
public static class Ecma
{
    private const NumberStyles DecimalStyles =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    /// <summary>
    /// The language's <c>toNumber</c> (section 3.1): a number is itself, a string is parsed with
    /// <see cref="StringToNumber"/>, <c>true</c> is 1, and everything else is 0. NaN becomes 0, so the
    /// result is never NaN.
    /// </summary>
    public static double ToNumber(OuiValue value)
    {
        var number = value.Kind switch
        {
            OuiValueKind.Number => value.RawNumber,
            OuiValueKind.String => StringToNumber((string)value.Payload!),
            OuiValueKind.Boolean => value.TryGetBoolean(out var b) && b ? 1 : 0,
            _ => 0,
        };
        return double.IsNaN(number) ? 0 : number;
    }

    /// <summary>
    /// ECMA-262 ToNumber, which loose equality uses (section 3.1). Unlike <see cref="ToNumber"/> it can
    /// return NaN, and an array or object goes through its string form (<c>Number([5])</c> is 5).
    /// </summary>
    public static double ToEcmaNumber(OuiValue value) => value.Kind switch
    {
        OuiValueKind.Null => 0,
        OuiValueKind.Boolean => value.TryGetBoolean(out var b) && b ? 1 : 0,
        OuiValueKind.Number => value.RawNumber,
        OuiValueKind.String => StringToNumber((string)value.Payload!),
        _ => StringToNumber(ToString(value)),
    };

    /// <summary>
    /// ECMA-262 StringToNumber (section 3.1): surrounding ECMAScript white space is ignored, an empty
    /// string is 0, <c>0x</c>, <c>0o</c> and <c>0b</c> literals are read without a sign, and
    /// <c>Infinity</c> may carry one. Anything else, including <c>"NaN"</c>, gives NaN.
    /// </summary>
    public static double StringToNumber(ReadOnlySpan<char> text)
    {
        var s = TrimWhiteSpace(text);
        if (s.IsEmpty)
        {
            return 0;
        }

        if (s.Length >= 2 && s[0] == '0' && RadixOf(s[1]) is var radix and > 0)
        {
            return ParseNonDecimalInteger(s[2..], radix);
        }

        return ScanDecimalLiteral(s) == s.Length ? ParseDecimalLiteral(s) : double.NaN;
    }

    /// <summary>
    /// ECMAScript <c>parseFloat</c>, which the <c>numeric</c>, <c>min</c> and <c>max</c> rules use
    /// (section 5.4): leading white space is skipped and the longest decimal prefix is read, so
    /// <c>"12abc"</c> is 12. NaN when no prefix is a number.
    /// </summary>
    public static double ParseFloat(ReadOnlySpan<char> text)
    {
        var s = text;
        while (!s.IsEmpty && IsWhiteSpace(s[0]))
        {
            s = s[1..];
        }

        var length = ScanDecimalLiteral(s);
        return length == 0 ? double.NaN : ParseDecimalLiteral(s[..length]);
    }

    /// <summary>ECMA-262 ToBoolean (section 3.1): arrays, objects and components are truthy.</summary>
    public static bool ToBoolean(OuiValue value) => value.Kind switch
    {
        OuiValueKind.Null => false,
        OuiValueKind.Boolean => value.TryGetBoolean(out var b) && b,
        OuiValueKind.Number => value.RawNumber != 0,
        OuiValueKind.String => ((string)value.Payload!).Length > 0,
        _ => true,
    };

    /// <summary>
    /// ECMAScript <c>String(value)</c> (section 3.1): numbers through <see cref="NumberToString"/>,
    /// arrays joined with commas (a null element is empty), and <c>[object Object]</c> for objects,
    /// components and actions. Null is <c>"null"</c>.
    /// </summary>
    public static string ToString(OuiValue value)
    {
        switch (value.Kind)
        {
            case OuiValueKind.Null:
                return "null";
            case OuiValueKind.Boolean:
                return value.TryGetBoolean(out var b) && b ? "true" : "false";
            case OuiValueKind.Number:
                return NumberToString(value.RawNumber);
            case OuiValueKind.String:
                return (string)value.Payload!;
            case OuiValueKind.Array:
                var builder = new StringBuilder();
                AppendJoined(builder, (OuiArray)value.Payload!);
                return builder.ToString();
            default:
                return "[object Object]";
        }
    }

    /// <summary>
    /// ECMA-262 Number::toString(10) (section 3.1): the shortest digits that read back as the same
    /// double, laid out as JavaScript does (<c>1e+21</c>, <c>1e-7</c>, <c>0.000001</c>). -0 is <c>"0"</c>.
    /// </summary>
    public static string NumberToString(double value)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (value == 0)
        {
            return "0";
        }

        if (double.IsInfinity(value))
        {
            return value > 0 ? "Infinity" : "-Infinity";
        }

        Span<char> digits = stackalloc char[24];
        var (digitCount, pointPosition) = ShortestDigits(Math.Abs(value), digits);
        digits = digits[..digitCount];

        Span<char> result = stackalloc char[48];
        var length = 0;
        if (value < 0)
        {
            result[length++] = '-';
        }

        // ECMA-262 Number::toString: value = 0.digits * 10^n, with k digits.
        var (k, n) = (digitCount, pointPosition);
        if (k <= n && n <= 21)
        {
            Append(result, ref length, digits);
            Append(result, ref length, '0', n - k);
        }
        else if (n > 0 && n <= 21)
        {
            Append(result, ref length, digits[..n]);
            result[length++] = '.';
            Append(result, ref length, digits[n..]);
        }
        else if (n > -6 && n <= 0)
        {
            Append(result, ref length, "0.");
            Append(result, ref length, '0', -n);
            Append(result, ref length, digits);
        }
        else
        {
            result[length++] = digits[0];
            if (k > 1)
            {
                result[length++] = '.';
                Append(result, ref length, digits[1..]);
            }

            var exponent = n - 1;
            result[length++] = 'e';
            result[length++] = exponent < 0 ? '-' : '+';
            Math.Abs(exponent).TryFormat(result[length..], out var written, provider: CultureInfo.InvariantCulture);
            length += written;
        }

        return new string(result[..length]);
    }

    /// <summary>
    /// ECMA-262 IsLooselyEqual, the language's <c>==</c> (section 3.2). Arrays, objects, components and
    /// actions are equal only to the same instance; against a string or number they compare through
    /// their string form. Null equals only null. Numbers compare with NaN unequal to everything.
    /// </summary>
    public static bool LooselyEquals(OuiValue left, OuiValue right)
    {
        if (IsComposite(left) && IsComposite(right))
        {
            return ReferenceEquals(left.Payload, right.Payload);
        }

        if (left.IsNull || right.IsNull)
        {
            return left.IsNull && right.IsNull;
        }

        // Booleans become numbers and composites become their primitive (string) form.
        var a = ToPrimitive(left);
        var b = ToPrimitive(right);
        if (a.Kind == OuiValueKind.String && b.Kind == OuiValueKind.String)
        {
            return string.Equals((string)a.Payload!, (string)b.Payload!, StringComparison.Ordinal);
        }

        return ToEcmaNumber(a) == ToEcmaNumber(b);
    }

    /// <summary>
    /// The language's <c>+</c> (section 3.2): when either side is a string it joins the string forms,
    /// with null as <c>""</c>; otherwise it adds with <see cref="ToNumber"/>.
    /// </summary>
    public static OuiValue Add(OuiValue left, OuiValue right)
    {
        if (left.Kind == OuiValueKind.String || right.Kind == OuiValueKind.String)
        {
            return string.Concat(left.IsNull ? "" : ToString(left), right.IsNull ? "" : ToString(right));
        }

        return ToNumber(left) + ToNumber(right);
    }

    /// <summary>
    /// ECMAScript <c>Math.round</c>, which <c>@Round</c> uses (section 3.4): a half rounds toward
    /// +Infinity, and a value in [-0.5, 0) gives -0.
    /// </summary>
    public static double MathRound(double value)
    {
        // value - floor(value) is exact, so this avoids the error in floor(value + 0.5).
        var floor = Math.Floor(value);
        var rounded = value - floor >= 0.5 ? floor + 1 : floor;
        return rounded == 0 && value < 0 ? -0.0 : rounded;
    }

    /// <summary>
    /// ECMAScript <c>String.prototype.toUpperCase</c> for library functions (section 3.6): the
    /// locale-independent full case mapping, so <c>ß</c> becomes <c>SS</c>.
    /// </summary>
    public static string ToUpperCase(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = default(StringBuilder);
        var runStart = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (SpecialUpperCase.TryGetValue(text[i], out var mapped))
            {
                builder ??= new StringBuilder(text.Length + 8);
                builder.Append(text.AsSpan(runStart, i - runStart).ToString().ToUpperInvariant()).Append(mapped);
                runStart = i + 1;
            }
        }

        return builder is null
            ? text.ToUpperInvariant()
            : builder.Append(text.AsSpan(runStart).ToString().ToUpperInvariant()).ToString();
    }

    /// <summary>ECMAScript WhiteSpace or LineTerminator: what StringToNumber trims and <c>\s</c> matches.</summary>
    internal static bool IsWhiteSpace(char c) => c switch
    {
        '\t' or '\n' or '\v' or '\f' or '\r' or ' ' or '\u00A0' or '\u1680' or '\u2028' or '\u2029'
            or '\u202F' or '\u205F' or '\u3000' or '\uFEFF' => true,
        >= '\u2000' and <= '\u200A' => true,
        _ => false,
    };

    internal static bool IsComposite(OuiValue value) =>
        value.Kind is OuiValueKind.Array or OuiValueKind.Object or OuiValueKind.Component or OuiValueKind.Action;

    // ECMA-262 ToPrimitive for loose equality: composites give their string, booleans their number.
    private static OuiValue ToPrimitive(OuiValue value) => value.Kind switch
    {
        OuiValueKind.Boolean => ToEcmaNumber(value),
        _ when IsComposite(value) => ToString(value),
        _ => value,
    };

    private static void AppendJoined(StringBuilder builder, OuiArray array)
    {
        for (var i = 0; i < array.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            var item = array[i];
            if (item.TryGetArray(out var nested))
            {
                AppendJoined(builder, nested);
            }
            else if (!item.IsNull)
            {
                builder.Append(ToString(item));
            }
        }
    }

    private static ReadOnlySpan<char> TrimWhiteSpace(ReadOnlySpan<char> text)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (end > start && IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        return text[start..end];
    }

    private static int RadixOf(char prefix) => prefix switch
    {
        'x' or 'X' => 16,
        'o' or 'O' => 8,
        'b' or 'B' => 2,
        _ => 0,
    };

    // A NonDecimalIntegerLiteral's digits, rounded to the nearest double (ties to even) as ECMA-262 requires.
    private static double ParseNonDecimalInteger(ReadOnlySpan<char> digits, int radix)
    {
        if (digits.IsEmpty)
        {
            return double.NaN;
        }

        foreach (var c in digits)
        {
            var digit = HexDigitValue(c);
            if (digit < 0 || digit >= radix)
            {
                return double.NaN;
            }
        }

        // The only part that needs big arithmetic is at most 1024 bits long: more is past every double.
        digits = digits.TrimStart('0');
        if ((digits.Length - 1L) * BitOperations.Log2((uint)radix) > 1024)
        {
            return double.PositiveInfinity;
        }

        var value = BigInteger.Zero;
        foreach (var c in digits)
        {
            value = (value * radix) + HexDigitValue(c);
        }

        var bitLength = (int)value.GetBitLength();
        if (bitLength <= 53)
        {
            return (double)value;
        }

        var shift = bitLength - 53;
        var mantissa = value >> shift;
        var remainder = value - (mantissa << shift);
        var half = BigInteger.One << (shift - 1);
        if (remainder > half || (remainder == half && !mantissa.IsEven))
        {
            mantissa += 1;
        }

        return Math.ScaleB((double)mantissa, shift);
    }

    private static int HexDigitValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    /// <summary>
    /// The length of the longest prefix of <paramref name="s"/> that is an ECMA-262 StrDecimalLiteral:
    /// an optional sign, then <c>Infinity</c> or ASCII digits with an optional fraction and exponent.
    /// 0 when there is none.
    /// </summary>
    private static int ScanDecimalLiteral(ReadOnlySpan<char> s)
    {
        var i = s.Length > 0 && s[0] is '+' or '-' ? 1 : 0;
        if (s[i..].StartsWith("Infinity", StringComparison.Ordinal))
        {
            return i + "Infinity".Length;
        }

        var integerDigits = CountDigits(s, i);
        i += integerDigits;
        var fractionDigits = 0;
        if (i < s.Length && s[i] == '.')
        {
            fractionDigits = CountDigits(s, i + 1);
            if (integerDigits + fractionDigits > 0)
            {
                i += 1 + fractionDigits;
            }
        }

        if (integerDigits + fractionDigits == 0)
        {
            return 0;
        }

        if (i < s.Length && s[i] is 'e' or 'E')
        {
            var j = i + 1 < s.Length && s[i + 1] is '+' or '-' ? i + 2 : i + 1;
            var exponentDigits = CountDigits(s, j);
            if (exponentDigits > 0)
            {
                i = j + exponentDigits;
            }
        }

        return i;
    }

    private static int CountDigits(ReadOnlySpan<char> s, int start)
    {
        var i = start;
        while (i < s.Length && char.IsAsciiDigit(s[i]))
        {
            i++;
        }

        return i - start;
    }

    // s is a whole StrDecimalLiteral; .NET's parse is correctly rounded and keeps -0.
    private static double ParseDecimalLiteral(ReadOnlySpan<char> s)
    {
        if (s.EndsWith("Infinity", StringComparison.Ordinal))
        {
            return s[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity;
        }

        return double.Parse(s, DecimalStyles, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Writes the shortest round-trip digits of a positive finite <paramref name="value"/>, without
    /// leading or trailing zeros, and returns their count and the decimal point position n, so that
    /// value = 0.digits * 10^n.
    /// </summary>
    private static (int Count, int PointPosition) ShortestDigits(double value, Span<char> digits)
    {
        Span<char> formatted = stackalloc char[32];
        value.TryFormat(formatted, out var length, "R", CultureInfo.InvariantCulture);
        formatted = formatted[..length];

        var exponentIndex = formatted.IndexOf('E');
        var exponent = exponentIndex < 0 ? 0 : int.Parse(formatted[(exponentIndex + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var mantissa = exponentIndex < 0 ? formatted : formatted[..exponentIndex];

        var count = 0;
        var pointPosition = 0;
        var afterPoint = false;
        foreach (var c in mantissa)
        {
            if (c == '.')
            {
                afterPoint = true;
            }
            else if (count == 0 && c == '0')
            {
                // A leading zero: "0.001" puts the point before the zeros that follow it.
                pointPosition -= afterPoint ? 1 : 0;
            }
            else
            {
                digits[count++] = c;
                pointPosition += afterPoint ? 0 : 1;
            }
        }

        while (count > 1 && digits[count - 1] == '0')
        {
            count--;
        }

        return (count, pointPosition + exponent);
    }

    private static void Append(Span<char> buffer, ref int length, ReadOnlySpan<char> text)
    {
        text.CopyTo(buffer[length..]);
        length += text.Length;
    }

    private static void Append(Span<char> buffer, ref int length, char c, int count)
    {
        buffer.Slice(length, count).Fill(c);
        length += count;
    }

    // SpecialCasing.txt (Unicode 16.0): the unconditional uppercase mappings longer than one code unit.
    // Every other character uses the simple mapping of ToUpperInvariant.
    private static readonly FrozenDictionary<char, string> SpecialUpperCase = new Dictionary<char, string>
    {
        ['\u00DF'] = "SS", ['\uFB00'] = "FF", ['\uFB01'] = "FI", ['\uFB02'] = "FL",
        ['\uFB03'] = "FFI", ['\uFB04'] = "FFL", ['\uFB05'] = "ST", ['\uFB06'] = "ST",
        ['\u0587'] = "\u0535\u0552", ['\uFB13'] = "\u0544\u0546", ['\uFB14'] = "\u0544\u0535", ['\uFB15'] = "\u0544\u053B",
        ['\uFB16'] = "\u054E\u0546", ['\uFB17'] = "\u0544\u053D", ['\u0149'] = "\u02BCN", ['\u0390'] = "\u0399\u0308\u0301",
        ['\u03B0'] = "\u03A5\u0308\u0301", ['\u01F0'] = "J\u030C", ['\u1E96'] = "H\u0331", ['\u1E97'] = "T\u0308",
        ['\u1E98'] = "W\u030A", ['\u1E99'] = "Y\u030A", ['\u1E9A'] = "A\u02BE", ['\u1F50'] = "\u03A5\u0313",
        ['\u1F52'] = "\u03A5\u0313\u0300", ['\u1F54'] = "\u03A5\u0313\u0301", ['\u1F56'] = "\u03A5\u0313\u0342", ['\u1FB6'] = "\u0391\u0342",
        ['\u1FC6'] = "\u0397\u0342", ['\u1FD2'] = "\u0399\u0308\u0300", ['\u1FD3'] = "\u0399\u0308\u0301", ['\u1FD6'] = "\u0399\u0342",
        ['\u1FD7'] = "\u0399\u0308\u0342", ['\u1FE2'] = "\u03A5\u0308\u0300", ['\u1FE3'] = "\u03A5\u0308\u0301", ['\u1FE4'] = "\u03A1\u0313",
        ['\u1FE6'] = "\u03A5\u0342", ['\u1FE7'] = "\u03A5\u0308\u0342", ['\u1FF6'] = "\u03A9\u0342", ['\u1F80'] = "\u1F08\u0399",
        ['\u1F81'] = "\u1F09\u0399", ['\u1F82'] = "\u1F0A\u0399", ['\u1F83'] = "\u1F0B\u0399", ['\u1F84'] = "\u1F0C\u0399",
        ['\u1F85'] = "\u1F0D\u0399", ['\u1F86'] = "\u1F0E\u0399", ['\u1F87'] = "\u1F0F\u0399", ['\u1F88'] = "\u1F08\u0399",
        ['\u1F89'] = "\u1F09\u0399", ['\u1F8A'] = "\u1F0A\u0399", ['\u1F8B'] = "\u1F0B\u0399", ['\u1F8C'] = "\u1F0C\u0399",
        ['\u1F8D'] = "\u1F0D\u0399", ['\u1F8E'] = "\u1F0E\u0399", ['\u1F8F'] = "\u1F0F\u0399", ['\u1F90'] = "\u1F28\u0399",
        ['\u1F91'] = "\u1F29\u0399", ['\u1F92'] = "\u1F2A\u0399", ['\u1F93'] = "\u1F2B\u0399", ['\u1F94'] = "\u1F2C\u0399",
        ['\u1F95'] = "\u1F2D\u0399", ['\u1F96'] = "\u1F2E\u0399", ['\u1F97'] = "\u1F2F\u0399", ['\u1F98'] = "\u1F28\u0399",
        ['\u1F99'] = "\u1F29\u0399", ['\u1F9A'] = "\u1F2A\u0399", ['\u1F9B'] = "\u1F2B\u0399", ['\u1F9C'] = "\u1F2C\u0399",
        ['\u1F9D'] = "\u1F2D\u0399", ['\u1F9E'] = "\u1F2E\u0399", ['\u1F9F'] = "\u1F2F\u0399", ['\u1FA0'] = "\u1F68\u0399",
        ['\u1FA1'] = "\u1F69\u0399", ['\u1FA2'] = "\u1F6A\u0399", ['\u1FA3'] = "\u1F6B\u0399", ['\u1FA4'] = "\u1F6C\u0399",
        ['\u1FA5'] = "\u1F6D\u0399", ['\u1FA6'] = "\u1F6E\u0399", ['\u1FA7'] = "\u1F6F\u0399", ['\u1FA8'] = "\u1F68\u0399",
        ['\u1FA9'] = "\u1F69\u0399", ['\u1FAA'] = "\u1F6A\u0399", ['\u1FAB'] = "\u1F6B\u0399", ['\u1FAC'] = "\u1F6C\u0399",
        ['\u1FAD'] = "\u1F6D\u0399", ['\u1FAE'] = "\u1F6E\u0399", ['\u1FAF'] = "\u1F6F\u0399", ['\u1FB3'] = "\u0391\u0399",
        ['\u1FBC'] = "\u0391\u0399", ['\u1FC3'] = "\u0397\u0399", ['\u1FCC'] = "\u0397\u0399", ['\u1FF3'] = "\u03A9\u0399",
        ['\u1FFC'] = "\u03A9\u0399", ['\u1FB2'] = "\u1FBA\u0399", ['\u1FB4'] = "\u0386\u0399", ['\u1FC2'] = "\u1FCA\u0399",
        ['\u1FC4'] = "\u0389\u0399", ['\u1FF2'] = "\u1FFA\u0399", ['\u1FF4'] = "\u038F\u0399", ['\u1FB7'] = "\u0391\u0342\u0399",
        ['\u1FC7'] = "\u0397\u0342\u0399", ['\u1FF7'] = "\u03A9\u0342\u0399",
    }.ToFrozenDictionary();
}

/// <summary>
/// String collation for <c>@Sort</c> (language.md, section 3.4): ECMAScript <c>a.localeCompare(b)</c>
/// with no arguments. Hosts may supply their own to pin a locale.
/// </summary>
public interface IOuiCollator
{
    /// <summary>Compares two strings: negative when <paramref name="left"/> sorts first, zero when equal, positive otherwise (language.md, section 3.4).</summary>
    int Compare(string left, string right);
}

/// <summary>
/// The default collator (section 3.4): the ICU root collation through <see cref="CompareInfo"/>, the
/// same order as JavaScript's <c>localeCompare</c> in an English locale, so <c>a</c> sorts before
/// <c>B</c>. It never falls back to ordinal comparison: under globalization-invariant mode, where .NET
/// has no collation data, <see cref="Compare"/> throws instead.
/// </summary>
public sealed class OuiCollator : IOuiCollator
{
    private static readonly CompareInfo Root = CultureInfo.InvariantCulture.CompareInfo;

    private OuiCollator()
    {
    }

    /// <summary>The default collator (section 3.4).</summary>
    public static OuiCollator Default { get; } = new();

    /// <summary>True when the runtime has linguistic collation; false in globalization-invariant mode, which compares ordinally.</summary>
    internal static bool IsLinguistic { get; } = Root.Compare("a", "B", CompareOptions.None) < 0;

    /// <summary>Compares with the ICU root collation (section 3.4).</summary>
    /// <exception cref="NotSupportedException">The runtime runs in globalization-invariant mode.</exception>
    public int Compare(string left, string right)
    {
        if (!IsLinguistic)
        {
            throw new NotSupportedException(
                "Locale collation is unavailable because .NET runs in globalization-invariant mode; supply an IOuiCollator.");
        }

        return Root.Compare(left, right, CompareOptions.None);
    }
}
