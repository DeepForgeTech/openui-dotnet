using OpenUI.Lang.Core.Tests.Conformance;
using OpenUI.Lang.Library;
using OpenUI.Lang.Semantics;

namespace OpenUI.Lang.Core.Tests.Evaluation;

/// <summary>The table of language.md section 3.4, one row per sentence: each row is an expression and the JSON of its value.</summary>
public sealed class BuiltinFunctionTests
{
    private const string Items = """[{n: 1, s: "ab"}, {n: 2, s: "Abc"}, {n: "2", s: "xyz"}, {n: 3}]""";
    private const string One = """{"n":1,"s":"ab"}""";
    private const string Two = """{"n":2,"s":"Abc"}""";
    private const string TwoText = """{"n":"2","s":"xyz"}""";
    private const string Three = """{"n":3}""";

    private static OpenUILibrary Library { get; } = OpenUILibrary.CreateBuilder("builtins").Root("Box")
        .Add(new ComponentSpec("Box", "", [new PropSpec("value", TypeShape.Any, false)], default, "Box(value?: any)"))
        .Build();

    private static string Value(string expression)
    {
        var render = OpenUIEvaluator.Evaluate(new OpenUIParser(Library).Parse($"root = Box({expression})\n"));
        Assert.Empty(render.Errors);
        return ConformanceAdapter.Summarize(render.Root!["value"].Value)?.ToJsonString() ?? "null";
    }

    [Theory]
    [InlineData("@Count([1, 2, 3])", "3")]
    [InlineData("@Count([])", "0")]
    [InlineData("""@Count("abc")""", "0")]
    [InlineData("@Count(null)", "0")]
    [InlineData("@Count({a: 1})", "0")]
    [InlineData("@Count(5)", "0")]
    public void Count_is_the_element_count_and_zero_for_non_arrays(string expression, string expected) =>
        Assert.Equal(expected, Value(expression));

    [Theory]
    [InlineData("@First([1, 2])", "1")]
    [InlineData("@First([])", "null")]
    [InlineData("""@First("abc")""", "null")]
    [InlineData("@First(null)", "null")]
    [InlineData("@Last([1, 2])", "2")]
    [InlineData("@Last([])", "null")]
    [InlineData("@Last(7)", "null")]
    public void First_and_last_read_the_ends_and_null_when_there_is_none(string expression, string expected) =>
        Assert.Equal(expected, Value(expression));

    [Theory]
    [InlineData("""@Sum([1, "2", true, null])""", "4")]
    [InlineData("@Sum([])", "0")]
    [InlineData("""@Sum("5")""", "0")]
    [InlineData("@Sum(null)", "0")]
    public void Sum_adds_with_to_number_and_is_zero_for_non_arrays(string expression, string expected) =>
        Assert.Equal(expected, Value(expression));

    [Theory]
    [InlineData("@Avg([1, 2, 6])", "3")]
    [InlineData("""@Avg(["2", "4"])""", "3")]
    [InlineData("@Avg([])", "0")]
    [InlineData("@Avg(5)", "0")]
    public void Avg_is_the_mean_with_to_number_and_zero_when_empty_or_not_an_array(string expression, string expected) =>
        Assert.Equal(expected, Value(expression));

    [Theory]
    [InlineData("""@Min([3, "1", 2])""", "1")]
    [InlineData("@Min([])", "0")]
    [InlineData("""@Min("x")""", "0")]
    [InlineData("""@Max([3, "10", 2])""", "10")]
    [InlineData("@Max([])", "0")]
    [InlineData("@Max(7)", "0")]
    public void Min_and_max_use_to_number_and_are_zero_when_empty_or_not_an_array(string expression, string expected) =>
        Assert.Equal(expected, Value(expression));

    [Theory]
    [InlineData("==", "2", $"[{Two},{TwoText}]")]
    [InlineData("!=", "2", $"[{One},{Three}]")]
    [InlineData(">", "2", $"[{Three}]")]
    [InlineData("<", "2", $"[{One}]")]
    [InlineData(">=", "2", $"[{Two},{TwoText},{Three}]")]
    [InlineData("<=", "2", $"[{One},{Two},{TwoText}]")]
    [InlineData("~=", "2", "[]")]
    public void Filter_compares_the_field_with_each_op(string op, string operand, string expected) =>
        Assert.Equal(expected, Value($"""@Filter({Items}, "n", "{op}", {operand})"""));

    [Theory]
    [InlineData("b", $"[{One},{Two}]")]
    [InlineData("A", $"[{Two}]")]
    [InlineData("xy", $"[{TwoText}]")]
    public void Filter_contains_is_a_case_sensitive_substring(string operand, string expected) =>
        Assert.Equal(expected, Value($"""@Filter({Items}, "s", "contains", "{operand}")"""));

    [Fact]
    public void Filter_contains_reads_the_field_as_a_string() =>
        Assert.Equal("""[{"n":12}]""", Value("""@Filter([{n: 12}, {n: 3}], "n", "contains", 1)"""));

    [Theory]
    [InlineData("""@Filter([{n: 1}, {m: 2}], "n")""", """[{"m":2}]""")]
    [InlineData("""@Filter([{n: 1}, {n: 2}], "n", null, 2)""", """[{"n":2}]""")]
    [InlineData("""@Filter([{n: 1}, {n: 2}], "n", 5, 2)""", "[]")]
    [InlineData("""@Filter([1, 2, 3], "", ">", 1)""", "[2,3]")]
    [InlineData("""@Filter(["a", "b"], "", "==", "b")""", """["b"]""")]
    [InlineData("""@Filter([{c: {n: "a"}}, {c: {n: "b"}}], "c.n", "==", "b")""", """[{"c":{"n":"b"}}]""")]
    [InlineData("""@Filter("abc", "", "==", "abc")""", "[]")]
    [InlineData("""@Filter(null, "", "==", null)""", "[]")]
    public void Filter_handles_a_missing_or_unknown_op_an_empty_field_dot_paths_and_non_arrays(string expression, string expected) =>
        Assert.Equal(expected, Value(expression));

    [Theory]
    [InlineData("""@Sort([3, "10", 2], "")""", """[2,3,"10"]""")]
    [InlineData("""@Sort([3, "10", 2], "", "desc")""", """["10",3,2]""")]
    [InlineData("""@Sort([3, 1, 2], "", "DESC")""", "[1,2,3]")]
    [InlineData("""@Sort(["B", "a", null], "")""", """[null,"a","B"]""")]
    [InlineData("""@Sort([{a: 2}, {a: 1}], "a")""", """[{"a":1},{"a":2}]""")]
    [InlineData("""@Sort([{c: {n: 2}}, {c: {n: 1}}], "c.n")""", """[{"c":{"n":1}},{"c":{"n":2}}]""")]
    [InlineData("""@Sort([{a: 1, i: 1}, {a: 0, i: 2}, {a: 1, i: 3}], "a")""", """[{"a":0,"i":2},{"a":1,"i":1},{"a":1,"i":3}]""")]
    [InlineData("""@Sort([{a: 1, i: 1}, {a: 0, i: 2}, {a: 1, i: 3}], "a", "desc")""", """[{"a":1,"i":1},{"a":1,"i":3},{"a":0,"i":2}]""")]
    [InlineData("""@Sort("abc", "")""", "\"abc\"")]
    [InlineData("""@Sort(5, "", "desc")""", "5")]
    public void Sort_is_stable_numeric_when_it_can_be_and_leaves_non_arrays_alone(string expression, string expected) =>
        Assert.Equal(expected, Value(expression));

    [Theory]
    [InlineData("@Round(2.5)", "3")]
    [InlineData("@Round(-2.5)", "-2")]
    [InlineData("@Round(2.4)", "2")]
    [InlineData("@Round(1.25, 1)", "1.3")]
    [InlineData("@Round(1234, -2)", "1200")]
    [InlineData("""@Round("2.6")""", "3")]
    [InlineData("@Round(null)", "0")]
    public void Round_rounds_a_half_up_to_the_given_places(string expression, string expected) =>
        Assert.Equal(expected, Value(expression));

    [Theory]
    [InlineData("@Abs(-2)", "2")]
    [InlineData("@Abs(2)", "2")]
    [InlineData("""@Abs("-3")""", "3")]
    [InlineData("@Abs(null)", "0")]
    [InlineData("@Floor(1.9)", "1")]
    [InlineData("@Floor(-1.1)", "-2")]
    [InlineData("""@Floor("1.5")""", "1")]
    [InlineData("@Ceil(1.1)", "2")]
    [InlineData("@Ceil(-1.9)", "-1")]
    [InlineData("""@Ceil("1.5")""", "2")]
    public void Abs_floor_and_ceil_use_to_number(string expression, string expected) =>
        Assert.Equal(expected, Value(expression));

    [Fact]
    public void Each_maps_the_array_through_its_template() =>
        Assert.Equal("[2,3]", Value("""@Each([1, 2], "i", i + 1)"""));
}
