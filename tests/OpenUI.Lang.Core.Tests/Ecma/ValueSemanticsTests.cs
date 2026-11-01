using OpenUI.Lang.Values;
using EcmaScript = OpenUI.Lang.Values.Ecma;

namespace OpenUI.Lang.Core.Tests.Ecma;

/// <summary>String(v), Number(v) and v == w over the operand set in values.json, checked against JavaScript.</summary>
public class ValueSemanticsTests
{
    private static readonly System.Text.Json.Nodes.JsonObject Data = ReferenceData.Read("Ecma", "values.json");

    // Built once so reference equality between slots behaves like distinct JS objects.
    private static readonly List<OuiValue> Values =
        Data["values"]!.AsArray().Select(descriptor => ReferenceValues.Read(descriptor!)).ToList();

    // NaN cannot be an OuiValue: the factory makes it 0 (section 3.1). JavaScript's answers for those
    // operands have no value to run on here; Ecma's double overloads keep them (NumberConversionTests).
    private static readonly List<int> Representable = Enumerable.Range(0, Values.Count)
        .Where(i => !Data["values"]![i]!.ToJsonString().Contains("0x7ff8000000000000", StringComparison.Ordinal))
        .ToList();

    private static string Label(int index) => $"#{index} {Data["values"]![index]!.ToJsonString()}";

    [Fact]
    public void ToJsString_matches_JavaScript()
    {
        var expected = Data["toString"]!.AsArray().Select(node => (string)node!).ToList();
        var failures = Representable
            .Where(i => EcmaScript.ToString(Values[i]) != expected[i])
            .Select(i => $"{Label(i)}: JS '{expected[i]}', got '{EcmaScript.ToString(Values[i])}'")
            .ToList();

        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    [Fact]
    public void ToJsNumber_matches_JavaScript()
    {
        var expected = Data["toNumber"]!.AsArray().Select(node => (string)node!).ToList();
        var failures = Representable
            .Where(i => ReferenceData.Bits(EcmaScript.ToEcmaNumber(Values[i])) != expected[i])
            .Select(i => $"{Label(i)}: JS {expected[i]}, got {ReferenceData.Bits(EcmaScript.ToEcmaNumber(Values[i]))}")
            .ToList();

        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    [Fact]
    public void IsLooselyEqual_matches_the_JavaScript_matrix()
    {
        var rows = Data["looseEquals"]!.AsArray().Select(node => (string)node!).ToList();
        var failures = new List<string>();

        foreach (var i in Representable)
        {
            foreach (var j in Representable)
            {
                var expected = rows[i][j] == '1';
                if (EcmaScript.LooselyEquals(Values[i], Values[j]) != expected)
                {
                    failures.Add($"{Label(i)} == {Label(j)}: JS {expected}");
                }
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} mismatches:\n{string.Join('\n', failures.Take(20))}");
    }

    [Fact]
    public void IsLooselyEqual_compares_a_non_array_object_by_reference()
    {
        OuiValue first = OuiObject.Create([KeyValuePair.Create("a", (OuiValue)1.0)]);
        OuiValue second = OuiObject.Create([KeyValuePair.Create("a", (OuiValue)1.0)]);

        Assert.True(EcmaScript.LooselyEquals(first, first));
        Assert.False(EcmaScript.LooselyEquals(first, second));
        Assert.Equal("[object Object]", EcmaScript.ToString(first));
        Assert.True(EcmaScript.LooselyEquals(first, "[object Object]"));
    }

    [Fact]
    public void ToNumber_never_gives_NaN_and_reads_only_scalars()
    {
        Assert.Equal(0, EcmaScript.ToNumber("abc"));
        Assert.Equal(0, EcmaScript.ToNumber(double.NaN));
        Assert.Equal(0, EcmaScript.ToNumber(OuiArray.Create(5.0)));
        Assert.Equal(1, EcmaScript.ToNumber(true));
        Assert.Equal(16, EcmaScript.ToNumber(" 0x10 "));
    }

    [Fact]
    public void Add_joins_when_either_side_is_a_string_and_adds_otherwise()
    {
        Assert.Equal((OuiValue)"a", EcmaScript.Add("a", OuiValue.Null));
        Assert.Equal((OuiValue)"12", EcmaScript.Add(1.0, "2"));
        Assert.Equal((OuiValue)"1,2x", EcmaScript.Add(OuiArray.Create(1.0, 2.0), "x"));
        Assert.Equal((OuiValue)2.0, EcmaScript.Add(true, 1.0));
        Assert.Equal((OuiValue)1.0, EcmaScript.Add(OuiArray.Create(5.0), 1.0));
        Assert.Equal((OuiValue)0.0, EcmaScript.Add(double.PositiveInfinity, double.NegativeInfinity));
    }

    [Theory]
    [InlineData("straße", "STRASSE")]
    [InlineData("ﬁx", "FIX")]
    [InlineData("ŉ", "ʼN")]
    [InlineData("café", "CAFÉ")]
    public void ToUpperCase_uses_the_full_case_mapping(string text, string expected) =>
        Assert.Equal(expected, EcmaScript.ToUpperCase(text));

    [Fact]
    public void ToJsString_prints_nested_arrays_like_join()
    {
        OuiValue nested = OuiArray.Create(1.0, OuiArray.Create(2.0, OuiValue.Null, "x"), OuiValue.Null, true);

        Assert.Equal("1,2,,x,,true", EcmaScript.ToString(nested));
    }
}
