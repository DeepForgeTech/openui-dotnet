using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace OpenUI.Lang.Blazor.Tests.Mud;

/// <summary>What the package's views do for themselves: the state a tab bar, a section, a card block, a modal and a table keep.</summary>
public sealed class MudViewBehaviourTests
{
    private static BunitContext NewContext()
    {
        var context = TestLibrary.NewContext(services => services.AddMudServices().AddOpenUIComponents(TestLibrary.Mud));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Render<MudPopoverProvider>();
        return context;
    }

    private static IRenderedComponent<OpenUIProgram> Render(BunitContext context, string program) =>
        context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, program));

    private static void Update(IRenderedComponent<OpenUIProgram> cut, string program) =>
        cut.Render(parameters => parameters.Add(component => component.Program, program));

    private static string Tabs(params string[] tabs) =>
        $"root = Tabs([{string.Join(", ", tabs.Select(tab => $"TabItem(\"{tab}\", \"{tab} tab\", [TextContent(\"{tab} body\")])"))}])\n";

    [Fact]
    public async Task Its_tabs_keep_the_pick_when_a_tab_arrives_ahead_of_it()
    {
        await using var context = NewContext();
        var cut = Render(context, Tabs("one", "two"));
        Assert.Contains("one body", cut.Markup);

        cut.FindAll(".mud-tab")[1].Click();
        cut.WaitForAssertion(() => Assert.Contains("two body", cut.Markup));

        Update(cut, Tabs("new", "one", "two"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("two body", cut.Markup);
            Assert.Equal("two tab", cut.Find(".mud-tab.mud-tab-active").TextContent.Trim());
        });
    }

    [Fact]
    public async Task Its_tab_content_is_spaced_from_the_tabs_on_every_mud_line()
    {
        await using var context = NewContext();
        var cut = Render(context, Tabs("one"));

        // The panels class has another name on each MudBlazor line, and MudTabs passes a name it lacks on to the markup.
        Assert.Contains("one body", cut.Find(".mud-tabs-panels .pt-3").TextContent);
        Assert.DoesNotContain("panelsclass", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Its_tabs_draw_repeated_values()
    {
        await using var context = NewContext();
        var cut = Render(context, Tabs("x", "x"));

        cut.FindAll(".mud-tab")[1].Click();

        Assert.Equal(2, cut.FindAll(".mud-tab").Count);
    }

    [Fact]
    public async Task Its_accordion_opens_the_section_the_user_picks()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = Accordion([AccordionItem(\"a\", \"A\", [TextContent(\"a body\")]), AccordionItem(\"b\", \"B\", [TextContent(\"b body\")])])\n");

        cut.FindAll(".mud-expand-panel-header")[1].Click();

        cut.WaitForAssertion(() =>
            Assert.Equal("B", cut.Find(".mud-expand-panel.mud-panel-expanded .mud-expand-panel-header").TextContent.Trim()));
        Assert.Single(cut.FindAll(".mud-expand-panel.mud-panel-expanded"));
    }

    [Fact]
    public async Task Its_card_block_runs_the_action_by_click_and_by_key_and_lays_itself_out()
    {
        await using var context = NewContext();
        var cut = Render(context, "$n = 0\nroot = Stack([SnippetCardBlock([SnippetCardItem(\"a\", IconText(Icon(\"wind\"), null, null, \"A\"))], \"grid\", true, @Set($n, $n + 1)), TextContent(\"Count \" + $n)])\n");

        Assert.Contains("display:grid", cut.Markup);
        cut.Find("[role=button]").Click();
        cut.WaitForAssertion(() => Assert.Contains("Count 1", cut.Markup));
        cut.Find("[role=button]").KeyDown(key: "Enter");
        cut.WaitForAssertion(() => Assert.Contains("Count 2", cut.Markup));
        cut.Find("[role=button]").KeyDown(key: "x");
        Assert.Contains("Count 2", cut.Markup);
    }

    [Fact]
    public async Task Its_button_runs_the_action()
    {
        await using var context = NewContext();
        var cut = Render(context, "$n = 0\nroot = Stack([Button(\"Add\", @Set($n, $n + 1)), TextContent(\"Count \" + $n)])\n");

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Contains("Count 1", cut.Markup));
    }

    [Fact]
    public async Task Its_modal_closes_for_good_and_leaves_with_the_program()
    {
        await using var context = NewContext();
        var dialogs = context.Render<MudDialogProvider>();
        const string open = "root = Stack([Modal(\"M\", true, [TextContent(\"inside\")]), TextContent(\"page\")])\n";
        var cut = Render(context, open);
        Assert.Contains("inside", dialogs.Markup);

        dialogs.Find(".mud-button-close").Click();
        dialogs.WaitForAssertion(() => Assert.DoesNotContain("inside", dialogs.Markup));

        Update(cut, open + "again = 1\n");
        Assert.DoesNotContain("inside", dialogs.Markup);
    }

    [Fact]
    public async Task Its_editable_table_keeps_an_edit_when_a_row_arrives()
    {
        await using var context = NewContext();
        const string column = "{type: \"text\", key: \"a\", header: \"A\"}";
        var cut = Render(context, $"root = EditableTable(\"t\", [{column}], [{{id: \"1\", values: [\"Corn\"]}}])\n");

        cut.Find("input").Change("Maize");
        cut.WaitForAssertion(() => Assert.Contains("Save Changes", cut.Markup));

        Update(cut, $"root = EditableTable(\"t\", [{column}], [{{id: \"1\", values: [\"Corn\"]}}, {{id: \"2\", values: [\"Wheat\"]}}])\n");

        cut.WaitForAssertion(() => Assert.Equal(["Maize", "Wheat"], cut.FindAll("input").Select(input => input.GetAttribute("value"))));
        Assert.Contains("Save Changes", cut.Markup);
    }
}
