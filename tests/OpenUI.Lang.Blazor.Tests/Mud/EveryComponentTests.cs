using System.Text.RegularExpressions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using OpenUI.Lang.Blazor.Mud;
using OpenUI.Lang.Library;
using OpenUI.Lang.Prompting;

namespace OpenUI.Lang.Blazor.Tests.Mud;

/// <summary>Each standard component drawn through the package's own registration, from a program that uses it.</summary>
public sealed partial class EveryComponentTests
{
    private static readonly string SpecPath = Path.Combine(AppContext.BaseDirectory, "generated", "spec.json");

    // A program for each component that gets drawn, and text only that component's view puts on the page.
    private static readonly Dictionary<string, (string Program, string Drawn)> Programs = new()
    {
        ["Card"] = ("""Card([TextContent("card body")])""", "card body"),
        ["CardHeader"] = ("""CardHeader("heading", "under it")""", "under it"),
        ["TextContent"] = ("""TextContent("plain words")""", "plain words"),
        ["MarkDownRenderer"] = ("""MarkDownRenderer("**strong words**")""", "<strong>strong words</strong>"),
        ["Callout"] = ("""Callout("info", "note", "the details")""", "the details"),
        ["TextCallout"] = ("""TextCallout("info", "note", "the details")""", "the details"),
        ["Image"] = ("""Image("the alt", "a.png")""", "the alt"),
        ["ImageBlock"] = ("""ImageBlock("a.png", "the alt")""", "the alt"),
        ["ImageGallery"] = ("""ImageGallery([{src: "a.png", alt: "the alt"}])""", "the alt"),
        ["CodeBlock"] = ("""CodeBlock("text", "the code")""", "the code"),
        ["InlineHeader"] = ("""InlineHeader("heading", "under it")""", "under it"),
        ["Table"] = ("""Table([Col("Day", ["Mon"], "string")])""", "Mon"),
        ["EditableTable"] = ("""EditableTable("t", [{type: "text", key: "a", header: "Crop"}], [{id: "1", values: ["Corn"]}])""", "Corn"),
        ["BarChart"] = ("""BarChart(["Mon"], [Series("Rain", [1])])""", "Rain"),
        ["LineChart"] = ("""LineChart(["Mon"], [Series("Rain", [1])])""", "Rain"),
        ["AreaChart"] = ("""AreaChart(["Mon"], [Series("Rain", [1])])""", "Rain"),
        ["RadarChart"] = ("""RadarChart(["Mon", "Tue", "Wed"], [Series("Rain", [1, 2, 3])])""", "Rain"),
        ["HorizontalBarChart"] = ("""HorizontalBarChart(["Mon"], [Series("Rain", [1])])""", "Rain"),
        ["PieChart"] = ("""PieChart(["Mon"], [1])""", "Mon"),
        ["RadialChart"] = ("""RadialChart(["Mon"], [1])""", "Mon"),
        ["SingleStackedBarChart"] = ("""SingleStackedBarChart(["Mon"], [1])""", "Mon"),
        ["ScatterChart"] = ("""ScatterChart([ScatterSeries("Probes", [Point(1, 2, 3)])])""", "Probes"),
        ["Form"] = ("""Form("f", Buttons([Button("Send")]), [FormControl("Field", Input("a"))])""", "Send"),
        ["FormControl"] = ("""FormControl("Field", Input("a"), "the hint")""", "the hint"),
        ["Label"] = ("""Label("the label")""", "the label"),
        ["Input"] = ("""Input("a", "the hint")""", "the hint"),
        ["TextArea"] = ("""TextArea("a", "the hint")""", "the hint"),
        ["Select"] = ("""Select("a", [SelectItem("x", "Option")], "the hint")""", "the hint"),
        ["DatePicker"] = ("""DatePicker("day")""", "mud-picker"),
        ["Slider"] = ("""Slider("a", "continuous", 0, 10, 1, [5], "Wind")""", "Wind: 5"),
        ["CheckBoxGroup"] = ("""CheckBoxGroup("a", [CheckBoxItem("Label", "Details", "x")])""", "Details"),
        ["RadioGroup"] = ("""RadioGroup("a", [RadioItem("Label", "Details", "x")])""", "Details"),
        ["SwitchGroup"] = ("""SwitchGroup("a", [SwitchItem("Label", "Details", "x")])""", "Label"),
        ["Chips"] = ("""Chips("a", "single", [ChipItem("x", "Label", Icon("wind"))])""", "Label"),
        ["OptionCards"] = ("""OptionCards("a", "single", [OptionCard("x", "Label", "Details")])""", "Details"),
        ["Button"] = ("""Button("Press")""", "Press"),
        ["Buttons"] = ("""Buttons([Button("Press")])""", "Press"),
        ["IconButton"] = ("""IconButton("Press", Icon("wind"))""", "Press"),
        ["Stack"] = ("""Stack([TextContent("inside")])""", "inside"),
        ["Tabs"] = ("""Tabs([TabItem("x", "Heading", [TextContent("inside")])])""", "inside"),
        ["Accordion"] = ("""Accordion([AccordionItem("x", "Heading", [TextContent("inside")])])""", "Heading"),
        ["Steps"] = ("""Steps([StepsItem("Heading", "the details")])""", "the details"),
        ["Carousel"] = ("""Carousel([[TextContent("inside")]])""", "inside"),
        ["Separator"] = ("""Separator()""", "mud-divider"),
        ["TagBlock"] = ("""TagBlock(["Corn"])""", "Corn"),
        ["Tag"] = ("""Tag("Corn")""", "Corn"),
        ["Icon"] = ("""Icon("wind")""", "<svg"),
        ["EntityList"] = ("""EntityList([{left: "Corn", right: "12"}])""", "Corn"),
        ["ListBlock"] = ("""ListBlock([ListItem("Heading", "under it")])""", "under it"),
        ["Text"] = ("""Text("text", "the value", "the subtext")""", "the subtext"),
        ["BoldText"] = ("""BoldText("text", "the value", "the subtext")""", "the subtext"),
        ["IconText"] = ("""IconText(Icon("wind"), null, null, "Heading", "under it")""", "under it"),
        ["ImageText"] = ("""ImageText("a.png", null, "Heading", "under it")""", "under it"),
        ["ImageTextLarge"] = ("""ImageTextLarge("a.png", null, "Heading", "under it")""", "under it"),
        ["MetricIndicatorInline"] = ("""MetricIndicatorInline("12", "the subtext")""", "the subtext"),
        ["MetricIndicatorWithStrikethrough"] = ("""MetricIndicatorWithStrikethrough("12", "the subtext", "10")""", "the subtext"),
        ["SnippetCardBlock"] = ("""SnippetCardBlock([SnippetCardItem("a", IconText(Icon("wind"), null, null, "Inside"))])""", "Inside"),
        ["OverviewCardBlock"] = ("""OverviewCardBlock([OverviewCardItem("a", Text("text", "Inside"))])""", "Inside"),
        ["ContextCardBlock"] = ("""ContextCardBlock([ContextCardItem("a", "Inside")])""", "Inside"),
        ["CompositeCardBlock"] = ("""CompositeCardBlock([CompositeCardItem("a", Text("text", "Inside"))])""", "Inside"),
        ["VisualCardBlock"] = ("""VisualCardBlock([VisualCardItem(BoldText("text", "Inside"))])""", "Inside"),
        ["Modal"] = ("""Modal("Heading", true, [TextContent("inside")])""", "inside"),

        // This host registers no theme choice, and that is what the view says.
        ["ThemeSwitcher"] = ("""ThemeSwitcher("Appearance")""", "no theme choice"),
    };

    public static TheoryData<string> Drawn() => [.. Programs.Keys];

    public static TheoryData<string> PromptOptionCases() => ["none", "tools", "rules"];

    private static PromptOptions? Options(string name) => name switch
    {
        "tools" => new PromptOptions { Tools = [new ToolDescriptor("getForecast", "Reads a forecast.")] },
        "rules" => new PromptOptions { AdditionalRules = ["Answer in French."] },
        _ => null,
    };

    [Theory]
    [MemberData(nameof(Drawn))]
    public async Task A_program_using_it_draws_with_no_errors(string name)
    {
        await using var context = TestLibrary.NewContext(services => services.AddMudServices().AddOpenUIComponents(TestLibrary.Mud));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var popovers = context.Render<MudPopoverProvider>();
        var dialogs = context.Render<MudDialogProvider>();
        var (program, drawn) = Programs[name];

        var cut = context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, $"root = {program}\n"));

        Assert.Empty(cut.Instance.Session.Current.Errors);
        Assert.DoesNotContain("oui-diagnostic", cut.Markup);
        Assert.Contains(drawn, cut.Markup + popovers.Markup + dialogs.Markup);
    }

    [Fact]
    public void The_programs_cover_every_component_that_gets_drawn() =>
        Assert.Equal(
            TestLibrary.Mud.Spec.Components.Select(component => component.Name).Where(name => !MudDefinitions.Items.Contains(name)).Order(),
            Programs.Keys.Order());

    [Fact]
    public void The_programs_use_every_item_a_parent_reads()
    {
        var called = Programs.Values.SelectMany(entry => CallName().Matches(entry.Program).Select(call => call.Groups[1].Value)).ToHashSet();

        // No signature in the spec takes a Slice, so no program can use one.
        Assert.Equal(["Slice"], MudDefinitions.Items.Where(item => !called.Contains(item)));
    }

    [Theory]
    [MemberData(nameof(PromptOptionCases))]
    public void The_package_teaches_what_the_spec_teaches_and_its_own_group_of_components(string options)
    {
        var prompt = TestLibrary.Mud.SystemPrompt(Options(options));
        var own = OwnGroup().Match(prompt);

        Assert.True(own.Success);
        Assert.Contains("ThemeSwitcher(label?: string)", own.Value);
        Assert.Equal(SystemPrompt.Generate(LibrarySpec.Parse(File.ReadAllText(SpecPath)), Options(options)), prompt.Remove(own.Index, own.Length));
    }

    [GeneratedRegex(@"\b([A-Z]\w*)\(")]
    private static partial Regex CallName();

    [GeneratedRegex(@"### Appearance\n.*\n\n")]
    private static partial Regex OwnGroup();
}
