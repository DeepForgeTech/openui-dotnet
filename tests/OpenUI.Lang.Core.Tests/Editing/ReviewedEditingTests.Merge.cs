using OpenUI.Lang.Editing;

namespace OpenUI.Lang.Core.Tests.Editing;

public partial class ReviewedEditingTests
{
    [Fact]
    public void A_cut_off_patch_merged_in_the_middle_does_not_swallow_what_follows()
    {
        var merged = ProgramMerger.Merge("a = Text(\"x\")\nb = Text(\"y\")\nroot = Stack([a, b])\n", "a = Text(\"new");

        Assert.Equal(new[] { "a", "b", "root" }, Names(merged.Text));
        var root = OpenUIEvaluator.Evaluate(new OpenUIParser(Library).Parse(merged.Text)).Root!;
        Assert.Equal("new,y", TextsOf(root));
    }

    [Theory]
    [InlineData("a = Text(\"new\\", "new\\")]
    [InlineData("a = Text(\"new\\u00", "new")]
    [InlineData("a = Text(\"new\\\\u00", "new\\u00")]
    [InlineData("a = Text('new", "new")]
    [InlineData("a = Text(\"new  ", "new")]
    public void A_patch_cut_inside_a_string_reads_as_implicit_closing_reads_it(string patch, string expected)
    {
        var merged = ProgramMerger.Merge("a = Text(\"x\")\nb = Text(\"y\")\nroot = Stack([a, b])\n", patch);

        var root = OpenUIEvaluator.Evaluate(new OpenUIParser(Library).Parse(merged.Text)).Root!;
        Assert.Equal(expected + ",y", TextsOf(root));
    }

    [Fact]
    public void A_cut_off_bracket_in_a_patch_is_closed_where_it_was_cut()
    {
        var merged = ProgramMerger.Merge("a = Text(\"x\")\nb = Text(\"y\")\nroot = Stack([a, b])\n", "a = Stack([Text(\"new\")\nb = Text(\"z\")\n");

        Assert.Equal(new[] { "a", "b", "root" }, Names(merged.Text));
    }

    [Fact]
    public void A_cut_off_current_program_does_not_swallow_a_later_statement()
    {
        var merged = ProgramMerger.Merge("root = Stack([a])\na = Text(\"cut", "extra = Text(\"more\")\nroot = Stack([a, extra])\n");

        Assert.Equal(new[] { "root", "a", "extra" }, Names(merged.Text));
    }
}
