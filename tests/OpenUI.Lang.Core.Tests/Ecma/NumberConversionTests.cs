using EcmaScript = OpenUI.Lang.Values.Ecma;

namespace OpenUI.Lang.Core.Tests.Ecma;

/// <summary>Differential tests: the expected values were produced by running JavaScript (tools/ecma-reference).</summary>
public class NumberConversionTests
{
    private static string Describe(double value) => $"{ReferenceData.Bits(value)} ({value:R})";

    private static void AssertSameBits(string input, double expected, double actual) =>
        Assert.True(
            ReferenceData.Bits(expected) == ReferenceData.Bits(actual),
            $"{input.Replace("\n", "\\n")}: JS gives {Describe(expected)}, got {Describe(actual)}");

    [Fact]
    public void NumberToString_matches_JavaScript_for_every_reference_case()
    {
        var failures = ReferenceData.Cases("Ecma", "number-to-string.json")
            .Select(c => (Value: ReferenceData.Double((string)c["bits"]!), Expected: (string)c["expected"]!))
            .Select(c => (c.Value, c.Expected, Actual: EcmaScript.NumberToString(c.Value)))
            .Where(c => c.Expected != c.Actual)
            .Select(c => $"{ReferenceData.Bits(c.Value)}: JS '{c.Expected}', got '{c.Actual}'")
            .ToList();

        Assert.True(failures.Count == 0, $"{failures.Count} mismatches:\n{string.Join('\n', failures.Take(20))}");
    }

    [Theory]
    [InlineData(0.1 + 0.2, "0.30000000000000004")]
    [InlineData(1e21, "1e+21")]
    [InlineData(1e-7, "1e-7")]
    [InlineData(123e-20, "1.23e-18")]
    [InlineData(-0.0, "0")]
    [InlineData(9007199254740992.0, "9007199254740992")]
    [InlineData(5e-324, "5e-324")]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "Infinity")]
    [InlineData(double.NegativeInfinity, "-Infinity")]
    [InlineData(1e-6, "0.000001")]
    [InlineData(123456789012345680000.0, "123456789012345680000")]
    public void NumberToString_hard_cases(double value, string expected) =>
        Assert.Equal(expected, EcmaScript.NumberToString(value));

    [Fact]
    public void StringToNumber_matches_JavaScript_for_every_reference_case()
    {
        var failures = ReferenceData.Cases("Ecma", "string-to-number.json")
            .Select(c => (Input: (string)c["input"]!, Expected: ReferenceData.Double((string)c["bits"]!)))
            .Where(c => ReferenceData.Bits(c.Expected) != ReferenceData.Bits(EcmaScript.StringToNumber(c.Input)))
            .Select(c => $"'{c.Input.Replace("\n", "\\n")}': JS {Describe(c.Expected)}, got {Describe(EcmaScript.StringToNumber(c.Input))}")
            .ToList();

        Assert.True(failures.Count == 0, $"{failures.Count} mismatches:\n{string.Join('\n', failures.Take(20))}");
    }

    [Theory]
    [InlineData("", 0.0)]
    [InlineData("   ", 0.0)]
    [InlineData("0x1F", 31.0)]
    [InlineData("0b11", 3.0)]
    [InlineData("0o7", 7.0)]
    [InlineData(".5", 0.5)]
    [InlineData("5.", 5.0)]
    [InlineData("+5", 5.0)]
    [InlineData("\x00a012\x3000", 12.0)]
    [InlineData("\xfeff12", 12.0)]
    [InlineData("Infinity", double.PositiveInfinity)]
    [InlineData("-Infinity", double.NegativeInfinity)]
    public void StringToNumber_accepts(string input, double expected) =>
        Assert.Equal(expected, EcmaScript.StringToNumber(input));

    [Theory]
    [InlineData("1e")]
    [InlineData("-0x10")]
    [InlineData("1_000")]
    [InlineData("١٢")]
    [InlineData("１２")]
    [InlineData("\u00851")]
    [InlineData("\x180e1")]
    [InlineData("infinity")]
    [InlineData("0x")]
    [InlineData("1 2")]
    public void StringToNumber_rejects(string input) =>
        Assert.True(double.IsNaN(EcmaScript.StringToNumber(input)));

    [Fact]
    public void StringToNumber_keeps_negative_zero() =>
        Assert.Equal("0x8000000000000000", ReferenceData.Bits(EcmaScript.StringToNumber("-0")));

    [Fact]
    public void ParseFloat_matches_JavaScript_for_every_reference_case()
    {
        var failures = ReferenceData.Cases("Ecma", "parse-float.json")
            .Select(c => (Input: (string)c["input"]!, Expected: ReferenceData.Double((string)c["bits"]!)))
            .Where(c => ReferenceData.Bits(c.Expected) != ReferenceData.Bits(EcmaScript.ParseFloat(c.Input)))
            .Select(c => $"'{c.Input.Replace("\n", "\\n")}': JS {Describe(c.Expected)}, got {Describe(EcmaScript.ParseFloat(c.Input))}")
            .ToList();

        Assert.True(failures.Count == 0, $"{failures.Count} mismatches:\n{string.Join('\n', failures.Take(20))}");
    }

    [Theory]
    [InlineData("12abc", 12.0)]
    [InlineData("  3.5kg", 3.5)]
    [InlineData("1e3x", 1000.0)]
    [InlineData("1e", 1.0)]
    [InlineData("0x1F", 0.0)]
    [InlineData("-.5e1", -5.0)]
    [InlineData("Infinityx", double.PositiveInfinity)]
    public void ParseFloat_reads_the_longest_prefix(string input, double expected) =>
        Assert.Equal(expected, EcmaScript.ParseFloat(input));

    [Fact]
    public void MathRound_matches_JavaScript_for_every_reference_case()
    {
        var failures = ReferenceData.Cases("Ecma", "math-round.json")
            .Select(c => (Input: ReferenceData.Double((string)c["bits"]!), Expected: ReferenceData.Double((string)c["expected"]!)))
            .Where(c => ReferenceData.Bits(c.Expected) != ReferenceData.Bits(EcmaScript.MathRound(c.Input)))
            .Select(c => $"{Describe(c.Input)}: JS {Describe(c.Expected)}, got {Describe(EcmaScript.MathRound(c.Input))}")
            .ToList();

        Assert.True(failures.Count == 0, $"{failures.Count} mismatches:\n{string.Join('\n', failures.Take(20))}");
    }

    [Theory]
    [InlineData(-0.5, "0x8000000000000000")]
    [InlineData(2.5, "0x4008000000000000")]
    [InlineData(-2.5, "0xc000000000000000")]
    [InlineData(0.49999999999999994, "0x0000000000000000")]
    public void MathRound_hard_cases(double value, string expectedBits) =>
        Assert.Equal(expectedBits, ReferenceData.Bits(EcmaScript.MathRound(value)));
}
