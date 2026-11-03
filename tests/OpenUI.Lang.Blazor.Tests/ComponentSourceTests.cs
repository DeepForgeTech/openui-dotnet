using Bunit;
using Microsoft.Extensions.DependencyInjection;
using OpenUI.Lang.Blazor.Tests.TestComponents.PlainKit;
using OpenUI.Lang.Library;
using OpenUI.Lang.Diagnostics;
using MudBlazor.Services;
using OpenUI.Lang.Blazor.Mud;

namespace OpenUI.Lang.Blazor.Tests;

/// <summary>The seam a component package builds on, proved with a package that has nothing to do with MudBlazor.</summary>
public sealed class ComponentSourceTests
{
    private const string Plain = """
        $name = ""
        root = Column([Heading("Hello", "one"), Field("who", $name), Press("Go", @Set($name, "pressed")), Notice("Name: " + $name)])

        """;

    private static IRenderedComponent<OpenUIProgram> Render(BunitContext context, string program) =>
        context.Render<OpenUIProgram>(parameters => parameters.Add(component => component.Program, program));

    [Fact]
    public async Task A_package_with_no_MudBlazor_draws_and_runs_with_only_its_source_registered()
    {
        await using var context = TestLibrary.NewContext(services => services.AddOpenUIComponents<PlainComponents>());

        var cut = Render(context, Plain);

        Assert.Empty(cut.Instance.Session.Current.Errors);
        Assert.Equal("Hello", cut.Find("h2.plain-heading").TextContent);
        Assert.DoesNotContain("mud-", cut.Markup);
        cut.Find("button.plain-press").Click();
        cut.WaitForAssertion(() => Assert.Equal("Name: pressed", cut.Find("p.plain-notice").TextContent));
        cut.Find("input.plain-field").Input("typed");
        cut.WaitForAssertion(() => Assert.Equal("Name: typed", cut.Find("p.plain-notice").TextContent));
    }

    [Fact]
    public void The_renderer_does_not_depend_on_any_ui_library()
    {
        var names = typeof(OpenUIComponent).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name);

        Assert.DoesNotContain(names, name => name!.Contains("Mud", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Swapping_the_source_for_another_changes_the_prompt_to_that_one_s_signatures()
    {
        var plain = new OpenUIComponents([new PlainComponents()]).SystemPrompt();
        var mud = new OpenUIComponents([new MudComponents()]).SystemPrompt();

        Assert.NotEqual(mud, plain);
        Assert.Contains("Heading(text: string, level?: \"one\" | \"two\")", plain);
        Assert.Contains("Press(text: string, onPress?: ActionExpression)", plain);
        Assert.Contains("root = Column(", plain);
        Assert.DoesNotContain("Heading(", mud);
        Assert.Contains("TextContent(", mud);
        foreach (var taughtByMud in new[] { "TextContent(", "Tabs(", "Accordion(", "EditableTable(", "Modal(" })
        {
            Assert.DoesNotContain(taughtByMud, plain);
        }
    }

    [Fact]
    public async Task A_program_naming_another_package_s_component_is_unknown()
    {
        await using var context = TestLibrary.NewContext(services => services.AddOpenUIComponents<PlainComponents>());

        var cut = Render(context, "root = Column([Tabs([TabItem(\"a\", \"A\", [])])])\n");

        Assert.Contains(cut.Instance.Session.Current.Errors, error => error.Code == OpenUIErrorCode.UnknownComponent && error.Message.Contains("Tabs"));
        Assert.DoesNotContain("mud-", cut.Markup);
    }

    [Fact]
    public async Task One_program_uses_the_components_of_two_sources_and_the_prompt_teaches_them_all()
    {
        await using var context = TestLibrary.NewContext(services => services.AddOpenUIComponents<PlainComponents>().AddOpenUIComponents<PlainExtras>());
        var components = context.Services.GetRequiredService<OpenUIComponents>();

        var cut = Render(context, "root = Column([Heading(\"Hello\"), Badge(\"new\")])\n");

        Assert.Empty(cut.Instance.Session.Current.Errors);
        Assert.Equal("Hello", cut.Find("h2.plain-heading").TextContent);
        Assert.Equal("new", cut.Find("span.plain-badge").TextContent);
        var prompt = components.SystemPrompt();
        Assert.Contains("Heading(text: string", prompt);
        Assert.Contains("Badge(text: string)", prompt);
        Assert.Contains("| Badge)[]", prompt);
    }

    [Fact]
    public async Task A_source_can_sit_on_top_of_a_real_package_and_one_program_mixes_them()
    {
        await using var context = TestLibrary.NewContext(services => services
            .AddMudServices()
            .AddOpenUIComponents(TestLibrary.Mud)
            .AddOpenUIComponents(new LibraryDefinition("mine", null,
            [
                new("Mine", [new("Badge", "A small label.", typeof(PlainBadgeView), [new("text", TypeShape.String) { Required = true }]) { Slots = ["Card"] }], []),
            ])));
        var components = context.Services.GetRequiredService<OpenUIComponents>();

        var cut = Render(context, "root = Stack([Card([TextContent(\"Text\"), Badge(\"new\")])])\n");

        Assert.Empty(cut.Instance.Session.Current.Errors);
        Assert.Equal("new", cut.Find("span.plain-badge").TextContent);
        Assert.Contains("Text", cut.Markup);
        var prompt = components.SystemPrompt();
        Assert.Contains("Badge(text: string)", prompt);
        Assert.Contains("TextContent(", prompt);
    }

    [Fact]
    public async Task A_later_source_wins_a_name_in_what_is_drawn_and_what_is_taught()
    {
        var replacement = new LibraryDefinition("mine", null,
        [
            new("Mine", [new("Notice", "A louder message.", typeof(PlainLoudNoticeView), [new("words", TypeShape.String) { Required = true }])], []),
        ]);
        await using var context = TestLibrary.NewContext(services => services.AddOpenUIComponents<PlainComponents>().AddOpenUIComponents(replacement));
        var prompt = context.Services.GetRequiredService<OpenUIComponents>().SystemPrompt();

        var cut = Render(context, "root = Column([Notice(\"hi\")])\n");

        Assert.Contains("Notice(words: string)", prompt);
        Assert.DoesNotContain("Notice(message: string)", prompt);
        Assert.NotNull(cut.Find("p.plain-loud-notice"));
        Assert.Empty(cut.FindAll("p.plain-notice"));
    }

    [Fact]
    public async Task A_later_source_redraws_a_name_and_keeps_what_is_taught()
    {
        await using var context = TestLibrary.NewContext(services => services.AddOpenUIComponents<PlainComponents>().AddOpenUIComponents<PlainExtras>());

        var cut = Render(context, "root = Column([Notice(\"hi\")])\n");

        Assert.Equal("hi", cut.Find("p.plain-loud-notice").TextContent);
        Assert.Contains("Notice(message: string)", context.Services.GetRequiredService<OpenUIComponents>().SystemPrompt());
    }

    [Fact]
    public void The_first_root_a_source_declares_is_the_root_whatever_comes_after_it()
    {
        var withRootOfItsOwn = new LibraryDefinition("second", "Wrapper",
        [
            new("Second", [new("Wrapper", "Wraps.", typeof(PlainColumnView), [new("children", TypeShape.Any)])], []),
        ]);

        Assert.Equal("Column", new OpenUIComponents([new PlainComponents(), withRootOfItsOwn]).Library.RootComponent);
        Assert.Equal("Wrapper", new OpenUIComponents([withRootOfItsOwn, new PlainComponents()]).Library.RootComponent);
        Assert.Equal("Column", new OpenUIComponents([new PlainExtras(), new PlainComponents()]).Library.RootComponent);
    }

    [Fact]
    public async Task With_no_source_the_page_says_what_to_register_and_goes_away_quietly()
    {
        await using var context = new BunitContext();

        var error = Assert.Throws<InvalidOperationException>(() => context.Render<OpenUIProgram>(parameters => parameters.Add(component => component.Program, "root = Column([])\n")));

        Assert.Contains("AddOpenUIComponents", error.Message);
    }

    [Fact]
    public void OpenUIProgram_takes_no_library_from_the_page() =>
        Assert.DoesNotContain(typeof(OpenUIProgram).GetProperties(), property => property.PropertyType == typeof(LibraryDefinition) || property.Name == "Library");

    [Fact]
    public void One_type_registered_twice_counts_once()
    {
        var services = new ServiceCollection().AddOpenUIComponents<PlainComponents>().AddOpenUIComponents<PlainComponents>();

        Assert.Single(services, service => service.ServiceType == typeof(IComponentSource));
    }
}
