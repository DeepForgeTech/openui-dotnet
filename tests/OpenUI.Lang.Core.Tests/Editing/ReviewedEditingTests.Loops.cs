namespace OpenUI.Lang.Core.Tests.Editing;

public partial class ReviewedEditingTests
{
    [Fact]
    public void A_loop_inside_a_referenced_statement_reads_its_own_items()
    {
        var root = OpenUIEvaluator.Evaluate(new OpenUIParser(Library).Parse(
            "root = Stack(@Each([1, 2], \"a\", child))\nchild = Stack(@Each([\"x\", \"y\"], \"b\", Text(b)))\n")).Root!;

        Assert.Equal("[x,y],[x,y]", TextsOf(root));
    }

    [Fact]
    public void A_loop_variable_stays_out_of_a_referenced_statement()
    {
        var root = OpenUIEvaluator.Evaluate(new OpenUIParser(Library).Parse(
            "root = Stack(@Each([\"o\"], \"a\", Stack([Text(a), other])))\nother = Text(\"\" + a)\n")).Root!;

        Assert.DoesNotContain("o,o", TextsOf(root));
    }
}
