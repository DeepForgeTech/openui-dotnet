using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using OpenUI.Lang.Blazor.Mud;

namespace OpenUI.Lang.Blazor.Tests.Mud;

public sealed class LayoutAndContentTests
{
    private const string TabsProgram =
        "root = Tabs([TabItem(\"a\", \"First\", [TextContent(\"Alpha text\")]), TabItem(\"b\", \"Second\", [TextContent(\"Beta text\")])])\n";

    private static BunitContext NewContext()
    {
        var context = TestLibrary.NewContext(services => services.AddMudServices().AddOpenUIComponents(TestLibrary.Mud));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Render<MudPopoverProvider>();
        return context;
    }

    private static IRenderedComponent<OpenUIProgram> Render(BunitContext context, string program, bool streaming = false) =>
        context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, program)
            .Add(component => component.IsStreaming, streaming));

    private static void Update(IRenderedComponent<OpenUIProgram> cut, string program, bool streaming = true) =>
        cut.Render(parameters => parameters
            .Add(component => component.Program, program)
            .Add(component => component.IsStreaming, streaming));

    private static string Literal(string text) => JsonSerializer.Serialize(text);

    [Fact]
    public async Task Tabs_show_the_first_tab_and_switch_when_another_is_picked()
    {
        await using var context = NewContext();
        var cut = Render(context, TabsProgram);
        Assert.Equal(["First", "Second"], cut.FindAll(".mud-tab").Select(tab => tab.TextContent.Trim()));
        Assert.Contains("Alpha text", cut.Markup);
        Assert.DoesNotContain("Beta text", cut.Markup);

        cut.FindAll(".mud-tab")[1].Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Beta text", cut.Markup);
            Assert.DoesNotContain("Alpha text", cut.Markup);
        });
    }

    [Fact]
    public async Task Tabs_follow_the_tab_being_written_until_the_user_picks_one()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = Tabs([TabItem(\"a\", \"First\", [TextContent(\"Alpha text\")])])\n", streaming: true);
        Assert.Contains("Alpha text", cut.Markup);

        Update(cut, "root = Tabs([TabItem(\"a\", \"First\", [TextContent(\"Alpha text\")]), TabItem(\"b\", \"Second\", [TextContent(\"Beta text\")])])\n");
        cut.WaitForAssertion(() => Assert.Contains("Beta text", cut.Markup));

        cut.FindAll(".mud-tab")[0].Click();
        cut.WaitForAssertion(() => Assert.Contains("Alpha text", cut.Markup));
        Update(cut, "root = Tabs([TabItem(\"a\", \"First\", [TextContent(\"Alpha text\")]), TabItem(\"b\", \"Second\", [TextContent(\"Beta text\")]), TabItem(\"c\", \"Third\", [TextContent(\"Gamma text\")])])\n");
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".mud-tab").Count));

        Assert.Contains("Alpha text", cut.Markup);
        Assert.DoesNotContain("Gamma text", cut.Markup);
    }

    [Fact]
    public async Task A_finished_program_opens_on_its_first_tab()
    {
        await using var context = NewContext();
        var cut = Render(context, TabsProgram);

        Update(cut, TabsProgram, streaming: false);

        Assert.Contains("Alpha text", cut.Markup);
    }

    [Fact]
    public async Task An_accordion_opens_one_section_at_a_time_and_closes_the_open_one()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = Accordion([AccordionItem(\"a\", \"First\", [TextContent(\"Alpha text\")]), AccordionItem(\"b\", \"Second\", [TextContent(\"Beta text\")])])\n");
        Assert.Empty(cut.FindAll(".mud-expand-panel.mud-panel-expanded"));

        cut.FindAll(".mud-expand-panel-header")[0].Click();
        cut.WaitForAssertion(() => Assert.Equal(["First"], OpenSections(cut)));

        cut.FindAll(".mud-expand-panel-header")[1].Click();
        cut.WaitForAssertion(() => Assert.Equal(["Second"], OpenSections(cut)));

        cut.FindAll(".mud-expand-panel-header")[1].Click();
        cut.WaitForAssertion(() => Assert.Empty(OpenSections(cut)));
    }

    [Fact]
    public async Task An_accordion_opens_the_section_that_arrives_until_the_user_acts()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = Accordion([AccordionItem(\"a\", \"First\", [TextContent(\"Alpha text\")])])\n", streaming: true);
        cut.WaitForAssertion(() => Assert.Equal(["First"], OpenSections(cut)));

        Update(cut, "root = Accordion([AccordionItem(\"a\", \"First\", [TextContent(\"Alpha text\")]), AccordionItem(\"b\", \"Second\", [TextContent(\"Beta text\")])])\n");
        cut.WaitForAssertion(() => Assert.Equal(["Second"], OpenSections(cut)));

        cut.FindAll(".mud-expand-panel-header")[1].Click();
        cut.WaitForAssertion(() => Assert.Empty(OpenSections(cut)));
        Update(cut, "root = Accordion([AccordionItem(\"a\", \"First\", [TextContent(\"Alpha text\")]), AccordionItem(\"b\", \"Second\", [TextContent(\"Beta text\")]), AccordionItem(\"c\", \"Third\", [TextContent(\"Gamma text\")])])\n");
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".mud-expand-panel-header").Count));

        Assert.Empty(OpenSections(cut));
    }

    [Fact]
    public async Task Steps_are_numbered_and_their_details_are_markdown()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = Steps([StepsItem(\"Mix\", \"Add **fungicide**\"), StepsItem(\"Spray\", \"Log the pass\")])\n");

        Assert.Equal(["1", "2"], cut.FindAll(".mud-timeline-item-dot-inner").Select(dot => dot.TextContent));
        Assert.Contains("Mix", cut.Markup);
        Assert.Equal("fungicide", cut.Find("strong").TextContent);
    }

    [Fact]
    public async Task A_carousel_shows_one_slide_and_the_arrows_move_between_them()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = Carousel([[TextContent(\"One\")], [TextContent(\"Two\")], [TextContent(\"Three\")]])\n");
        Assert.Contains("One", cut.Markup);
        Assert.DoesNotContain("Two", cut.Markup);
        Assert.Contains("1 / 3", cut.Markup);
        Assert.True(cut.Find("button[aria-label=Previous]").HasAttribute("disabled"));

        cut.Find("button[aria-label=Next]").Click();
        cut.Find("button[aria-label=Next]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Three", cut.Markup);
            Assert.Contains("3 / 3", cut.Markup);
            Assert.True(cut.Find("button[aria-label=Next]").HasAttribute("disabled"));
        });

        cut.Find("button[aria-label=Previous]").Click();

        cut.WaitForAssertion(() => Assert.Contains("Two", cut.Markup));
    }

    // MudBlazor draws a dialog in its provider, outside the program's own markup.
    private static IRenderedComponent<MudDialogProvider> RenderDialogs(BunitContext context) => context.Render<MudDialogProvider>();

    [Fact]
    public async Task A_modal_opens_from_its_variable_and_the_close_button_writes_false_back()
    {
        await using var context = NewContext();
        var dialogs = RenderDialogs(context);
        var cut = Render(context, "$open = false\nroot = Stack([Button(\"Show\", @Set($open, true)), Modal(\"Confirm spray\", $open, [TextContent(\"Spray Monday?\")]), TextContent($open ? \"state open\" : \"state closed\")])\n");
        Assert.DoesNotContain("Spray Monday?", dialogs.Markup);
        Assert.Contains("state closed", cut.Markup);

        await cut.ClickWhere("button", "Show");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Confirm spray", dialogs.Markup);
            Assert.Contains("Spray Monday?", dialogs.Markup);
            Assert.Contains("state open", cut.Markup);
        });

        dialogs.Find("button[aria-label=Close]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Spray Monday?", dialogs.Markup);
            Assert.Contains("state closed", cut.Markup);
        });
        Assert.Equal("false", cut.Instance.Session.State.Get("$open").ToString());
    }

    [Fact]
    public async Task A_modal_follows_the_program_s_state_while_it_is_open()
    {
        await using var context = NewContext();
        var dialogs = RenderDialogs(context);
        var cut = Render(context, "$open = true\n$n = 0\nroot = Stack([Modal(\"Count\", $open, [TextContent(\"Taps \" + $n), Button(\"Tap\", @Set($n, $n + 1))])])\n");
        Assert.Contains("Taps 0", dialogs.Markup);

        await dialogs.InvokeAsync(() => dialogs.FindAll("button").First(button => button.TextContent.Contains("Tap")).Click());

        cut.WaitForAssertion(() => Assert.Contains("Taps 1", dialogs.Markup));
    }

    [Fact]
    public async Task A_button_in_a_modal_can_close_it()
    {
        await using var context = NewContext();
        var dialogs = RenderDialogs(context);
        Render(context, "$open = true\nroot = Modal(\"Confirm\", $open, [Buttons([Button(\"Done\", @Set($open, false))])])\n");
        Assert.Contains("Confirm", dialogs.Markup);

        await dialogs.InvokeAsync(() => dialogs.FindAll("button").First(button => button.TextContent.Contains("Done")).Click());

        dialogs.WaitForAssertion(() => Assert.DoesNotContain("Confirm", dialogs.Markup));
    }

    [Theory]
    [InlineData("sm", "mud-dialog-width-sm")]
    [InlineData("md", "mud-dialog-width-md")]
    [InlineData("lg", "mud-dialog-width-lg")]
    public async Task A_modal_takes_its_width_from_its_size(string size, string widthClass)
    {
        await using var context = NewContext();
        var dialogs = RenderDialogs(context);
        Render(context, $"$open = true\nroot = Modal(\"Wide\", $open, [TextContent(\"Body\")], \"{size}\")\n");

        Assert.Contains(widthClass, dialogs.Find(".mud-dialog").ClassList);
    }

    [Fact]
    public async Task A_modal_with_no_open_binding_is_closed_and_one_set_open_can_still_be_dismissed()
    {
        await using var context = NewContext();
        var dialogs = RenderDialogs(context);
        Render(context, "root = Modal(\"Never\", null, [TextContent(\"Nobody sees this\")])\n");
        Assert.DoesNotContain("Nobody sees this", dialogs.Markup);

        var cut = Render(context, "root = Modal(\"Notice\", true, [TextContent(\"Read this\")])\n");
        Assert.Contains("Read this", dialogs.Markup);

        dialogs.Find("button[aria-label=Close]").Click();

        dialogs.WaitForAssertion(() => Assert.DoesNotContain("Read this", dialogs.Markup));
        Update(cut, "root = Modal(\"Notice\", true, [TextContent(\"Read this\")])\n", streaming: false);
        Assert.DoesNotContain("Read this", dialogs.Markup);
        Assert.Empty(dialogs.FindAll(".mud-overlay"));
    }

    [Fact]
    public async Task Text_cards_and_headers_draw_what_they_are_given()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = Stack([CardHeader(\"North field\", \"42 acres\"), InlineHeader(\"**Bold** heading\", \"Plain *note*\"), TextCallout(\"danger\", \"Stop\", \"Keep out\"), CodeBlock(\"json\", \"{\\\"a\\\": 1}\")])\n");

        Assert.Contains("North field", cut.Markup);
        Assert.Contains("42 acres", cut.Markup);
        Assert.Equal("Bold", cut.Find("strong").TextContent);
        Assert.Equal("note", cut.Find("em").TextContent);
        Assert.Empty(cut.FindAll("p strong"));
        Assert.Contains("mud-alert-outlined-error", cut.Find(".mud-alert").ClassList);
        Assert.Contains("Stop", cut.Find(".mud-alert").TextContent);
        Assert.Equal("{\"a\": 1}", cut.Find("pre code").TextContent);
        Assert.Contains("json", cut.Markup);
    }

    [Fact]
    public async Task A_code_block_is_text_whatever_the_code_says()
    {
        await using var context = NewContext();
        var cut = Render(context, $"root = CodeBlock({Literal("html\"><script>alert(1)</script>")}, {Literal("<script>alert(1)</script><img src=x onerror=alert(2)>")})\n");

        Assert.Empty(cut.FindAll("script, img"));
        Assert.Equal("<script>alert(1)</script><img src=x onerror=alert(2)>", cut.Find("pre code").TextContent);
        AssertNoActiveMarkup(cut);
    }

    [Fact]
    public async Task Markdown_draws_in_text_content_the_markdown_renderer_and_a_callout()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = Stack([TextContent(\"A **bold** word\"), MarkDownRenderer(\"- one\\n- two\\n\\n| A | B |\\n| - | - |\\n| 1 | 2 |\"), Callout(\"info\", \"Heads up\", \"Mind the *wind*\")])\n");

        Assert.Equal("bold", cut.Find("strong").TextContent);
        Assert.Equal(["one", "two"], cut.FindAll("li").Select(item => item.TextContent));
        Assert.Equal(["A", "B", "1", "2"], cut.FindAll("table th, table td").Select(cell => cell.TextContent));
        Assert.Equal("wind", cut.Find(".mud-alert em").TextContent);
    }

    [Fact]
    public async Task Text_content_breaks_where_the_text_does_and_the_markdown_renderer_does_not()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = Stack([TextContent(\"one\\ntwo\"), MarkDownRenderer(\"three\\nfour\")])\n");

        Assert.Single(cut.FindAll("br"));
    }

    [Fact]
    public async Task A_markdown_renderer_container_follows_its_variant()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = Stack([MarkDownRenderer(\"clear\", \"clear\"), MarkDownRenderer(\"card\", \"card\"), MarkDownRenderer(\"sunk\", \"sunk\")])\n");

        var papers = cut.FindAll(".mud-paper");
        Assert.Equal(2, papers.Count);
        Assert.Contains("mud-paper-outlined", papers[0].ClassList);
        Assert.DoesNotContain("mud-paper-outlined", papers[1].ClassList);
        Assert.Contains("background-gray", papers[1].GetAttribute("style"));
    }

    [Theory]
    [InlineData("TextContent")]
    [InlineData("MarkDownRenderer")]
    [InlineData("Callout")]
    public async Task Markdown_cannot_add_markup_or_a_script_whatever_the_program_says(string component)
    {
        await using var context = NewContext();
        var hostile = string.Join("\n\n", HostileMarkdown);
        var call = component == "Callout" ? $"Callout(\"info\", \"Title\", {Literal(hostile)})" : $"{component}({Literal(hostile)})";
        var cut = Render(context, $"root = {call}\n");

        AssertNoActiveMarkup(cut);
        Assert.Contains("&lt;script&gt;", cut.Markup);
        Assert.Equal(["https://example.com/ok", "mailto:dale@example.com"], cut.FindAll("a[href]").Select(link => link.GetAttribute("href")).Distinct().Where(href => !href!.Contains("example.org")));
    }

    [Fact]
    public async Task Markdown_links_that_survive_open_in_a_new_tab_without_the_opener()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = MarkDownRenderer(\"[ok](https://example.com/ok)\")\n");

        var link = cut.Find("a");
        Assert.Equal("https://example.com/ok", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
    }

    [Fact]
    public async Task Markdown_that_is_cut_off_at_any_point_still_draws()
    {
        await using var context = NewContext();
        var document = string.Join("\n\n", HostileMarkdown) + "\n\n```js\nlet a = 1;\n```\n\n| A | B |\n| - | - |\n| *1* | **2** |";
        var text = Literal(document);
        var cut = Render(context, "root = MarkDownRenderer(\"\")\n", streaming: true);

        for (var length = 0; length <= text.Length; length += 3)
        {
            Update(cut, $"root = MarkDownRenderer({text[..length]}\n");
            AssertNoActiveMarkup(cut);
        }

        Assert.Empty(cut.Instance.Session.Current.Errors);
    }

    [Fact]
    public async Task Markdown_nested_too_deep_for_the_parser_shows_as_plain_text()
    {
        await using var context = NewContext();
        var nested = string.Concat(Enumerable.Repeat("> ", 300)) + "<script>alert(1)</script>";
        var cut = Render(context, $"root = Stack([TextContent({Literal(nested)}), InlineHeader({Literal(nested)})])\n");

        Assert.Empty(cut.Instance.Session.Current.Errors);
        Assert.Contains("&gt; &gt; &gt;", cut.Markup);
        AssertNoActiveMarkup(cut);
    }

    // Each of these is a way to get script or foreign markup onto the page through markdown.
    private static readonly string[] HostileMarkdown =
    [
        "<script>alert(1)</script>",
        "<img src=x onerror=alert(1)>",
        "<iframe src=\"javascript:alert(1)\"></iframe> <a href=\"javascript:alert(1)\" onclick=\"alert(1)\">raw</a>",
        "<!-- comment --> <style>body{display:none}</style>",
        "[inline](javascript:alert(1)) [case](JaVaScRiPt:alert(1)) [entity](&#106;avascript:alert(1)) [tab](java\tscript:alert(1))",
        "[data](data:text/html,<script>alert(1)</script>) [vb](vbscript:msgbox(1)) [file](file:///etc/passwd) [relative](/admin) [empty]()",
        "[ref][r] [r]: javascript:alert(1)",
        "<javascript:alert(1)> <data:text/html;base64,PHNjcmlwdD4=>",
        "![image](https://example.org/pixel.png) ![bad](javascript:alert(1) \"t\")",
        "[title](https://example.org/a \"x\" onmouseover=\"alert(1)\") <https://example.org/a\" onmouseover=\"alert(1)>",
        "*text*{onclick=\"alert(1)\"} {#id .class onfocus=alert(1)}",
        "[ok](https://example.com/ok) <mailto:dale@example.com>",
    ];

    private static void AssertNoActiveMarkup(IRenderedComponent<OpenUIProgram> cut)
    {
        Assert.Empty(cut.FindAll("script, iframe, img, style, object, embed, svg image"));
        foreach (var element in cut.FindAll("*"))
        {
            foreach (var attribute in element.Attributes)
            {
                Assert.False(attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase), $"{element.TagName} {attribute.Name}");
                Assert.DoesNotContain("javascript:", attribute.Value, StringComparison.OrdinalIgnoreCase);
            }
        }

        foreach (var link in cut.FindAll("a[href]"))
        {
            var href = link.GetAttribute("href")!;
            Assert.True(href.StartsWith("https://", StringComparison.Ordinal) || href.StartsWith("http://", StringComparison.Ordinal) || href.StartsWith("mailto:", StringComparison.Ordinal), href);
        }
    }

    private static List<string> OpenSections(IRenderedComponent<OpenUIProgram> cut) =>
        [.. cut.FindAll(".mud-expand-panel.mud-panel-expanded").Select(panel => panel.QuerySelector(".mud-expand-panel-header")!.TextContent.Trim())];

    // Cards in a row that wraps may not get narrower than 18rem, since they wrap instead. In a row that does not, a minimum
    // would be wider than a phone, and the row would scroll sideways. The browser checks this at 375 and 700 px.
    [Theory]
    [InlineData(", true", "18rem")]
    [InlineData("", "0px")]
    public async Task A_stack_lets_its_cards_get_as_narrow_as_it_can_wrap_them(string wrap, string minimum)
    {
        await using var context = NewContext();

        var cut = Render(context, $"root = Stack([Card([TextContent(\"a\")]), Card([TextContent(\"b\")])], \"row\", null, null, null{wrap})\n");

        Assert.Contains($"--oui-card-min-width: {minimum}", cut.Find("[role=group]").GetAttribute("style"));
        Assert.Contains("min-width: min(var(--oui-card-min-width, 0px), 100%)", cut.Find(".mud-card").GetAttribute("style"));
    }

    // A Card holding cards in a row is a stack of cards like any other.
    [Theory]
    [InlineData(", null, null, null, true", "18rem")]
    [InlineData("", "0px")]
    public async Task A_card_lets_the_cards_in_its_row_get_as_narrow_as_it_can_wrap_them(string wrap, string minimum)
    {
        await using var context = NewContext();

        var cut = Render(context, $"root = Card([Card([TextContent(\"a\")]), Card([TextContent(\"b\")])], \"card\", \"row\"{wrap})\n");

        Assert.Contains($"--oui-card-min-width: {minimum}", cut.Find(".mud-card-content [role=group]").GetAttribute("style"));
    }
}
