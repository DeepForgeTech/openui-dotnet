using System.Text.Json;
using OpenUI.Lang.Core.Tests.Conformance;
using OpenUI.Lang.Editing;
using OpenUI.Lang.Library;
using OpenUI.Lang.Values;
using Xunit;

namespace OpenUI.Lang.Core.Tests.Editing;

public class EditingTests
{
    private static OpenUILibrary Library { get; } = ConformanceAdapter.CreateLibrary(FixtureCatalog.LoadLibrary());

    [Fact]
    public void Merge_reports_what_it_deleted_and_collected()
    {
        var result = ProgramMerger.Merge("root = Stack([a, b])\n$x = 1\na = Text(\"A\")\nb = Text(\"B\")\nc = Text(\"C\")", "root = Stack([a])\nb = null");
        Assert.Equal(new[] { "root", "$x", "a" }, result.Statements.AsEnumerable());
        Assert.Equal("b", Assert.Single(result.Deleted));
        Assert.Equal("c", Assert.Single(result.Collected));
        Assert.Equal("root = Stack([a])\n$x = 1\na = Text(\"A\")", result.Text);
    }

    [Fact]
    public void Merge_keeps_what_a_query_reaches_and_reads_multi_line_ternaries_as_one_statement()
    {
        var result = ProgramMerger.Merge("root = Stack([])\nq = Query(\"t\", {a: arg}, {})\narg = 1\nunused = 2", "root = $a\n  ? Stack([])\n  : Stack([])");
        Assert.Equal(new[] { "root", "q", "arg" }, result.Statements.AsEnumerable());
        Assert.Contains("? Stack([])", result.Text);
    }

    [Fact]
    public void Merge_reads_only_the_fenced_part_of_a_reply()
    {
        var result = ProgramMerger.Merge("root = Stack([a])\na = Text(\"A\")", "Here is the fix:\n```\na = Text(\"B\")\n```\nThat should do it.");
        Assert.Equal("root = Stack([a])\na = Text(\"B\")", result.Text);
    }

    [Fact]
    public void Serialize_writes_bindings_actions_host_state_and_trims_optional_nulls()
    {
        var source = "$q = \"\"\nroot = Stack([search, save, Tag(\"t\")])\nsearch = Input(\"s\", null, $q)\nsave = Button(\"Save\", Action([@Set($q, \"x\"), @ToAssistant(\"hi\", {a: 1})]))";
        var root = OpenUIEvaluator.Evaluate(new OpenUIParser(Library).Parse(source)).Root!;
        var text = OpenUISerializer.Serialize(root, new Dictionary<string, OuiValue> { ["$q"] = "kept" });

        Assert.StartsWith("root = Stack([search, save, Tag(\"t\")])", text);
        Assert.Contains("search = Input(\"s\", null, $q)", text);
        Assert.Contains("save = Button(\"Save\", Action([@Set($q, \"x\"), @ToAssistant(\"hi\", {a: 1})]))", text);
        Assert.EndsWith("$q = \"kept\"", text);

        var again = OpenUIEvaluator.Evaluate(new OpenUIParser(Library).Parse(text));
        Assert.Empty(again.Errors);
        Assert.Equal(ConformanceAdapter.Summarize(root)!.ToJsonString(), ConformanceAdapter.Summarize(again.Root)!.ToJsonString());
    }
}

public class ValidatorTests
{
    private static OpenUILibrary Library { get; } = ConformanceAdapter.CreateLibrary(FixtureCatalog.LoadLibrary());

    private static async IAsyncEnumerable<string> Chunks(params string[] chunks)
    {
        foreach (var chunk in chunks)
        {
            await Task.Yield();
            yield return chunk;
        }
    }

    [Fact]
    public void Validate_reports_errors_and_runs_no_tools()
    {
        var report = new OpenUIValidator(Library).Validate("root = Stack([Nope(\"x\"), Text(q.title)])\nq = Query(\"t\", {}, {title: \"d\"})");
        Assert.False(report.IsFatal);
        Assert.Equal("unknown-component", Assert.Single(report.Errors).Code.Value);
        Assert.NotNull(report.Render.Root);
    }

    [Fact]
    public void Errors_read_back_as_the_wire_shape_for_model_feedback()
    {
        var report = new OpenUIValidator(Library).Validate("root = Stack([Nope(\"x\")])");

        using var feedback = JsonDocument.Parse(report.Errors.ToModelFeedbackJson());

        var error = Assert.Single(feedback.RootElement.EnumerateArray());
        Assert.Equal("unknown-component", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(error.GetProperty("hint").GetString()));
    }

    [Fact]
    public async Task ValidateAsync_equals_Validate()
    {
        var validator = new OpenUIValidator(Library);
        var whole = validator.Validate("hello");
        var streamed = await validator.ValidateAsync(Chunks("hel", "lo"));
        Assert.True(whole.IsFatal);
        Assert.True(streamed.IsFatal);
        Assert.Equal(whole.Errors, streamed.Errors);
    }
}
