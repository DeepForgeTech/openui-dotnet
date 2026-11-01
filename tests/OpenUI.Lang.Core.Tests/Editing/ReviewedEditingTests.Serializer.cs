using OpenUI.Lang.Editing;

namespace OpenUI.Lang.Core.Tests.Editing;

public partial class ReviewedEditingTests
{
    [Theory]
    [InlineData("root = Stack(rows)\nrows = [Text(\"a\"), Text(\"b\")]\n", "a,b")]
    [InlineData("root = Stack(rows)\nrows = @Each([\"a\", \"b\"], \"t\", Text(t))\n", "a,b")]
    [InlineData("root = Stack([pick])\npick = true ? Text(\"a\") : Text(\"b\")\n", "a")]
    [InlineData("root = Stack([one, one])\none = Text(\"a\")\n", "a,a")]
    public void A_serialized_tree_parses_back_to_the_same_tree(string program, string expected)
    {
        var root = OpenUIEvaluator.Evaluate(new OpenUIParser(Library).Parse(program)).Root!;
        Assert.Equal(expected, TextsOf(root));

        var text = OpenUISerializer.Serialize(root);
        var again = OpenUIEvaluator.Evaluate(new OpenUIParser(Library).Parse(text)).Root!;

        Assert.Equal(expected, TextsOf(again));
    }

    [Fact]
    public void A_statement_with_components_inside_it_stays_a_statement()
    {
        var root = OpenUIEvaluator.Evaluate(new OpenUIParser(Library).Parse("root = Stack([card])\ncard = Stack([Text(\"a\"), Text(\"b\")])\n")).Root!;

        var text = OpenUISerializer.Serialize(root);

        Assert.Contains("card = Stack(", text, StringComparison.Ordinal);
        Assert.Contains("root = Stack([card])", text, StringComparison.Ordinal);
    }
}
