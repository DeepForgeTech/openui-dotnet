using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using OpenUI.Lang.Blazor.Mud;
using OpenUI.Lang.Runtime;

namespace OpenUI.Lang.Blazor.Tests.Mud;

/// <summary>The media, data display and card components of the MudBlazor set.</summary>
public sealed class MudDisplayTests
{
    private const string Neutral = Icons.Material.Filled.RadioButtonChecked;

    private static BunitContext NewContext()
    {
        var context = TestLibrary.NewContext(services => services.AddMudServices().AddOpenUIComponents(TestLibrary.Mud));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Render<MudPopoverProvider>();
        return context;
    }

    private static IRenderedComponent<OpenUIProgram> Render(BunitContext context, string program, List<OpenUIHostEvent>? events = null) =>
        context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.OnHostEvent, (OpenUIHostEvent e) => events?.Add(e))
            .Add(component => component.Program, program + "\n"));

    // What MudBlazor draws inside the svg for a Material icon.
    private static string GlyphOf(BunitContext context, string icon) =>
        context.Render<MudIcon>(parameters => parameters.Add(component => component.Icon, icon)).Find("svg").InnerHtml;

    private static IEnumerable<string> GlyphsIn(IRenderedComponent<OpenUIProgram> cut) => cut.FindAll("svg").Select(svg => svg.InnerHtml);

    // One program for each card block, with two cards that show the two texts "First" and "Second".
    public static TheoryData<string, string> CardBlocks => new()
    {
        { "SnippetCardBlock", """SnippetCardBlock([SnippetCardItem("a", IconText(Icon("wind"), null, null, "First"), Text("text", "8 mph")), SnippetCardItem("b", IconText(Icon("sun"), null, null, "Second"), Text("text", "Clear"))]{0})""" },
        { "OverviewCardBlock", """OverviewCardBlock([OverviewCardItem("a", Text("text", "First"), MetricIndicatorInline("$4.62")), OverviewCardItem("b", Text("text", "Second"), MetricIndicatorInline("$11.90"))]{0})""" },
        { "ContextCardBlock", """ContextCardBlock([ContextCardItem("a", "First", "Gusts"), ContextCardItem("b", Tag("Second"), "Frost")]{0})""" },
        { "CompositeCardBlock", """CompositeCardBlock([CompositeCardItem("a", Text("text", "First")), CompositeCardItem("b", Text("text", "Second"))]{0})""" },
        { "VisualCardBlock", """VisualCardBlock([VisualCardItem(BoldText("text", "First"), "a"), VisualCardItem(BoldText("text", "Second"), "b")]{0})""" },
    };

    [Theory]
    [MemberData(nameof(CardBlocks))]
    public async Task A_card_block_draws_every_item(string block, string program)
    {
        await using var context = NewContext();
        var cut = Render(context, "root = " + string.Format(program, ""));

        Assert.True(cut.Markup.Contains("First") && cut.Markup.Contains("Second"), block);
        Assert.Empty(cut.Instance.Session.Current.Errors);
    }

    [Theory]
    [MemberData(nameof(CardBlocks))]
    public async Task A_card_block_with_an_action_runs_it_on_a_click_and_without_one_is_not_clickable(string block, string program)
    {
        await using var context = NewContext();
        var events = new List<OpenUIHostEvent>();
        var clickable = Render(context, "root = " + string.Format(program, """, "grid", true, @ToAssistant("opened")"""), events);
        var plain = Render(context, "root = " + string.Format(program, ""));

        Assert.True(clickable.FindAll("[role=button]").Count == 2, block);
        Assert.Empty(plain.FindAll("[role=button]"));
        clickable.FindAll("[role=button]")[1].Click();

        clickable.WaitForAssertion(() => Assert.Single(events));
        Assert.Equal("continue_conversation", events[0].Type);
        Assert.Equal("opened", events[0].HumanFriendlyMessage);
    }

    [Fact]
    public async Task A_card_responds_to_the_keyboard_as_a_button()
    {
        await using var context = NewContext();
        var events = new List<OpenUIHostEvent>();
        var cut = Render(context, """root = ContextCardBlock([ContextCardItem("a", "First", "Gusts")], "grid", true, @ToAssistant("opened"))""", events);

        cut.Find("[role=button]").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Tab" });
        cut.Find("[role=button]").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => Assert.Single(events));
    }

    [Fact]
    public async Task A_button_in_a_card_footer_runs_its_own_action_and_not_the_card_s()
    {
        await using var context = NewContext();
        var events = new List<OpenUIHostEvent>();
        var cut = Render(context, """root = CompositeCardBlock([CompositeCardItem("a", null, [Text("text", "Body")], {price: BoldText("number", "$318"), button: Button("Add", @ToAssistant("added"))})], "grid", true, @ToAssistant("opened"))""", events);
        Assert.Contains("$318", cut.Markup);

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Single(events));
        Assert.Equal("added", events[0].HumanFriendlyMessage);
    }

    [Fact]
    public async Task A_composite_card_draws_its_header_body_and_footer()
    {
        await using var context = NewContext();
        var cut = Render(context, """root = CompositeCardBlock([CompositeCardItem("a", ImageTextLarge("images/a.svg", "Seed", "Hybrid A", "112 day"), [Text("text", "Strong stalks"), TagBlock(["Corn"]), EntityList([{left: "Population", right: "34,000"}])], {price: MetricIndicatorWithStrikethrough("$318", "per bag", "$342"), button: Button("Add")})])""");

        Assert.Contains("Hybrid A", cut.Markup);
        Assert.Contains("Strong stalks", cut.Markup);
        Assert.Contains("Population", cut.Markup);
        Assert.Contains("$342", cut.Markup);
        Assert.Contains("Add", cut.Find("button").TextContent);
    }

    [Fact]
    public async Task A_card_block_lays_out_as_a_grid_or_a_scrolling_row_and_ignores_a_gap_that_is_not_a_length()
    {
        await using var context = NewContext();
        var grid = Render(context, """root = VisualCardBlock([VisualCardItem(BoldText("text", "A"))])""");
        var carousel = Render(context, """root = VisualCardBlock([VisualCardItem(BoldText("text", "A")), VisualCardItem(BoldText("text", "B"))], "carousel", true, null, 24)""");
        var hostile = Render(context, """root = VisualCardBlock([VisualCardItem(BoldText("text", "A"))], "grid", true, null, "4px;background:url(//evil.test/x)")""");

        Assert.Contains("display:grid", grid.Find("div").GetAttribute("style"));
        var row = carousel.Find("div").GetAttribute("style");
        Assert.Contains("overflow-x:auto", row);
        Assert.Contains("gap:24px", row);
        Assert.DoesNotContain("evil", hostile.Markup);
        Assert.Contains("gap:12px", hostile.Find("div").GetAttribute("style"));
    }

    [Fact]
    public async Task A_card_with_a_picture_draws_it_as_an_element_and_one_that_is_not_loadable_draws_its_alt_text()
    {
        await using var context = NewContext();
        var picture = Render(context, """root = VisualCardBlock([VisualCardItem(BoldText("text", "Storm"), "a", "images/storm.svg", Tag("Radar"), "Storm cell")])""");
        var blocked = Render(context, """root = ContextCardBlock([ContextCardItem("a", "Radar", "Storm", null, "javascript:alert(1)", "Storm cell")])""");

        Assert.Equal("images/storm.svg", picture.Find("img").GetAttribute("src"));
        Assert.Empty(blocked.FindAll("img"));
        Assert.Contains("Storm cell", blocked.Markup);
        Assert.DoesNotContain("javascript", blocked.Markup);
    }

    [Theory]
    [InlineData("""https://x.test/a.png"); background: url("javascript:alert(1)""", true)]
    [InlineData("""x.png"); position/fixed;inset/0;background/url("//evil.test/y""", true)]
    [InlineData("""x.png"); position:fixed;inset:0;background:url("//evil.test/y""", false)]
    public async Task A_background_image_cannot_inject_css(string source, bool drawn)
    {
        await using var context = NewContext();
        var literal = source.Replace("\"", "\\\"");
        var cut = Render(context, $$"""root = VisualCardBlock([VisualCardItem(BoldText("text", "Storm"), "a", "{{literal}}", null, "Storm")])""");

        Assert.All(cut.FindAll("[style]"), element => Assert.DoesNotContain("url(", element.GetAttribute("style")));
        Assert.All(cut.FindAll("[style]"), element => Assert.DoesNotContain("fixed", element.GetAttribute("style")));
        Assert.Equal(drawn ? [source] : [], cut.FindAll("img").Select(image => image.GetAttribute("src")));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData(" javascript:alert(1)")]
    [InlineData("data:image/svg+xml,<svg onload=alert(1)>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("//evil.test/a.png")]
    [InlineData("\\\\evil.test\\a.png")]
    [InlineData("https:evil.test/a.png")]
    public async Task An_image_with_a_source_that_is_not_http_https_or_relative_draws_its_alt_text(string source)
    {
        await using var context = NewContext();
        var literal = source.Replace("\\", "\\\\");
        var cut = Render(context, $$"""
            root = Stack([Image("Alt one", "{{literal}}"), ImageBlock("{{literal}}", "Alt two"), ImageText("{{literal}}", "Alt three", "Title"), ImageTextLarge("{{literal}}", "Alt four", "Title"), ImageGallery([{src: "{{literal}}", alt: "Alt five"}]), ListBlock([ListItem("Row", null, {src: "{{literal}}", alt: "Alt six"})], "image")])
            """);

        Assert.Empty(cut.FindAll("img"));
        Assert.All(["Alt one", "Alt two", "Alt three", "Alt four", "Alt five", "Alt six"], alt => Assert.Contains(alt, cut.Markup));
        Assert.DoesNotContain("javascript", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evil", cut.Markup);
    }

    [Theory]
    [InlineData("https://x.test/a.png")]
    [InlineData("http://x.test/a.png")]
    [InlineData("HTTPS://X.TEST/A.PNG")]
    [InlineData("images/a.png")]
    [InlineData("./a.png")]
    [InlineData("../a.png")]
    [InlineData("/images/a.png")]
    [InlineData("a/b:c.png")]
    [InlineData("a.png?next=javascript:alert(1)")]
    public void An_image_source_that_is_http_https_or_relative_is_kept(string source) =>
        Assert.Equal(source, DisplayValues.ImageSource(source));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("java\tscript:alert(1)")]
    [InlineData("java\nscript:alert(1)")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:a@b.co")]
    [InlineData("ftp://x.test/a.png")]
    [InlineData("a:b")]
    [InlineData("//x.test/a.png")]
    [InlineData("\\x.test\\a.png")]
    [InlineData("https://x.test/\\evil")]
    public void An_image_source_that_is_anything_else_is_dropped(string? source) =>
        Assert.Null(DisplayValues.ImageSource(source));

    [Fact]
    public async Task An_image_draws_its_source_and_alt_text()
    {
        await using var context = NewContext();
        var cut = Render(context, """root = Stack([Image("North 40", "images/field.svg"), ImageBlock("https://x.test/storm.png", "Storm"), ImageBlock("")])""");

        var images = cut.FindAll("img");
        Assert.Equal(2, images.Count);
        Assert.Equal("images/field.svg", images[0].GetAttribute("src"));
        Assert.Equal("North 40", images[0].GetAttribute("alt"));
        Assert.Equal("https://x.test/storm.png", images[1].GetAttribute("src"));
        Assert.Equal("no-referrer", images[1].GetAttribute("referrerpolicy"));
    }

    [Fact]
    public async Task An_image_text_takes_its_alt_from_the_title_and_clamps_the_size_it_is_given()
    {
        await using var context = NewContext();
        var cut = Render(context, """root = Stack([ImageText("a.png", null, "Probe 3", "31%", true, "vertical", 96), ImageText("b.png", null, "Probe 5", null, false, "horizontal", 1e9), ImageText("c.png", "Sensor", "Probe 7")])""");

        var images = cut.FindAll("img");
        Assert.Equal(["Probe 3", "Probe 5", "Sensor"], images.Select(image => image.GetAttribute("alt")));
        var boxes = images.Select(image => image.ParentElement!.GetAttribute("style")).ToList();
        Assert.Contains("width:96px;height:96px", boxes[0]);
        Assert.Contains("width:512px;height:512px", boxes[1]);
        Assert.Contains("width:40px;height:40px", boxes[2]);
    }

    [Fact]
    public async Task A_gallery_shows_five_photos_and_opens_all_of_them_in_a_viewer()
    {
        await using var context = NewContext();
        var photos = string.Join(", ", Enumerable.Range(1, 6).Select(n => $$"""{src: "p{{n}}.png", alt: "Photo {{n}}", details: "Detail {{n}}"}"""));
        var cut = Render(context, $"root = ImageGallery([{photos}])");

        Assert.Equal(5, cut.FindAll("img").Count);
        Assert.Empty(cut.FindAll(".mud-overlay"));
        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Contains("Detail 1", cut.Markup));
        Assert.Contains("1 of 6", cut.Markup);
        cut.Find("[aria-label='Next photo']").Click();
        cut.WaitForAssertion(() => Assert.Contains("Detail 2", cut.Markup));
        cut.Find("[aria-label='Close gallery']").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[aria-label='Next photo']")));
    }

    [Fact]
    public async Task A_gallery_photo_opens_the_viewer_at_that_photo_and_a_gallery_with_no_photos_draws_nothing()
    {
        await using var context = NewContext();
        var cut = Render(context, """root = ImageGallery([{src: "a.png", alt: "A", details: "About A"}, {src: "b.png", alt: "B", details: "About B"}])""");
        var empty = Render(context, "root = ImageGallery([])");

        cut.FindAll("[role=button]")[1].Click();

        cut.WaitForAssertion(() => Assert.Contains("About B", cut.Markup));
        Assert.DoesNotContain("About A", cut.Markup);
        Assert.Empty(empty.FindAll("img"));
    }

    [Fact]
    public async Task An_icon_draws_the_material_icon_for_its_name_in_any_spelling()
    {
        await using var context = NewContext();
        var cut = Render(context, """root = Stack([Icon("circle-check"), Icon("CircleCheck"), Icon("circle_check"), Icon("check-circle")])""");

        Assert.Equal([GlyphOf(context, Icons.Material.Filled.CheckCircle)], GlyphsIn(cut).Distinct());
        Assert.Equal(4, cut.FindAll("svg").Count);
    }

    [Fact]
    public async Task An_unknown_icon_draws_a_neutral_dot_or_its_category_s_icon_and_never_its_name()
    {
        await using var context = NewContext();
        var cut = Render(context, """root = Stack([Icon("zz-no-such-icon"), Icon("zz-no-such-icon", "weather"), Icon("<img src=x onerror=alert(1)>"), Icon(""), Icon("zz-no-such-icon", "Finance")])""");

        var glyphs = GlyphsIn(cut).ToList();
        Assert.Equal(
            [GlyphOf(context, Neutral), GlyphOf(context, Icons.Material.Filled.Cloud), GlyphOf(context, Neutral), GlyphOf(context, Icons.Material.Filled.AttachMoney)],
            glyphs);
        Assert.DoesNotContain("zz-no-such-icon", cut.Markup);
        Assert.DoesNotContain("onerror", cut.Markup);
        Assert.DoesNotContain("<img", cut.Markup);
    }

    [Fact]
    public async Task An_icon_button_runs_its_action_and_labels_itself_with_its_name()
    {
        await using var context = NewContext();
        var events = new List<OpenUIHostEvent>();
        var cut = Render(context, """root = Stack([IconButton("Refresh forecast", Icon("refresh-cw"), @ToAssistant("refresh")), IconButton("Open the map", Icon("map-pin"))])""", events);
        var buttons = cut.FindAll("button");

        Assert.Equal(["Refresh forecast", "Open the map"], buttons.Select(button => button.GetAttribute("aria-label")));
        Assert.Equal(GlyphOf(context, Icons.Material.Filled.Refresh), buttons[0].QuerySelector("svg")!.InnerHtml);
        buttons[0].Click();
        cut.WaitForAssertion(() => Assert.Single(events));
        cut.FindAll("button")[1].Click();

        cut.WaitForAssertion(() => Assert.Equal(2, events.Count));
        Assert.Equal("refresh", events[0].HumanFriendlyMessage);
        Assert.Equal("Open the map", events[1].HumanFriendlyMessage);
    }

    [Fact]
    public async Task An_icon_text_draws_its_icon_title_and_subtitle_and_tints_the_badge_by_its_variant()
    {
        await using var context = NewContext();
        var cut = Render(context, """root = Stack([IconText(Icon("thermometer"), "danger", "m", "Frost", "Low of 28 F", true, "vertical"), IconText(null, null, null, "No icon")])""");

        Assert.Contains("Frost", cut.Markup);
        Assert.Contains("Low of 28 F", cut.Markup);
        Assert.Single(cut.FindAll("svg"));
        Assert.Contains("var(--mud-palette-error)", cut.Markup);
    }

    [Theory]
    [InlineData("""IconText(Icon("wind"), null, null, "Heading", "Under it")""")]
    [InlineData("""ImageText("a.png", null, "Heading", "Under it")""")]
    public async Task A_subtitle_sits_on_its_own_line_in_the_muted_colour(string program)
    {
        await using var context = NewContext();
        var cut = Render(context, "root = " + program);

        var subtitle = cut.FindAll(".mud-typography").Single(text => text.TextContent == "Under it");
        Assert.Equal("DIV", subtitle.TagName);
        Assert.DoesNotContain("mud-secondary-text", subtitle.ClassName);
        Assert.Contains("var(--mud-palette-text-secondary)", subtitle.GetAttribute("style"));
    }

    [Theory]
    [InlineData("""CheckBoxGroup("a", [CheckBoxItem("Label", "Under it", "x")])""")]
    [InlineData("""RadioGroup("a", [RadioItem("Label", "Under it", "x")])""")]
    public async Task A_choice_s_description_sits_on_its_own_line_in_the_muted_colour(string program)
    {
        await using var context = NewContext();
        var cut = Render(context, "root = " + program);

        var description = cut.FindAll(".mud-typography").Single(text => text.TextContent == "Under it");
        Assert.Contains("display: block", description.GetAttribute("style"));
        Assert.Contains("var(--mud-palette-text-secondary)", description.GetAttribute("style"));
    }

    [Fact]
    public async Task A_tag_block_draws_a_chip_for_each_tag()
    {
        await using var context = NewContext();
        var cut = Render(context, """root = TagBlock(["Corn", "V8", "North 40"], "lg")""");

        Assert.Equal(["Corn", "V8", "North 40"], cut.FindAll(".mud-chip-content").Select(chip => chip.TextContent));
        Assert.All(cut.FindComponents<MudChip<string>>(), chip => Assert.Equal(Size.Large, chip.Instance.Size));
    }

    [Fact]
    public async Task An_entity_list_draws_its_rows_and_only_a_default_size_draws_the_header_and_footer()
    {
        await using var context = NewContext();
        var rows = """[{left: "North 40", right: "412", rightVariant: "number"}, {left: "South 80", right: "78"}], "SIZE", {left: "Field", right: "Acres"}, {left: "Total", right: "490"}""";
        var normal = Render(context, "root = EntityList(" + rows.Replace("SIZE", "default") + ")");
        var small = Render(context, "root = EntityList(" + rows.Replace("SIZE", "small") + ")");

        Assert.Equal(2, normal.FindAll("tbody tr").Count);
        Assert.Contains("Field", normal.Find("thead").TextContent);
        Assert.Contains("Total", normal.Find("tfoot").TextContent);
        Assert.Contains("tabular-nums", normal.FindAll("tbody td")[1].GetAttribute("style"));
        Assert.DoesNotContain("tabular-nums", normal.FindAll("tbody td")[3].GetAttribute("style"));
        Assert.Equal(2, small.FindAll("tbody tr").Count);
        Assert.Empty(small.FindAll("thead"));
        Assert.Empty(small.FindAll("tfoot"));
    }

    [Fact]
    public async Task A_list_draws_its_items_and_only_an_item_with_an_action_is_clickable()
    {
        await using var context = NewContext();
        var events = new List<OpenUIHostEvent>();
        var cut = Render(context, """root = ListBlock([ListItem("Scout", "North 40"), ListItem("Calibrate", null, null, "Open checklist", @ToAssistant("calibrate")), ListItem("Order", null, null, "Ignored label")])""", events);

        Assert.Contains("Scout", cut.Markup);
        Assert.Contains("North 40", cut.Markup);
        Assert.Contains("Order", cut.Markup);
        Assert.Contains("Open checklist", cut.Markup);
        Assert.DoesNotContain("Ignored label", cut.Markup);
        var buttons = cut.FindAll("[role=button]");
        Assert.Single(buttons);
        buttons[0].Click();

        cut.WaitForAssertion(() => Assert.Single(events));
        Assert.Equal("calibrate", events[0].HumanFriendlyMessage);
    }

    [Fact]
    public async Task A_list_numbers_its_items_or_shows_their_images()
    {
        await using var context = NewContext();
        var numbered = Render(context, """root = ListBlock([ListItem("A"), ListItem("B")])""");
        var pictured = Render(context, """root = ListBlock([ListItem("A", null, {src: "a.png", alt: "Probe A"}), ListItem("B")], "image")""");

        Assert.Equal(["1", "2"], numbered.FindAll(".mud-avatar").Select(avatar => avatar.TextContent.Trim()));
        Assert.Equal("a.png", pictured.Find("img").GetAttribute("src"));
        Assert.Empty(pictured.FindAll(".mud-avatar"));
    }

    [Fact]
    public async Task Text_draws_its_subtext_and_a_metric_subtext_is_green_or_red_by_its_sign()
    {
        await using var context = NewContext();
        var cut = Render(context, """root = Stack([Text("text", "Wind", "up", "metric"), Text("text", "Temp", "+4 F", "metric"), BoldText("number", "Rain", "-2 in", "metric"), Text("text", "Plain", "+9", "text")])""");

        var subtexts = cut.FindAll(".mud-typography-caption").Select(element => element.OuterHtml).ToList();
        Assert.All(subtexts, subtext => Assert.Contains("d-block", subtext));
        Assert.Contains("var(--mud-palette-text-secondary)", subtexts[0]);
        Assert.Contains("mud-success-text", subtexts[1]);
        Assert.Contains("mud-error-text", subtexts[2]);
        Assert.Contains("var(--mud-palette-text-secondary)", subtexts[3]);
        var bold = Assert.Single(cut.FindAll("[style*='font-weight:600']"));
        Assert.Equal("Rain", bold.TextContent);
        Assert.Contains("tabular-nums", bold.GetAttribute("style"));
    }

    [Fact]
    public async Task A_metric_draws_its_trend_with_a_sign_and_the_strikethrough_variant_strikes_the_previous_value()
    {
        await using var context = NewContext();
        var inline = Render(context, """root = MetricIndicatorInline("$4.62", "per bushel", {direction: "up", value: 3.2})""");
        var struck = Render(context, """root = MetricIndicatorWithStrikethrough("$4.62", "December", "$4.48", {direction: "down", value: 18})""");

        Assert.Contains("+3.2%", inline.Markup);
        Assert.Contains("mud-success-text", inline.FindAll("p").First(text => text.TextContent == "+3.2%").ClassName);
        Assert.Empty(inline.FindAll("[style*=line-through]"));
        Assert.Contains("-18%", struck.Markup);
        Assert.Equal("$4.48", struck.Find("[style*=line-through]").TextContent);
        Assert.Contains("mud-error-text", struck.FindAll("p").First(text => text.TextContent == "-18%").ClassName);
    }

    [Theory]
    [InlineData("""MetricIndicatorInline("1", "x", {direction: "sideways", value: 5})""")]
    [InlineData("""MetricIndicatorInline("1", "x", {direction: "up", value: "lots"})""")]
    [InlineData("""MetricIndicatorInline("1", "x", {direction: "up"})""")]
    [InlineData("""MetricIndicatorInline("1", "x", {value: 5})""")]
    [InlineData("""MetricIndicatorInline("1", "x", {})""")]
    [InlineData("""MetricIndicatorInline("1", "x", "up")""")]
    [InlineData("""MetricIndicatorInline("1", "x", null)""")]
    [InlineData("""MetricIndicatorInline("1")""")]
    [InlineData("""MetricIndicatorWithStrikethrough("1", "x", "2", {direction: "down", value: [1]})""")]
    [InlineData("""MetricIndicatorWithStrikethrough("1")""")]
    [InlineData("""MetricIndicatorInline(null, null, {direction: "up", value: 1e999})""")]
    public async Task A_metric_with_a_trend_that_is_missing_or_not_a_number_draws_no_trend_and_does_not_throw(string metric)
    {
        await using var context = NewContext();
        var cut = Render(context, "root = " + metric);

        Assert.DoesNotContain("%", cut.Markup);
    }

    [Fact]
    public async Task A_trend_whose_value_is_a_numeric_string_still_reads_as_a_number()
    {
        await using var context = NewContext();
        var cut = Render(context, """root = MetricIndicatorInline("1", null, {direction: "up", value: "12"})""");

        Assert.Contains("+12%", cut.Markup);
    }

    [Theory]
    [InlineData("media.oui")]
    [InlineData("data-display.oui")]
    [InlineData("cards.oui")]
    public async Task The_gallery_programs_draw_with_no_errors_and_no_unknown_component(string file)
    {
        await using var context = NewContext();
        var cut = Render(context, File.ReadAllText(Path.Combine(RepositoryRoot(), "samples", "OpenUI.Lang.Samples.BlazorApp", "Programs", "gallery", file)));

        Assert.Empty(cut.Instance.Session.Current.Errors);
        Assert.DoesNotContain("unknown", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(cut.FindAll(".mud-typography"));
    }

    [Fact]
    public async Task A_gallery_card_block_runs_its_action_against_the_program_s_state()
    {
        await using var context = NewContext();
        var cut = Render(context, File.ReadAllText(Path.Combine(RepositoryRoot(), "samples", "OpenUI.Lang.Samples.BlazorApp", "Programs", "gallery", "cards.oui")));
        Assert.Contains("Nothing picked yet", cut.Markup);

        await cut.InvokeAsync(() => cut.FindAll("[role=button]").First(card => card.TextContent.Contains("Frost watch")).Click());

        cut.WaitForAssertion(() => Assert.Contains("A context card", cut.Markup));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenUI.Lang.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
