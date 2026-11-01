using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenUI.Lang.Core.Tests.Conformance;
using OpenUI.Lang.Library;
using OpenUI.Lang.Prompting;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Semantics;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Library;

/// <summary>Libraries defined in code and written back out (prompt.md, sections 2.7, 2.8 and 3.1).</summary>
public sealed class CodeFirstLibraryTests
{
    private static readonly ComponentSpec Text = new("Text", "Some text", [new PropSpec("text", TypeShape.String, true)], default, "Text(text: string)");
    private static readonly ComponentSpec Tag = new("Tag", "A tag", [new PropSpec("text", TypeShape.String, true)], default, "Tag(text: string)");
    private static readonly ComponentSpec Stack = Slotted("Stack", "children", "Text");

    private static ComponentSpec Slotted(string name, string prop, params string[] accepts) => new(
        name, "Holds things", [new PropSpec(prop, new ArrayShape(new ComponentRefShape([.. accepts])), true)], default, $"{name}({prop}: any[])");

    private static OpenUILibrary Base() => OpenUILibrary.CreateBuilder("base").Root("Stack").Add(Stack, "Layout").Add(Text, "Content").Build();

    private static OuiComponent? Render(OpenUILibrary library, string source) =>
        OpenUIEvaluator.Evaluate(new OpenUIParser(library).Parse(source)).Root;

    private static string[] Slot(OpenUILibrary library, string component, string prop)
    {
        Assert.True(library.TryGetComponent(component, out var spec));
        var slot = Assert.IsType<ArrayShape>(spec.Props.Single(p => p.Name == prop).Type);
        return [.. Assert.IsType<ComponentRefShape>(slot.Item).Components];
    }

    private static OpenUILibraryException ExtendFails(OpenUILibrary library, Action<LibraryExtension> configure) =>
        Assert.Throws<OpenUILibraryException>(() => library.Extend(configure));

    private sealed record TypedText(string Text) : IOpenUIComponent<TypedText>
    {
        public static ComponentSpec Spec => CodeFirstLibraryTests.Text;

        public static TypedText FromNode(OuiComponent node, IStateWriter writer) => new(node["text"].Value.ToString());
    }

    // The builder.

    [Fact]
    public void A_built_library_keeps_its_root_order_and_groups_and_renders()
    {
        var library = OpenUILibrary.CreateBuilder("demo").Root("Stack")
            .Group("Later")
            .Add(Stack)
            .Add(Text, "Content")
            .Add(Tag, "Later")
            .Group("Later", "use sparingly")
            .Build();

        Assert.Equal("demo", library.Id);
        Assert.Equal("Stack", library.RootComponent);
        Assert.Equal(["Stack", "Text", "Tag"], library.Components.Select(c => c.Name));
        Assert.Equal(["Later", "Content"], library.Groups.Select(g => g.Name));
        Assert.Equal(["Tag"], library.Groups[0].Components.ToArray());
        Assert.Equal(["use sparingly"], library.Groups[0].Notes!.Value.ToArray());
        Assert.Null(library.Groups[1].Notes);
        Assert.Equal("Stack", Render(library, "root = Stack([Text(\"a\")])\n")!.Type);
    }

    [Fact]
    public void A_component_type_supplies_its_own_declaration()
    {
        var library = OpenUILibrary.CreateBuilder().Root<TypedText>().Add<TypedText>("Content").Build();

        Assert.Equal("Text", library.RootComponent);
        Assert.Equal(["Text"], library.Groups.Single().Components.ToArray());
        Assert.Equal("a", TypedText.FromNode(Render(library, "root = Text(\"a\")\n")!, null!).Text);
    }

    [Fact]
    public void Build_refuses_a_required_prop_after_an_optional_one()
    {
        var outOfOrder = new ComponentSpec(
            "Note", "", [new PropSpec("tone", TypeShape.String, false), new PropSpec("text", TypeShape.String, true)], default, "Note(tone?: string, text: string)");

        var failure = Assert.Throws<OpenUILibraryException>(() => OpenUILibrary.CreateBuilder().Root("Note").Add(outOfOrder).Build());

        Assert.Contains(failure.Violations, v => v.Contains("Note", StringComparison.Ordinal) && v.Contains("text", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_refuses_a_duplicate_component_and_traits_for_a_missing_one()
    {
        var duplicate = Assert.Throws<OpenUILibraryException>(() => OpenUILibrary.CreateBuilder().Add(Text).Add(Text).Build());
        Assert.Contains(duplicate.Violations, v => v.Contains("defined twice", StringComparison.Ordinal));

        var traits = Assert.Throws<OpenUILibraryException>(() => OpenUILibrary.CreateBuilder().Add(Text).WithTraits("Nope", default).Build());
        Assert.Contains(traits.Violations, v => v.Contains("Nope", StringComparison.Ordinal));
    }

    // Extend, sentence by sentence of section 2.8.

    [Fact]
    public void Extending_returns_a_new_library_without_the_base_id_and_leaves_the_base_alone()
    {
        var library = Base();

        var extended = library.Extend(e => e.AddComponent(Tag));

        Assert.Null(extended.Id);
        Assert.Equal("base", library.Id);
        Assert.False(library.TryGetComponent("Tag", out _));
        Assert.Equal(["Stack", "Text", "Tag"], extended.Components.Select(c => c.Name));
        Assert.Equal("Stack", extended.RootComponent);
    }

    [Theory]
    [InlineData("Stack.children")]
    [InlineData("Stack")]
    public void An_added_component_joins_the_union_of_the_slots_it_names(string slot)
    {
        var library = Base();

        var extended = library.Extend(e => e.AddComponent(Tag, slot));

        Assert.Equal(["Text", "Tag"], Slot(extended, "Stack", "children"));
        Assert.Equal(["Text"], Slot(library, "Stack", "children"));
        var result = OpenUIEvaluator.Evaluate(new OpenUIParser(extended).Parse("root = Stack([Tag(\"a\")])\n"));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void A_parent_with_two_content_slots_must_name_the_prop()
    {
        var two = new ComponentSpec("Two", "", [
            new PropSpec("a", new ArrayShape(new ComponentRefShape(["Text"])), true),
            new PropSpec("b", new ArrayShape(new ComponentRefShape(["Text"])), true)], default, "Two(a: any[], b: any[])");
        var library = OpenUILibrary.CreateBuilder().Root("Two").Add(two).Add(Text).Build();

        var failure = ExtendFails(library, e => e.AddComponent(Tag, "Two"));

        Assert.Contains(failure.Violations, v => v.Contains("not one component slot", StringComparison.Ordinal));
        Assert.Equal(["Text", "Tag"], Slot(library.Extend(e => e.AddComponent(Tag, "Two.b")), "Two", "b"));
    }

    [Fact]
    public void A_slot_on_a_missing_component_is_an_error()
    {
        var failure = ExtendFails(Base(), e => e.AddComponent(Tag, "Nope.children"));

        Assert.Contains(failure.Violations, v => v.Contains("names no component", StringComparison.Ordinal));
    }

    [Fact]
    public void Overriding_replaces_a_component_by_name_and_slots_still_refer_to_it()
    {
        var replacement = Text with { Description = "Other text" };

        var extended = Base().Extend(e => e.OverrideComponent(replacement));

        Assert.True(extended.TryGetComponent("Text", out var text));
        Assert.Equal("Other text", text.Description);
        Assert.Equal(["Stack", "Text"], extended.Components.Select(c => c.Name));
        Assert.Equal(["Text"], Slot(extended, "Stack", "children"));
    }

    [Fact]
    public void Overriding_a_missing_component_is_an_error()
    {
        var failure = ExtendFails(Base(), e => e.OverrideComponent(Tag));

        Assert.Contains(failure.Violations, v => v.Contains("does not exist", StringComparison.Ordinal));
    }

    [Fact]
    public void Removing_drops_a_component_from_the_list_every_slot_union_and_the_groups()
    {
        var library = OpenUILibrary.CreateBuilder().Root("Stack").Add(Slotted("Stack", "children", "Text", "Tag")).Add(Text, "Content").Add(Tag, "Content").Build();

        var extended = library.Extend(e => e.RemoveComponent("Tag"));

        Assert.False(extended.TryGetComponent("Tag", out _));
        Assert.Equal(["Text"], Slot(extended, "Stack", "children"));
        Assert.Equal(["Text"], extended.Groups.Single().Components.ToArray());
        Assert.Equal(["Text", "Tag"], Slot(library, "Stack", "children"));
    }

    [Fact]
    public void Removing_the_root_the_only_type_of_a_prop_or_a_missing_component_is_an_error()
    {
        Assert.Contains(ExtendFails(Base(), e => e.RemoveComponent("Stack")).Violations, v => v.Contains("is the root", StringComparison.Ordinal));
        Assert.Contains(ExtendFails(Base(), e => e.RemoveComponent("Text")).Violations, v => v.Contains("no type it accepts", StringComparison.Ordinal));
        Assert.Contains(ExtendFails(Base(), e => e.RemoveComponent("Tag")).Violations, v => v.Contains("does not exist", StringComparison.Ordinal));
    }

    [Fact]
    public void Functions_and_actions_can_be_added()
    {
        var upper = new FunctionDefinition(new FunctionSpec("Upper", "", [], null, "Upper()"), (in FunctionArguments _) => "A");
        var save = new ActionSpec("Save", "", [], "Save()");

        var extended = Base().Extend(e => e.AddFunction(upper).AddAction(save));

        Assert.True(extended.TryGetFunction("Upper", out _));
        Assert.True(extended.TryGetAction("Save", out _));
        Assert.Equal((OuiValue)"A", Render(extended, "root = Stack([Text(@Upper())])\n")!["children"].Value.TryGetArray(out var children) && children[0].TryGetComponent(out var text) ? text["text"].Value : OuiValue.Null);
    }

    [Fact]
    public void Adding_a_name_that_already_exists_is_an_error()
    {
        var upper = new FunctionDefinition(new FunctionSpec("Upper", "", [], null, "Upper()"), (in FunctionArguments _) => "A");
        var save = new ActionSpec("Save", "", [], "Save()");
        var library = Base().Extend(e => e.AddFunction(upper).AddAction(save));

        Assert.Contains(ExtendFails(library, e => e.AddComponent(Text)).Violations, v => v.Contains("already exists", StringComparison.Ordinal));
        Assert.Contains(ExtendFails(library, e => e.AddFunction(upper)).Violations, v => v.Contains("already exists", StringComparison.Ordinal));
        Assert.Contains(ExtendFails(library, e => e.AddAction(save)).Violations, v => v.Contains("already exists", StringComparison.Ordinal));
        Assert.Contains(ExtendFails(library, e => e.AddAction(new ActionSpec("Upper", "", [], "Upper()"))).Violations, v => v.Contains("already exists", StringComparison.Ordinal));
        Assert.Contains(
            ExtendFails(library, e => e.AddFunction(new FunctionDefinition(new FunctionSpec("Save", "", [], null, "Save()"), (in FunctionArguments _) => 1))).Violations,
            v => v.Contains("already exists", StringComparison.Ordinal));
    }

    // Writing the spec.

    [Fact]
    public void The_fixture_library_is_written_back_key_for_key_in_order()
    {
        var read = FixtureCatalog.LoadLibrary();
        var spec = LibrarySpec.Parse(read.ToJsonString());

        // Same text once both sides are compact, so a reordered key fails as well as a lost one.
        Assert.Equal(read.ToJsonString(), JsonNode.Parse(spec.ToJson())!.ToJsonString());
    }

    [Fact]
    public void Writing_a_spec_keeps_keys_this_version_does_not_define()
    {
        const string Json = """{"future":{"a":1},"root":"Text","components":{"Text":{"signature":"Text()"}},"later":[2]}""";

        var written = JsonNode.Parse(LibrarySpec.Parse(Json).ToJson())!.AsObject();

        Assert.Equal("""{"a":1}""", written["future"]!.ToJsonString());
        Assert.Equal("[2]", written["later"]!.ToJsonString());
    }

    [Fact]
    public void WriteTo_writes_what_ToJson_shows()
    {
        var spec = Base().ToSpec();
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            spec.WriteTo(writer);
        }

        Assert.Equal(JsonNode.Parse(spec.ToJson())!.ToJsonString(), JsonNode.Parse(buffer.ToArray())!.ToJsonString());
    }

    [Fact]
    public void A_library_written_as_a_spec_and_read_back_gives_the_same_prompt()
    {
        var library = Base();

        var again = OpenUILibrary.FromSpec(LibrarySpec.Parse(library.ToSpec().ToJson()));

        Assert.Equal(SystemPrompt.Generate(library.ToSpec()), SystemPrompt.Generate(again.ToSpec()));
        Assert.Equal("base", again.Id);
        Assert.Equal("Stack", again.RootComponent);
    }

    [Fact]
    public void The_spec_of_a_library_has_no_version()
    {
        var library = OpenUILibrary.CreateBuilder("demo").Version("1.2.0").Root("Text").Add(Text).Build();

        Assert.Equal("1.2.0", library.Version);
        Assert.Null(library.ToSpec().Version);
    }

    [Fact]
    public void WriteJsonSchema_writes_the_validation_schema_alone()
    {
        var library = Base();
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            library.WriteJsonSchema(writer);
        }

        var schema = JsonNode.Parse(buffer.ToArray())!.AsObject();

        Assert.Equal(library.ToSpec().Schema!.Value.GetRawText(), schema.ToJsonString());
        Assert.False(schema.ContainsKey("components"));
        Assert.Equal("""["text"]""", schema["$defs"]!["Text"]!["required"]!.ToJsonString());
    }
}
