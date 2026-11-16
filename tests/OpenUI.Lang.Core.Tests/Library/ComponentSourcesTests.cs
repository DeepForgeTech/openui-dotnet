using System.Collections.Immutable;
using OpenUI.Lang.Library;
using OpenUI.Lang.Prompting;

namespace OpenUI.Lang.Core.Tests.Library;

public sealed class ComponentSourcesTests
{
    private static readonly PropSpec Text = new("text", TypeShape.String, Required: true);

    private static ComponentSpec Component(string name, string description = "A component.", params PropSpec[] props) =>
        new(name, description, props.Length == 0 ? [Text] : [.. props]);

    private static OpenUILibrary Build(string? root, string group, params ComponentSpec[] components)
    {
        var builder = OpenUILibrary.CreateBuilder("lib");
        if (root is not null)
        {
            builder.Root(root);
        }

        foreach (var component in components)
        {
            builder.Add(component, group);
        }

        return builder.Build();
    }

    private sealed record Source(OpenUILibrary Library) : IComponentSource
    {
        public LibrarySpec Spec { get; init; } = Library.ToSpec();

        public IReadOnlyDictionary<string, IReadOnlyList<string>> Slots { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
    }

    private static readonly Source First = new(Build("Stack", "Layout",
        Component("Stack", "A column.", new PropSpec("children", new ArrayShape(new ComponentRefShape(["Line"])), Required: true)),
        Component("Line", "A line.")));

    [Fact]
    public void No_source_fails_saying_what_to_register()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ComponentSources.Compose([]));

        Assert.Contains("IComponentSource", error.Message);
    }

    [Fact]
    public void One_source_comes_back_as_it_is() =>
        Assert.Same(First, ComponentSources.Compose([First]));

    [Fact]
    public void A_later_source_adds_its_components_with_its_group()
    {
        var composed = ComponentSources.Compose([First, new Source(Build(null, "Maps", Component("FieldMap", "A map.")))]);

        Assert.Equal(["Stack", "Line", "FieldMap"], composed.Library.Components.Select(component => component.Name));
        Assert.Equal("Stack", composed.Library.RootComponent);
        Assert.Equal(["Layout", "Maps"], composed.Spec.ComponentGroups!.Value.Select(group => group.Name));
        Assert.Contains("FieldMap(text: string)", SystemPrompt.Generate(composed.Spec));
        Assert.Contains("Line(text: string)", SystemPrompt.Generate(composed.Spec));
    }

    [Fact]
    public void A_later_source_wins_a_clash_and_the_component_keeps_its_place()
    {
        var changed = Component("Line", "A longer line.", Text, new PropSpec("size", TypeShape.Number, Required: false));

        var composed = ComponentSources.Compose([First, new Source(Build(null, "Mine", changed))]);

        Assert.Equal(["Stack", "Line"], composed.Library.Components.Select(component => component.Name));
        var prompt = SystemPrompt.Generate(composed.Spec);
        Assert.Contains("Line(text: string, size?: number)", prompt);
        Assert.DoesNotContain("Mine", prompt);
    }

    [Fact]
    public void The_last_source_wins_of_three()
    {
        var second = new Source(Build(null, "A", Component("Line", "Second.", Text, new PropSpec("a", TypeShape.Number, Required: false))));
        var third = new Source(Build(null, "B", Component("Line", "Third.", Text, new PropSpec("b", TypeShape.Number, Required: false))));

        var composed = ComponentSources.Compose([First, second, third]);

        Assert.True(composed.Library.TryGetComponent("Line", out var line));
        Assert.Equal("Line(text: string, b?: number)", line.Signature);
    }

    [Fact]
    public void The_first_source_to_declare_a_root_keeps_it()
    {
        var composed = ComponentSources.Compose([First, new Source(Build("Page", "Mine", Component("Page", "A page.")))]);

        Assert.Equal("Stack", composed.Library.RootComponent);
        Assert.Equal("Stack", composed.Spec.Root);
    }

    [Fact]
    public void A_source_with_no_root_takes_the_next_one_declared()
    {
        var composed = ComponentSources.Compose([new Source(Build(null, "Mine", Component("Extra", "One more."))), new Source(Build("Page", "Pages", Component("Page", "A page.")))]);

        Assert.Equal("Page", composed.Library.RootComponent);
        Assert.Equal("Page", composed.Spec.Root);
    }

    [Fact]
    public void A_later_source_can_replace_the_root_component_and_it_stays_the_root()
    {
        var composed = ComponentSources.Compose([First, new Source(Build(null, "Mine", Component("Stack", "My own column.")))]);

        Assert.Equal("Stack", composed.Library.RootComponent);
        Assert.Equal("My own column.", composed.Library.Components.Single(component => component.Name == "Stack").Description);
    }

    [Fact]
    public void A_slot_puts_a_later_component_into_an_earlier_signature()
    {
        var map = new Source(Build(null, "Maps", Component("FieldMap", "A map.")))
        {
            Slots = new Dictionary<string, IReadOnlyList<string>> { ["FieldMap"] = ["Stack"] },
        };

        var composed = ComponentSources.Compose([First, map]);

        Assert.True(composed.Library.TryGetComponent("Stack", out var stack));
        Assert.Contains("(Line | FieldMap)[]", stack.Signature);
    }

    [Fact]
    public void A_slot_that_names_no_component_fails_at_composition()
    {
        var map = new Source(Build(null, "Maps", Component("FieldMap", "A map.")))
        {
            Slots = new Dictionary<string, IReadOnlyList<string>> { ["FieldMap"] = ["Nowhere"] },
        };

        var error = Assert.Throws<OpenUILibraryException>(() => ComponentSources.Compose([First, map]));

        Assert.Contains("Nowhere", error.Message);
    }

    [Fact]
    public void The_earlier_source_is_unchanged_and_keeps_its_prompt_options()
    {
        var withOptions = ComponentSources.FromSpec(LibrarySpec.Parse(
            """
            {"root":"Line","components":{"Line":{"signature":"Line(text: string)","description":"A line."}},
             "schema":{"$defs":{"Line":{"type":"object","properties":{"text":{"type":"string"}},"required":["text"]}}},
             "promptOptions":{"additionalRules":["Keep it short."]}}
            """));
        var before = SystemPrompt.Generate(withOptions.Spec);

        var composed = ComponentSources.Compose([withOptions, new Source(Build(null, "Maps", Component("FieldMap", "A map.")))]);

        Assert.Equal(before, SystemPrompt.Generate(withOptions.Spec));
        Assert.Contains("Keep it short.", SystemPrompt.Generate(composed.Spec));
    }

    private static IComponentSource WithOptions(string component, string options) =>
        ComponentSources.FromSpec(LibrarySpec.Parse(
            "{\"components\":{\"" + component + "\":{\"signature\":\"" + component + "(text: string)\",\"description\":\"A component.\"}}," +
            "\"schema\":{\"$defs\":{\"" + component + "\":{\"type\":\"object\",\"properties\":{\"text\":{\"type\":\"string\"}},\"required\":[\"text\"]}}}," +
            "\"promptOptions\":" + options + "}"));

    [Fact]
    public void Rules_and_examples_of_every_source_are_kept_in_order_and_other_options_go_to_the_later_one()
    {
        var earlier = WithOptions("Line", """{"preamble":"Early.","additionalRules":["Keep lines short."],"examples":["root = Line(\"a\")"],"toolExamples":["early tool"]}""");
        var later = WithOptions("FieldMap", """{"preamble":"Late.","additionalRules":["Only maps."],"examples":["root = FieldMap(\"b\")"]}""");

        var composed = ComponentSources.Compose([earlier, later]);

        var options = composed.Spec.ReadPromptOptions()!;
        Assert.Equal<IEnumerable<string>>(["Keep lines short.", "Only maps."], options.AdditionalRules);
        Assert.Equal<IEnumerable<string>>(["root = Line(\"a\")", "root = FieldMap(\"b\")"], options.Examples);
        Assert.Equal<IEnumerable<string>>(["early tool"], options.ToolExamples);
        Assert.Equal("Late.", options.Preamble);
        var prompt = SystemPrompt.Generate(composed.Spec);
        Assert.Contains("Keep lines short.", prompt);
        Assert.Contains("Only maps.", prompt);
    }

    [Fact]
    public void A_later_source_with_only_a_rule_keeps_the_earlier_examples()
    {
        var earlier = WithOptions("Line", """{"additionalRules":["Keep lines short."],"examples":["root = Line(\"a\")"]}""");
        var composed = ComponentSources.Compose([earlier, WithOptions("FieldMap", """{"additionalRules":["Only maps."]}""")]);

        var options = composed.Spec.ReadPromptOptions()!;

        Assert.Equal<IEnumerable<string>>(["root = Line(\"a\")"], options.Examples);
        Assert.Equal<IEnumerable<string>>(["Keep lines short.", "Only maps."], options.AdditionalRules);
    }

    [Fact]
    public void A_clash_between_a_component_and_a_later_action_is_refused()
    {
        var builder = OpenUILibrary.CreateBuilder("lib").Add(Component("Other"));
        builder.AddActions(new ActionSpec("Line", "An action.", ImmutableArray<PropSpec>.Empty));

        Assert.Throws<OpenUILibraryException>(() => ComponentSources.Compose([First, new Source(builder.Build())]));
    }
}
