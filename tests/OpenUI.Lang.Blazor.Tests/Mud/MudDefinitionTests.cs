using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Bunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenUI.Lang.AspNetCore;
using OpenUI.Lang.Blazor.Tests.TestComponents;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;
using OpenUI.Lang.Prompting;

using OpenUI.Lang.Blazor.Mud;

namespace OpenUI.Lang.Blazor.Tests.Mud;

/// <summary>What the package's definitions teach, with stub views, and what a definition with views missing does.</summary>
public sealed class MudDefinitionTests
{
    private const string Route = "/api/chat";

    private static readonly string SampleSpecPath = Path.Combine(AppContext.BaseDirectory, "generated", "spec.json");

    private static readonly LibraryDefinition Complete = TestLibrary.MudWith("openui-complete", views =>
    {
        foreach (var component in MudDefinitions.Spec.Components.Where(component => !MudDefinitions.Items.Contains(component.Name)))
        {
            views.Add<AnyView>(component.Name);
        }
    });

    public static TheoryData<string> PromptOptionCases() => ["none", "tools"];

    private static PromptOptions? Options(string name) => name == "tools"
        ? new PromptOptions { Tools = [new ToolDescriptor("getForecast", "Reads a forecast.")], AdditionalRules = ["Answer in French."] }
        : null;

    private static async Task<string> SystemMessageAsync(Action<IServiceCollection> addOpenUI)
    {
        var model = new RecordingModel();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IChatClient>(model);
        addOpenUI(builder.Services);
        await using var app = builder.Build();
        app.MapOpenUIChat(Route);
        await app.StartAsync();

        using var body = new StringContent("""{"messages":[{"role":"user","content":"hi"}]}""", Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        using var response = await app.GetTestClient().PostAsync(Route, body);
        await response.Content.ReadAsStringAsync();

        return model.SystemMessage ?? throw new InvalidOperationException($"The model was not called: {response.StatusCode}.");
    }


    private static ComponentSpec Component(LibraryDefinition definition, string name) =>
        Assert.Single(definition.Library.Components, component => component.Name == name);

    [Fact]
    public async Task The_embedded_spec_is_byte_identical_to_the_sample_s_generated_one()
    {
        using var stream = typeof(MudDefinitions).Assembly.GetManifestResourceStream("OpenUI.Lang.Blazor.Mud.spec.json");
        Assert.NotNull(stream);
        using var embedded = new MemoryStream();
        await stream.CopyToAsync(embedded);

        Assert.Equal(await File.ReadAllBytesAsync(SampleSpecPath), embedded.ToArray());
    }

    [Theory]
    [MemberData(nameof(PromptOptionCases))]
    public void A_complete_definition_teaches_what_the_spec_itself_teaches(string options) =>
        Assert.Equal(SystemPrompt.Generate(LibrarySpec.Parse(File.ReadAllText(SampleSpecPath)), Options(options)), Complete.SystemPrompt(Options(options)));

    [Theory]
    [MemberData(nameof(PromptOptionCases))]
    public async Task A_complete_definition_teaches_what_the_endpoint_teaches_from_the_same_file(string options)
    {
        var fromFile = await SystemMessageAsync(services => services.AddOpenUI(SampleSpecPath, Options(options)));

        Assert.Equal(fromFile, Complete.SystemPrompt(Options(options)));
    }

    [Fact]
    public void Every_item_is_a_component_that_a_parent_slot_reads_and_no_card_draws_directly()
    {
        var library = OpenUILibrary.FromSpec(MudDefinitions.Spec);
        var card = library.Components.Single(component => component.Name == "Card").Signature;

        foreach (var item in MudDefinitions.Items)
        {
            Assert.True(library.TryGetComponent(item, out _), item);
            Assert.DoesNotMatch($@"\b{item}\b", card);

            // Slice is upstream's older way to fill a PieChart: its renderer still reads one, but no schema names it.
            if (item != "Slice")
            {
                Assert.True(
                    library.Components.Any(parent => parent.Name != item && Regex.IsMatch(parent.Signature, $@":[^,]*\b{item}\b")),
                    $"{item} has no parent.");
            }
        }
    }

    [Theory]
    [InlineData("Input", "input", true)]
    [InlineData("TextArea", "textarea", true)]
    [InlineData("Select", "select", true)]
    [InlineData("DatePicker", "datepicker", true)]
    [InlineData("Slider", "slider", true)]
    [InlineData("CheckBoxGroup", "checkbox", true)]
    [InlineData("RadioGroup", "radio", true)]
    [InlineData("SwitchGroup", "switch", false)]
    [InlineData("Chips", "chips", true)]
    [InlineData("OptionCards", "optioncards", true)]
    [InlineData("EditableTable", "editabletable", false)]
    public void Each_input_takes_its_field_name_from_its_name_prop(string name, string componentType, bool hasRules)
    {
        var traits = Component(Complete, name).Traits;

        Assert.Equal("name", traits.FieldNameProp);
        Assert.Equal(componentType, traits.FieldComponentType);
        Assert.Equal(hasRules ? "rules" : null, traits.FieldRulesProp);
    }

    [Fact]
    public void A_form_takes_its_name_from_its_name_prop() =>
        Assert.Equal("name", Component(Complete, "Form").Traits.FormNameProp);

    [Fact]
    public void A_complete_definition_misses_nothing_and_one_with_no_views_misses_every_drawn_component()
    {
        Assert.Empty(Complete.MissingViews);

        var missing = TestLibrary.MudWith("openui-empty", _ => { }).MissingViews;

        Assert.Contains("Stack", missing);
        Assert.Contains("BarChart", missing);
        Assert.DoesNotContain("Series", missing);
        Assert.Equal(MudDefinitions.Spec.Components.Length - MudDefinitions.Items.Count, missing.Count);
    }

    [Fact]
    public void A_view_for_a_name_the_library_does_not_have_is_refused() =>
        Assert.Throws<ArgumentException>(() => TestLibrary.MudWith("openui-typo", views => views.Add<AnyView>("Stak")));

    [Fact]
    public async Task A_host_adds_a_component_of_its_own_on_top_of_a_package()
    {
        var mine = new LibraryDefinition("mine", null,
        [
            new("Maps", [new("FieldMap", "A map of a field.", typeof(AnyView), [new PropDefinition("field", TypeShape.String) { Required = true }]) { Slots = ["Card"] }], []),
        ]);
        var prompt = new OpenUIComponents([Complete, mine]).SystemPrompt();

        Assert.Contains("FieldMap(field: string)", prompt);
        Assert.Contains("| FieldMap)[]", prompt);
        Assert.DoesNotContain("FieldMap(", Complete.SystemPrompt());

        await using var context = TestLibrary.NewContext(services => services.AddOpenUIComponents(Complete).AddOpenUIComponents(mine));
        var cut = context.Render<OpenUIProgram>(parameters => parameters.Add(component => component.Program, "root = Stack([Card([FieldMap(\"North\")])])\n"));

        Assert.Empty(cut.Instance.Session.Current.Errors);
        Assert.Contains("North", cut.Find("[data-type=FieldMap]").TextContent);
    }

    [Fact]
    public async Task A_host_replaces_one_view_and_the_prompt_and_the_others_stay()
    {
        var mine = new LibraryDefinition("mine", null, [], new Dictionary<string, Type> { ["BarChart"] = typeof(OtherView) });
        var replaced = new OpenUIComponents([Complete, mine]);

        Assert.Equal(Complete.SystemPrompt(), replaced.SystemPrompt());
        Assert.Equal(typeof(OtherView), replaced.Components.Find("BarChart"));
        Assert.Equal(typeof(AnyView), replaced.Components.Find("Stack"));
        Assert.Equal(typeof(AnyView), Complete.Components.Find("BarChart"));

        await using var context = TestLibrary.NewContext(services => services.AddOpenUIComponents(Complete).AddOpenUIComponents(mine));
        var cut = context.Render<OpenUIProgram>(parameters => parameters.Add(component => component.Program, "root = Stack([BarChart([\"A\"], [Series(\"S\", [1])])])\n"));

        Assert.NotNull(cut.Find(".other[data-type=BarChart]"));
    }

    [Fact]
    public async Task A_library_defined_in_code_needs_no_spec_and_a_second_source_can_add_to_it()
    {
        Assert.Empty(TestLibrary.Definition.MissingViews);
        Assert.DoesNotContain("TextContent(", TestLibrary.Definition.SystemPrompt());

        var more = new LibraryDefinition("more", null,
        [
            new("More", [new("Extra", "One more.", typeof(AnyView), [new PropDefinition("text", TypeShape.String) { Required = true }])], []),
        ],
        new Dictionary<string, Type> { ["Box"] = typeof(OtherView) });
        await using var context = TestLibrary.NewContext(services => services.AddOpenUIComponents(TestLibrary.Definition).AddOpenUIComponents(more));
        var cut = context.Render<OpenUIProgram>(parameters => parameters.Add(component => component.Program, "root = Stack([Box(\"x\"), Extra(\"more\")])\n"));

        Assert.Empty(cut.Instance.Session.Current.Errors);
        Assert.NotNull(cut.Find(".other[data-type=Box]"));
        Assert.Contains("more", cut.Markup);
    }


    private sealed class RecordingModel : IChatClient
    {
        public string? SystemMessage { get; private set; }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            SystemMessage = messages.First().Text;
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The endpoint streams.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
