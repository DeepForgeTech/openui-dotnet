using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Evaluation;

/// <summary>
/// A list of components also takes one list of components as an item, as <c>@Each</c> gives in <c>Table([first, @Each(...)])</c>.
/// Upstream's runtime renders such a list; read strictly, language.md section 8.2 would remove it.
/// </summary>
public sealed class NestedComponentListTests
{
    private static OpenUILibrary Library { get; } = OpenUILibrary.CreateBuilder("tables").Root("Table")
        .Add(new ComponentSpec("Table", "", [new PropSpec("columns", new ArrayShape(new ComponentRefShape(["Col"])), true)]))
        .Add(new ComponentSpec("Col", "", [new PropSpec("name", TypeShape.String, true)]))
        .Build();

    private static RenderResult Render(string program) => OpenUIEvaluator.Evaluate(new OpenUIParser(Library).Parse(program));

    private static IEnumerable<string> Names(OuiValue value) =>
        value.TryGetComponent(out var column) ? [column["name"].Value.ToString()] : value.TryGetArray(out var items) ? items.SelectMany(Names) : [];

    [Fact]
    public void An_Each_beside_a_single_component_is_kept_as_a_list_in_the_list()
    {
        var render = Render("root = Table([Col(\"a\"), @Each([\"b\", \"c\"], \"n\", Col(n))])\n");

        Assert.Empty(render.Errors);
        Assert.True(render.Root!["columns"].Value.TryGetArray(out var columns));
        Assert.Equal(2, columns.Count);
        Assert.True(columns[1].TryGetArray(out var run));
        Assert.Equal(2, run.Count);
        Assert.Equal(["a", "b", "c"], Names(render.Root!["columns"].Value));
    }

    [Fact]
    public void A_list_two_levels_down_is_still_a_type_mismatch_and_left_out()
    {
        var render = Render("root = Table([Col(\"a\"), [[Col(\"b\")]]])\n");

        var error = Assert.Single(render.Errors);
        Assert.Equal(OpenUIErrorCode.TypeMismatch, error.Code);
        Assert.Equal("/columns/1/0", error.Path);
        Assert.Equal(["a"], Names(render.Root!["columns"].Value));
    }

    [Fact]
    public void Data_in_the_inner_list_is_reported_and_left_out()
    {
        var render = Render("root = Table([Col(\"a\"), @Each([1, 2], \"n\", \"plain\")])\n");

        Assert.All(render.Errors, error => Assert.Equal(OpenUIErrorCode.TypeMismatch, error.Code));
        Assert.Contains(render.Errors, error => error.Path == "/columns/1/0");
        Assert.Equal(["a"], Names(render.Root!["columns"].Value));
    }

    [Fact]
    public void A_nested_list_in_a_list_of_data_is_still_a_type_mismatch()
    {
        var tags = OpenUILibrary.CreateBuilder("tags").Root("Tags")
            .Add(new ComponentSpec("Tags", "", [new PropSpec("names", new ArrayShape(TypeShape.String), true)]))
            .Build();

        var render = OpenUIEvaluator.Evaluate(new OpenUIParser(tags).Parse("root = Tags([\"x\", [\"y\"]])\n"));

        Assert.Contains(render.Errors, error => error.Code == OpenUIErrorCode.TypeMismatch && error.Path == "/names/1");
        Assert.True(render.Root!["names"].Value.TryGetArray(out var names));
        Assert.Equal(["x"], names.Select(name => name.ToString()));
    }

    [Fact]
    public void The_signature_and_prompt_still_show_the_declared_list()
    {
        Assert.True(Library.TryGetComponent("Table", out var table));

        Assert.Equal("Table(columns: Col[])", table.Signature);
        Assert.Contains("Table(columns: Col[])", Prompting.SystemPrompt.Generate(Library.ToSpec()));
    }
}
