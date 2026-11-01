using System.Text.Json.Nodes;
using OpenUI.Lang.Core.Tests.Conformance;
using OpenUI.Lang.Library;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Evaluation;

/// <summary>Language.md sections 3.1 and 3.2 run through the evaluator: each row is an expression and the JSON of its value.</summary>
public sealed class CoercionTests
{
    private static OpenUILibrary Library { get; } = OpenUILibrary.CreateBuilder("coercion").Root("Box")
        .Add(new ComponentSpec("Box", "", [new PropSpec("value", TypeShape.Any, false)], default, "Box(value?: any)"))
        .Build();

    private static string Value(string expression, IReadOnlyDictionary<string, OuiValue>? state = null)
    {
        var render = OpenUIEvaluator.Evaluate(new OpenUIParser(Library).Parse($"root = Box({expression})\n"), new EvaluationInputs(state));
        Assert.Empty(render.Errors);
        return ConformanceAdapter.Summarize(render.Root!["value"].Value)?.ToJsonString() ?? "null";
    }

    [Theory]
    [InlineData("[5] * 2", "0")]
    [InlineData("""["3"] * 2""", "0")]
    [InlineData("[5] + 1", "1")]
    [InlineData("[5] - 1", "-1")]
    [InlineData("-[5]", "0")]
    [InlineData("[5] > 1", "false")]
    [InlineData("[5] < 1", "true")]
    [InlineData("[5] >= 0", "true")]
    [InlineData("{a: 5} * 2", "0")]
    [InlineData("Box(5) * 2", "0")]
    [InlineData("null * 2", "0")]
    [InlineData("true * 2", "2")]
    [InlineData("false + 1", "1")]
    [InlineData("\" 3 \" * 2", "6")]
    [InlineData("\"abc\" * 2", "0")]
    [InlineData("@Sum([[5], 1])", "1")]
    [InlineData("@Avg([[5], 1])", "0.5")]
    [InlineData("@Min([[5], 1])", "0")]
    [InlineData("@Max([[5], 1])", "1")]
    [InlineData("@Abs([5])", "0")]
    [InlineData("@Round([5])", "0")]
    [InlineData("@Floor([5])", "0")]
    [InlineData("@Ceil([5])", "0")]
    [InlineData("""@Count(@Filter([[5]], "", ">", 1))""", "0")]
    public void Arithmetic_and_comparison_use_toNumber_where_a_composite_is_zero(string expression, string expected) =>
        Assert.Equal(expected, Value(expression));

    [Theory]
    [InlineData("[5] + \"x\"", "\"5x\"")]
    [InlineData("null + \"x\"", "\"x\"")]
    [InlineData("{a: 1} + \"x\"", "\"[object Object]x\"")]
    [InlineData("1 + \"x\"", "\"1x\"")]
    [InlineData("[null, 1] + \"x\"", "\",1x\"")]
    [InlineData("[5] == 5", "true")]
    [InlineData("[5] == \"5\"", "true")]
    [InlineData("[5] != 6", "true")]
    [InlineData("[5] == [5]", "false")]
    [InlineData("null == 0", "false")]
    public void Joining_and_loose_equality_use_the_string_form_of_a_composite(string expression, string expected) =>
        Assert.Equal(expected, Value(expression));

    [Fact]
    public void Plus_through_the_evaluator_is_the_definition_in_Ecma()
    {
        var operands = OperandSet();
        var failures = new List<string>();
        foreach (var (a, b) in operands.SelectMany(a => operands.Select(b => (a, b))))
        {
            var state = new Dictionary<string, OuiValue> { ["$a"] = a, ["$b"] = b };
            var expected = ConformanceAdapter.Summarize(OpenUI.Lang.Values.Ecma.Add(a, b))?.ToJsonString() ?? "null";
            if (Value("$a + $b", state) != expected)
            {
                failures.Add($"{OpenUI.Lang.Values.Ecma.ToString(a)} + {OpenUI.Lang.Values.Ecma.ToString(b)}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void Loose_equality_through_the_evaluator_matches_the_JavaScript_matrix()
    {
        var data = ReferenceData.Read("Ecma", "values.json");
        var values = OperandSet();
        var rows = data["looseEquals"]!.AsArray().Select(node => (string)node!).ToList();
        var failures = new List<string>();
        foreach (var i in Representable(data))
        {
            foreach (var j in Representable(data))
            {
                var state = new Dictionary<string, OuiValue> { ["$a"] = values[i], ["$b"] = values[j] };
                var expected = rows[i][j] == '1' ? "true" : "false";
                if (Value("$a == $b", state) != expected)
                {
                    failures.Add($"#{i} == #{j}: JS {expected}");
                }
            }
        }

        Assert.Empty(failures);
    }

    private static List<OuiValue> OperandSet() =>
        ReferenceData.Read("Ecma", "values.json")["values"]!.AsArray().Select(descriptor => ReferenceValues.Read(descriptor!)).ToList();

    private static IEnumerable<int> Representable(JsonObject data) =>
        Enumerable.Range(0, data["values"]!.AsArray().Count)
            .Where(i => !data["values"]![i]!.ToJsonString().Contains("0x7ff8000000000000", StringComparison.Ordinal));
}
