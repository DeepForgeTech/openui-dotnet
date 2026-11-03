using Bunit;
using OpenUI.Lang.Blazor.Tests.TestComponents;
using OpenUI.Lang.Library;

namespace OpenUI.Lang.Blazor.Tests;

/// <summary>What a code-defined library checks, and the items and lists it lets a program write.</summary>
public sealed class LibraryDefinitionTests
{
    private static readonly ComponentRefShape Col = new(["Col"]);

    private static LibraryDefinition Table { get; } = new("tables", "Table",
    [
        new("All",
        [
            new("Table", "A table of columns.", typeof(ColumnsView), [new("columns", new ArrayShape(Col)) { Required = true }]),
            new("Col", "One column.", null, [new("name", TypeShape.String) { Required = true }]) { IsItem = true },
        ], []),
    ]);

    [Fact]
    public void FindProblems_lists_every_problem()
    {
        var bad = new LibraryDefinition("bad", "Box",
        [
            new("All",
            [
                new("Box", "A box.", typeof(BoxView), [new("title", TypeShape.String), new("child", TypeShape.Any) { Required = true }]),
                new("Row", "A row.", typeof(BoxView), [new("gap", TypeShape.Number), new("items", TypeShape.Any) { Required = true }]),
                new("Plain", "Not a view.", typeof(string), []),
            ], []),
        ]);

        var problems = bad.FindProblems();

        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, problem => problem.Contains("\"Box\"") && problem.Contains("required \"child\""));
        Assert.Contains(problems, problem => problem.Contains("\"Row\"") && problem.Contains("required \"items\""));
        Assert.Contains(problems, problem => problem.Contains("String") && problem.Contains("\"Plain\""));
    }

    [Fact]
    public void Registering_a_library_that_breaks_the_rules_still_fails()
    {
        var bad = new LibraryDefinition("bad", "Box",
        [
            new("All", [new("Box", "A box.", typeof(BoxView), [new("title", TypeShape.String), new("child", TypeShape.Any) { Required = true }])], []),
        ]);

        var error = Assert.Throws<OpenUILibraryException>(() => new OpenUIComponents([TestLibrary.Definition, bad]));

        Assert.Equal(bad.FindProblems(), error.Violations);
    }

    [Fact]
    public void FindProblems_is_empty_for_a_sound_library()
    {
        Assert.Empty(TestLibrary.Definition.FindProblems());
        Assert.Empty(TestLibrary.Mud.FindProblems());
        Assert.Empty(Table.FindProblems());
    }

    [Fact]
    public void A_component_with_no_view_that_is_not_an_item_is_missing_one()
    {
        var library = new LibraryDefinition("gaps", "Table",
        [
            new("All",
            [
                new("Table", "A table.", typeof(ColumnsView), [new("columns", new ArrayShape(Col)) { Required = true }]),
                new("Col", "One column.", null, [new("name", TypeShape.String) { Required = true }]),
            ], []),
        ]);

        Assert.Equal(["Col"], library.MissingViews);
        Assert.Contains(library.FindProblems(), problem => problem.Contains("\"Col\" has no view"));
    }

    [Fact]
    public void An_item_needs_no_view()
    {
        Assert.Empty(Table.MissingViews);
        Assert.Null(Table.Components.Find("Col"));
    }
}
