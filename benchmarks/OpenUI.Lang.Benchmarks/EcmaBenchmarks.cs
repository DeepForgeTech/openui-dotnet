using BenchmarkDotNet.Attributes;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Benchmarks;

/// <summary>
/// The ECMAScript conversions the language borrows (language.md, section 3.1). Each method makes one
/// pass over a fixed list of 24 inputs of the kinds a model writes, and the time is reported per input.
/// </summary>
[MemoryDiagnoser]
public class EcmaBenchmarks
{
    private const int Inputs = 24;

    private static readonly string[] NumericText =
    [
        "0", "1", "42", "-7", "3.14159", "  12  ", "1e3", "2.5e-4", "1e21", "0x1F", "0b101", "0o17",
        "Infinity", "-Infinity", "", "abc", "12abc", "1,000", ".5", "5.", "007", "9007199254740993", "-0", "1_000",
    ];

    private static readonly double[] Numbers =
    [
        0, 1, 42, -7, 0.1, 0.30000000000000004, 3.14159, 1e21, 1.5e-7, 123456789.123, 1e300, -0.000001,
        100, 1234567890123456789, 5e-324, double.MaxValue, 2.5, 1000000, 0.5, 99.99, 1 / 3.0, 255, 65536, 1e-6,
    ];

    private static readonly OuiValue[] Values =
    [
        OuiValue.Null, true, false, 0, 1, "", "0", "1", "abc", " ", "5", "true", OuiArray.Create(5), OuiArray.Create(1, 2),
        OuiArray.Empty, OuiObject.Empty, 5, "5.0", -1, "x", OuiArray.Create("a", "b"), 2.5, "2.5", OuiArray.Create(OuiValue.Null),
    ];

    private static readonly string[] Words =
    [
        "apple", "Apple", "banana", "cherry", "date", "Elder", "fig", "grape", "kiwi", "lemon", "mango", "nectarine",
        "orange", "papaya", "quince", "raspberry", "strawberry", "tangerine", "ugli", "vanilla", "watermelon", "xigua", "yam", "zucchini",
    ];

    [GlobalSetup]
    public void Setup() =>
        Check.That(NumericText.Length == Inputs && Numbers.Length == Inputs && Values.Length == Inputs && Words.Length == Inputs, "the inputs are not 24 each");

    [Benchmark(OperationsPerInvoke = Inputs)]
    public double StringToNumber()
    {
        var sum = 0.0;
        foreach (var text in NumericText)
        {
            var number = Ecma.StringToNumber(text);
            sum += double.IsNaN(number) ? 0 : number;
        }

        return sum;
    }

    [Benchmark(OperationsPerInvoke = Inputs)]
    public int NumberToString()
    {
        var length = 0;
        foreach (var number in Numbers)
        {
            length += Ecma.NumberToString(number).Length;
        }

        return length;
    }

    /// <summary>Loose equality of each value with the one after it.</summary>
    [Benchmark(OperationsPerInvoke = Inputs)]
    public int LooselyEquals()
    {
        var equal = 0;
        for (var i = 0; i < Values.Length; i++)
        {
            equal += Ecma.LooselyEquals(Values[i], Values[(i + 1) % Values.Length]) ? 1 : 0;
        }

        return equal;
    }

    /// <summary>The <c>+</c> operator on each value and the one after it.</summary>
    [Benchmark(OperationsPerInvoke = Inputs)]
    public int Add()
    {
        var kinds = 0;
        for (var i = 0; i < Values.Length; i++)
        {
            kinds += (int)Ecma.Add(Values[i], Values[(i + 1) % Values.Length]).Kind;
        }

        return kinds;
    }

    [Benchmark(OperationsPerInvoke = Inputs)]
    public int ValueToString()
    {
        var length = 0;
        foreach (var value in Values)
        {
            length += Ecma.ToString(value).Length;
        }

        return length;
    }

    /// <summary>Each word against the one after it, as <c>@Sort</c> compares strings.</summary>
    [Benchmark(OperationsPerInvoke = Inputs)]
    public int LocaleCompare()
    {
        var order = 0;
        for (var i = 0; i < Words.Length; i++)
        {
            order += OuiCollator.Default.Compare(Words[i], Words[(i + 1) % Words.Length]);
        }

        return order;
    }
}
