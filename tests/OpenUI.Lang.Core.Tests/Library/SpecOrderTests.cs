using OpenUI.Lang.Library;
using OpenUI.Lang.Values;
using Xunit;

namespace OpenUI.Lang.Core.Tests.Library;

/// <summary>prompt.md section 3.1: a spec read as data keeps its order; a library defined in code must not break it.</summary>
public class SpecOrderTests
{
    // Note has its required prop second, the shape of upstream's react-ui Text.
    private const string OutOfOrder = """
        {
          "root": "Note",
          "schema": { "$defs": { "Note": {
            "type": "object",
            "properties": { "tone": { "type": "string" }, "text": { "type": "string" } },
            "required": ["text"]
          } } },
          "components": { "Note": { "signature": "Note(tone?: string, text: string)", "description": "A note" } }
        }
        """;

    private static OpenUILibrary Read(string json = OutOfOrder) => OpenUILibrary.FromSpec(LibrarySpec.Parse(json));

    private static OuiComponent? Render(OpenUILibrary library, string source) =>
        OpenUIEvaluator.Evaluate(new OpenUIParser(library).Parse(source)).Root;

    [Fact]
    public void FromSpec_reads_an_out_of_order_spec_and_reports_the_break()
    {
        var warning = Assert.Single(Read().DefinitionWarnings);
        Assert.Contains("Note", warning);
        Assert.Contains("text", warning);
    }

    [Fact]
    public void Positional_arguments_bind_in_the_specs_own_order()
    {
        var note = Render(Read(), "root = Note(\"loud\", \"hi\")");

        Assert.NotNull(note);
        Assert.Equal((OuiValue)"loud", note["tone"].Value);
        Assert.Equal((OuiValue)"hi", note["text"].Value);
    }

    [Fact]
    public void One_argument_fills_the_first_prop_so_the_required_one_is_missing()
    {
        Assert.Null(Render(Read(), "root = Note(\"hi\")"));
    }

    [Fact]
    public void The_order_survives_writing_the_spec_back_out()
    {
        var note = Render(OpenUILibrary.FromSpec(Read().ToSpec()), "root = Note(\"loud\", \"hi\")");

        Assert.NotNull(note);
        Assert.Equal((OuiValue)"loud", note["tone"].Value);
    }

    [Fact]
    public void A_spec_in_order_has_no_warnings()
    {
        var inOrder = OutOfOrder.Replace("\"required\": [\"text\"]", "\"required\": [\"tone\", \"text\"]", StringComparison.Ordinal);

        Assert.Empty(Read(inOrder).DefinitionWarnings);
    }

    [Theory]
    [InlineData("note")] // not an uppercase identifier
    [InlineData("Query")] // reserved
    [InlineData("Count")] // a built-in name
    public void A_bad_name_in_a_spec_stays_fatal(string name)
    {
        var json = OutOfOrder.Replace("\"Note\"", $"\"{name}\"", StringComparison.Ordinal);

        Assert.Throws<OpenUILibraryException>(() => Read(json));
    }

    [Fact]
    public void The_builder_still_throws_on_the_same_component()
    {
        var note = Read().Components[0];

        var error = Assert.Throws<OpenUILibraryException>(() => OpenUILibrary.CreateBuilder().Add(note).Build());

        Assert.Contains(error.Violations, v => v.Contains("comes after an optional prop", StringComparison.Ordinal));
    }

    [Fact]
    public void Extend_keeps_an_inherited_break_as_a_warning_and_rejects_a_new_one()
    {
        var library = Read();
        var note = library.Components[0];

        Assert.Single(library.Extend(_ => { }).DefinitionWarnings);
        Assert.Throws<OpenUILibraryException>(() => library.Extend(e => e.AddComponent(note with { Name = "Other" })));
    }

    [Fact]
    public void Extend_drops_the_warning_of_a_removed_component()
    {
        var library = Read(OutOfOrder.Replace("\"root\": \"Note\",", "", StringComparison.Ordinal));

        Assert.Empty(library.Extend(e => e.RemoveComponent("Note")).DefinitionWarnings);
    }
}
