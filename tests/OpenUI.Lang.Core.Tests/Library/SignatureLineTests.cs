using System.Collections.Immutable;
using System.Text.Json;
using OpenUI.Lang.Core.Tests.Prompt;
using OpenUI.Lang.Library;
using OpenUI.Lang.Prompting;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Semantics;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Library;

/// <summary>Signature lines printed from a declaration (prompt.md, sections 2.4 and 5.1).</summary>
public sealed class SignatureLineTests
{
    private static string Printed(TypeShape shape) => new ComponentSpec("C", "", [new PropSpec("p", shape, true)]).Signature;

    public static TheoryData<TypeShape, string> Shapes => new()
    {
        { TypeShape.String, "C(p: string)" },
        { TypeShape.Number, "C(p: number)" },
        { TypeShape.Boolean, "C(p: boolean)" },
        { TypeShape.Any, "C(p: any)" },
        { new EnumShape(["a", "b"]), "C(p: \"a\" | \"b\")" },
        { new ArrayShape(TypeShape.String), "C(p: string[])" },
        { new ArrayShape(new ArrayShape(TypeShape.Any)), "C(p: any[][])" },
        { new ArrayShape(new EnumShape(["a"])), "C(p: \"a\"[])" },
        { new ArrayShape(new EnumShape(["a", "b"])), "C(p: (\"a\" | \"b\")[])" },
        { new ObjectShape([]), "C(p: {})" },
        { new ObjectShape([new PropSpec("a", TypeShape.String, true), new PropSpec("b", TypeShape.Number, false)]), "C(p: {a: string, b?: number})" },
        { new UnionShape([TypeShape.String, TypeShape.Number]), "C(p: string | number)" },
        { new ArrayShape(new UnionShape([TypeShape.String, TypeShape.Number])), "C(p: (string | number)[])" },
        { new ActionSlotShape([]), "C(p: ActionExpression)" },
        { new ActionSlotShape(["CopyToClipboard", "OpenUrl"]), "C(p: @CopyToClipboard | @OpenUrl)" },
        { new ArrayShape(new ActionSlotShape(["CopyToClipboard", "OpenUrl"])), "C(p: (@CopyToClipboard | @OpenUrl)[])" },
        { new ComponentRefShape([]), "C(p: any)" },
        { new ComponentRefShape(["Button"]), "C(p: Button)" },
        { new ArrayShape(new ComponentRefShape(["Button"])), "C(p: Button[])" },
        { new ComponentRefShape(["Tile", "Map"]), "C(p: Tile | Map)" },
        { new ArrayShape(new ComponentRefShape(["Tile", "Map"])), "C(p: (Tile | Map)[])" },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void A_prop_prints_by_its_shape(TypeShape shape, string expected) => Assert.Equal(expected, Printed(shape));

    [Fact]
    public void A_declaration_prints_optional_and_bound_props_and_a_return_shape()
    {
        ImmutableArray<PropSpec> props =
        [
            new PropSpec("name", TypeShape.String, true),
            new PropSpec("value", TypeShape.String, false, Bindable: true),
            new PropSpec("size", TypeShape.Number, false, OuiValue.Number(1)),
        ];

        Assert.Equal("Input(name: string, value?: $binding<string>, size?: number)", new ComponentSpec("Input", "", props).Signature);
        Assert.Equal("Save(text: string)", new ActionSpec("Save", "", [props[0] with { Name = "text" }]).Signature);
        Assert.Equal("Upper(text: string) → string", new FunctionSpec("Upper", "", [props[0] with { Name = "text" }], TypeShape.String).Signature);
        Assert.Equal("Now()", new FunctionSpec("Now", "", []).Signature);
        Assert.Equal("C(a?: number, b?: {c?: number})", new ComponentSpec("C", "", [
            new PropSpec("a", TypeShape.Number, true, OuiValue.Number(1)),
            new PropSpec("b", new ObjectShape([new PropSpec("c", TypeShape.Number, true, OuiValue.Number(1))]), false),
        ]).Signature);
    }

    [Fact]
    public void The_fixture_librarys_signatures_are_what_its_schema_prints()
    {
        var library = OpenUILibrary.FromSpec(LibrarySpec.Parse(File.ReadAllText(GoldenFiles.FixtureLibraryPath)));
        var everyComponent = library.Components.Select(c => c.Name).ToImmutableHashSet();

        // library.json prints these two as any[], against the worked example of prompt.md section 2.3
        // and the rule of section 2.4 that the schema wins. Every other line must match it exactly.
        var writtenFromTheirSchema = new Dictionary<string, string>
        {
            ["Select"] = "Select(name: string, items: SelectItem[], value?: $binding<string>)",
            ["Table"] = "Table(columns: Col[])",
        };

        foreach (var component in library.Components)
        {
            ImmutableArray<PropSpec> props = [.. component.Props.Select(p => p with { Type = AnyWhenItListsEvery(p.Type, everyComponent) })];
            var expected = writtenFromTheirSchema.GetValueOrDefault(component.Name, component.Signature);
            Assert.Equal(expected, new ComponentSpec(component.Name, "", props).Signature);
        }

        foreach (var function in library.Functions)
        {
            Assert.Equal(function.Signature, new FunctionSpec(function.Name, "", function.Params, function.Returns).Signature);
        }

        foreach (var action in library.Actions)
        {
            Assert.Equal(action.Signature, new ActionSpec(action.Name, "", action.Params).Signature);
        }
    }

    [Fact]
    public void The_generated_react_ui_specs_signatures_are_what_its_schema_prints()
    {
        var path = Path.Combine(GoldenFiles.ReactChatGeneratedDirectory, "spec.json");
        var library = OpenUILibrary.FromSpec(LibrarySpec.Parse(File.ReadAllText(path)));

        Assert.NotEmpty(library.Components);
        foreach (var component in library.Components)
        {
            // TypeShape has no map of string to boolean, so a printed signature says {} for it.
            var written = component.Signature.Replace("Record<string, boolean>", "{}", StringComparison.Ordinal);
            Assert.Equal(written, new ComponentSpec(component.Name, "", component.Props).Signature);
        }
    }

    // The code-first spelling of "any component" is an empty list, which the schema writes as all of them.
    private static TypeShape AnyWhenItListsEvery(TypeShape shape, ImmutableHashSet<string> everyComponent) => shape switch
    {
        ArrayShape array => new ArrayShape(AnyWhenItListsEvery(array.Item, everyComponent)),
        ComponentRefShape refs when everyComponent.SetEquals(refs.Components) => new ComponentRefShape([]),
        _ => shape,
    };

    private static OpenUILibrary Dashboard()
    {
        var widgets = new ArrayShape(new ComponentRefShape(["Tile", "Map"]));
        return OpenUILibrary.CreateBuilder("dash")
            .Root("Page")
            .Group("Page", "- A page holds widgets.")
            .Add(new ComponentSpec(
                "Page",
                "A page",
                [
                    new PropSpec("title", TypeShape.String, true),
                    new PropSpec("widgets", widgets, true),
                    new PropSpec("columns", TypeShape.Number, false, OuiValue.Number(12)),
                ]),
                "Page")
            .Add(new ComponentSpec("Tile", "A number", [new PropSpec("location", TypeShape.String, true, Bindable: true), new PropSpec("act", new ActionSlotShape([]), false)]), "Widgets")
            .Add(new ComponentSpec("Map", "A map", [new PropSpec("kind", new EnumShape(["road", "sat"]), false)]), "Widgets")
            .Build();
    }

    [Fact]
    public void A_library_built_without_signatures_survives_its_spec_and_reaches_the_prompt()
    {
        var library = Dashboard();
        var spec = library.ToSpec();
        var read = OpenUILibrary.FromSpec(LibrarySpec.Parse(spec.ToJson()));

        Assert.True(read.TryGetComponent("Page", out var page));
        Assert.Equal("Page(title: string, widgets: (Tile | Map)[], columns?: number)", page.Signature);
        Assert.Equal(["Tile", "Map"], Assert.IsType<ComponentRefShape>(Assert.IsType<ArrayShape>(page.Props[1].Type).Item).Components.ToArray());
        Assert.True(read.TryGetComponent("Tile", out var tile));
        Assert.True(tile.Props[0].Bindable);
        Assert.Contains("Page(title: string, widgets: (Tile | Map)[], columns?: number) — A page", SystemPrompt.Generate(spec, new PromptOptions()), StringComparison.Ordinal);

        var program = new OpenUIParser(read).Parse("root = Page(\"t\", [Tile(\"a\"), Map(\"road\")])\n");
        Assert.Empty(OpenUIEvaluator.Evaluate(program).Errors);
    }

    [Fact]
    public void A_slot_that_gains_or_loses_a_component_prints_again()
    {
        var added = new ComponentSpec("Chart", "A chart", []);

        var extended = Dashboard().Extend(e => e.AddComponent(added, "Page.widgets"));
        Assert.True(extended.TryGetComponent("Page", out var page));
        Assert.Equal("Page(title: string, widgets: (Tile | Map | Chart)[], columns?: number)", page.Signature);

        var reduced = Dashboard().Extend(e => e.RemoveComponent("Map"));
        Assert.True(reduced.TryGetComponent("Page", out page));
        Assert.Equal("Page(title: string, widgets: Tile[], columns?: number)", page.Signature);
    }

    [Fact]
    public void A_signature_the_change_does_not_reach_stays_as_written()
    {
        var written = new ComponentSpec("Note", "", [new PropSpec("text", TypeShape.String, true)], default, "Note(text: string) with more to say");

        var library = OpenUILibrary.CreateBuilder().Root("Note").Add(written).Add(new ComponentSpec("Other", "", [])).Build();
        var reduced = library.Extend(e => e.RemoveComponent("Other"));

        Assert.True(reduced.TryGetComponent("Note", out var note));
        Assert.Equal(written.Signature, note.Signature);
    }
}
