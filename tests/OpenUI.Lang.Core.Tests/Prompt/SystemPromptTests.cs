using System.Text.Json;
using OpenUI.Lang.Library;
using OpenUI.Lang.Prompting;

namespace OpenUI.Lang.Core.Tests.Prompt;

/// <summary>Option defaults and input validation (prompt.md, section 4.2). Exact text is covered by the goldens.</summary>
public sealed class SystemPromptTests
{
    private static LibrarySpec Library() => GoldenFiles.Spec(GoldenFiles.ReadJsonObject(GoldenFiles.FixtureLibraryPath));

    [Fact]
    public void Null_options_mean_all_defaults() =>
        Assert.Equal(SystemPrompt.Generate(Library(), new PromptOptions()), SystemPrompt.Generate(Library()));

    [Fact]
    public void Tools_switch_on_tool_calls_and_bindings()
    {
        var implied = SystemPrompt.Generate(Library(), new PromptOptions { Tools = ["list_orders"] });
        var explicitly = SystemPrompt.Generate(
            Library(),
            new PromptOptions { Tools = ["list_orders"], ToolCalls = true, Bindings = true, BuiltinFunctions = true });

        Assert.Equal(explicitly, implied);
        Assert.Contains("## Query — Live Data Fetching", implied);
    }

    [Fact]
    public void Resolve_applies_the_defaults_in_one_place()
    {
        var none = SystemPrompt.Resolve(new PromptOptions());
        var tools = SystemPrompt.Resolve(new PromptOptions { Tools = ["list_orders"] });
        var emptyTools = SystemPrompt.Resolve(new PromptOptions { Tools = [] });
        var bindings = SystemPrompt.Resolve(new PromptOptions { Bindings = true });
        var builtinsOff = SystemPrompt.Resolve(new PromptOptions { ToolCalls = true, BuiltinFunctions = false });

        Assert.Equal((false, false, false), (none.ToolCalls, none.Bindings, none.BuiltinFunctions));
        Assert.Equal((true, true, true), (tools.ToolCalls, tools.Bindings, tools.BuiltinFunctions));
        Assert.False(emptyTools.ToolCalls);
        Assert.Equal((false, true, true), (bindings.ToolCalls, bindings.Bindings, bindings.BuiltinFunctions));
        Assert.Equal((true, true, false), (builtinsOff.ToolCalls, builtinsOff.Bindings, builtinsOff.BuiltinFunctions));
        Assert.StartsWith("You are an AI assistant", none.Preamble);
    }

    [Fact]
    public void Resolve_treats_default_arrays_as_empty()
    {
        var resolved = SystemPrompt.Resolve(new PromptOptions { Tools = default, Examples = default, ToolExamples = default, AdditionalRules = default });

        Assert.Empty(resolved.Tools);
        Assert.Empty(resolved.Examples);
        Assert.Empty(resolved.ToolExamples);
        Assert.Empty(resolved.AdditionalRules);
    }

    [Fact]
    public void Defaults_leave_out_features_the_client_did_not_ask_for()
    {
        var prompt = SystemPrompt.Generate(Library());

        Assert.DoesNotContain("## Query", prompt);
        Assert.DoesNotContain("@Run", prompt);
        Assert.DoesNotContain("@Set", prompt);
        Assert.DoesNotContain("## Edit Mode", prompt);
        Assert.DoesNotContain("## Inline Mode", prompt);
        Assert.DoesNotContain("@Count", prompt);
        // Custom functions still list, alone.
        Assert.Contains("@Upper(text: string) → string — The text in upper case", prompt);
    }

    [Fact]
    public void An_empty_preamble_replaces_the_default() =>
        Assert.StartsWith("\n\n## Syntax Rules", SystemPrompt.Generate(Library(), new PromptOptions { Preamble = "" }));

    [Fact]
    public void Output_uses_unix_line_breaks_and_no_trailing_newline()
    {
        var prompt = SystemPrompt.Generate(Library(), new PromptOptions { EditMode = true, InlineMode = true });

        Assert.DoesNotContain('\r', prompt);
        Assert.False(prompt.EndsWith('\n'));
    }

    [Fact]
    public void A_library_without_components_is_rejected() =>
        Assert.Throws<ArgumentException>(() => SystemPrompt.Generate(LibrarySpec.Parse("""{"root":"Card"}""")));

    [Fact]
    public void A_component_without_a_signature_is_rejected()
    {
        var error = Assert.Throws<ArgumentException>(
            () => SystemPrompt.Generate(LibrarySpec.Parse("""{"components":{"Card":{"description":"A card"}}}""")));

        Assert.Contains("components.Card", error.Message);
    }

    [Fact]
    public void A_null_library_is_rejected() =>
        Assert.Throws<ArgumentNullException>(() => SystemPrompt.Generate(null!));

    [Fact]
    public void Options_read_from_json_use_the_reference_names()
    {
        var options = PromptOptions.FromJson(Json("""
            {"toolCalls":true,"editMode":true,"preamble":"Hi","additionalRules":["a"],
             "tools":["x",{"name":"y","description":"d","inputSchema":{"type":"object"},"outputSchema":{"type":"string"},"annotations":{"readOnlyHint":true}}]}
            """));

        Assert.True(options.ToolCalls);
        Assert.Null(options.Bindings);
        Assert.True(options.EditMode);
        Assert.False(options.InlineMode);
        Assert.Equal("Hi", options.Preamble);
        Assert.Equal(["a"], options.AdditionalRules.ToArray());
        Assert.Collection(
            options.Tools,
            tool => Assert.Equal(new ToolDescriptor("x"), tool),
            tool => Assert.True(tool.Annotations!.Value.GetProperty("readOnlyHint").GetBoolean()));
    }

    [Theory]
    [InlineData("""{"toolCalls":"yes"}""")]
    [InlineData("""{"examples":"one"}""")]
    [InlineData("""{"tools":[1]}""")]
    [InlineData("""{"tools":[{"description":"no name"}]}""")]
    public void Options_with_the_wrong_json_type_are_rejected(string json) =>
        Assert.Throws<ArgumentException>(() => PromptOptions.FromJson(Json(json)));

    [Fact]
    public void A_spec_file_with_prompt_options_gives_the_prompt_those_options_give()
    {
        var plain = Library();
        var carrying = plain with
        {
            AdditionalProperties = [KeyValuePair.Create("promptOptions", Json("""{"preamble":"Carried.","additionalRules":["r"]}"""))],
        };

        var options = carrying.ReadPromptOptions();

        Assert.Equal("Carried.", options!.Preamble);
        Assert.Null(plain.ReadPromptOptions());
        Assert.StartsWith("Carried.", SystemPrompt.Generate(carrying), StringComparison.Ordinal);
        Assert.Equal(SystemPrompt.Generate(carrying, options), SystemPrompt.Generate(carrying));
        Assert.DoesNotContain("Carried.", SystemPrompt.Generate(carrying, new PromptOptions()), StringComparison.Ordinal);
    }

    [Fact]
    public void A_spec_file_with_prompt_options_of_the_wrong_shape_is_rejected()
    {
        var carrying = Library() with { AdditionalProperties = [KeyValuePair.Create("promptOptions", Json("[]"))] };

        Assert.Throws<ArgumentException>(() => carrying.ReadPromptOptions());
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
