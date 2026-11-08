using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using OpenUI.Lang.Blazor.Mud;
using OpenUI.Lang.Blazor.Tests.TestComponents;

namespace OpenUI.Lang.Blazor.Tests.Mud;

/// <summary>Programs are untrusted, and they change under the page: nothing they say may take the renderer down or leave a stale control live.</summary>
public sealed class HostileProgramTests
{
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

    private static void Update(IRenderedComponent<OpenUIProgram> cut, string program, bool streaming = false) =>
        cut.Render(parameters => parameters
            .Add(component => component.Program, program)
            .Add(component => component.IsStreaming, streaming));

    private static void AssertRendererAlive(BunitContext context, IRenderedComponent<OpenUIProgram> cut)
    {
        Assert.False(context.Renderer.UnhandledException.IsCompleted, context.Renderer.UnhandledException.IsCompleted ? context.Renderer.UnhandledException.Result.ToString() : "");
        Assert.DoesNotContain("failed to render", cut.Markup);
    }

    private static string Tab(string value, string trigger) => $"TabItem(\"{value}\", \"{trigger}\", [TextContent(\"{trigger} text\")])";

    private static string Section(string value, string trigger) => $"AccordionItem(\"{value}\", \"{trigger}\", [TextContent(\"{trigger} text\")])";

    private static List<string> Headers(IRenderedComponent<OpenUIProgram> cut) => [.. cut.FindAll(".mud-tab").Select(tab => tab.TextContent.Trim())];

    private static List<string> OpenSections(IRenderedComponent<OpenUIProgram> cut) =>
        [.. cut.FindAll(".mud-expand-panel.mud-panel-expanded").Select(panel => panel.QuerySelector(".mud-expand-panel-header")!.TextContent.Trim())];

    public static TheoryData<string> RepeatedValues() =>
    [
        $"root = Tabs([{Tab("x", "A")}, {Tab("x", "B")}])\n",
        $"root = Tabs([{Tab("", "A")}, {Tab("", "B")}, {Tab("y", "C")}])\n",
        $"root = Accordion([{Section("x", "A")}, {Section("x", "B")}])\n",
        $"root = Accordion([{Section("", "A")}, {Section("", "B")}, {Section("y", "C")}])\n",
    ];

    [Theory]
    [MemberData(nameof(RepeatedValues))]
    public async Task A_repeated_or_empty_value_does_not_reach_the_renderer(string program)
    {
        await using var context = NewContext();

        var cut = Render(context, program);

        AssertRendererAlive(context, cut);
        Assert.Contains("A", cut.Markup);
        Assert.Contains("B", cut.Markup);
    }

    [Theory]
    [MemberData(nameof(RepeatedValues))]
    public async Task A_repeated_or_empty_value_streams_and_moves_without_reaching_the_renderer(string program)
    {
        await using var context = NewContext();
        var cut = Render(context, "", streaming: true);

        for (var length = 1; length <= program.Length; length += 5)
        {
            Update(cut, program[..length], streaming: true);
        }

        Update(cut, program.Replace("\"A\"", "\"Z\"").Replace("\"x\"", "\"q\""));
        AssertRendererAlive(context, cut);
    }

    [Fact]
    public async Task A_section_moving_onto_another_s_value_does_not_reach_the_renderer()
    {
        await using var context = NewContext();
        var cut = Render(context, $"root = Accordion([{Section("a", "A")}, {Section("b", "B")}])\n");

        Update(cut, $"root = Accordion([{Section("b", "A")}, {Section("b", "B")}])\n");

        AssertRendererAlive(context, cut);
        Assert.Equal(2, cut.FindAll(".mud-expand-panel").Count);
    }

    [Fact]
    public async Task A_pick_survives_a_tab_inserted_ahead_of_it()
    {
        await using var context = NewContext();
        var cut = Render(context, $"root = Tabs([{Tab("a", "First")}, {Tab("b", "Second")}])\n");
        cut.FindAll(".mud-tab")[1].Click();
        cut.WaitForAssertion(() => Assert.Contains("Second text", cut.Markup));

        Update(cut, $"root = Tabs([{Tab("n", "New")}, {Tab("a", "First")}, {Tab("b", "Second")}])\n");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["New", "First", "Second"], Headers(cut));
            Assert.Contains("Second text", cut.Markup);
            Assert.DoesNotContain("New text", cut.Markup);
            Assert.Contains("mud-tab-active", cut.FindAll(".mud-tab")[2].ClassList);
        });
    }

    [Fact]
    public async Task Each_of_two_tabs_with_one_value_can_be_picked()
    {
        await using var context = NewContext();
        var cut = Render(context, $"root = Tabs([{Tab("x", "A")}, {Tab("x", "B")}])\n");
        Assert.Contains("A text", cut.Markup);

        cut.FindAll(".mud-tab")[1].Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("B text", cut.Markup);
            Assert.DoesNotContain("A text", cut.Markup);
        });
    }

    [Fact]
    public async Task A_section_with_no_value_is_not_open_until_it_is_picked()
    {
        await using var context = NewContext();
        var cut = Render(context, $"root = Accordion([{Section("", "A")}, {Section("", "B")}])\n");
        Assert.Empty(OpenSections(cut));

        cut.FindAll(".mud-expand-panel-header")[1].Click();

        cut.WaitForAssertion(() => Assert.Equal(["B"], OpenSections(cut)));
    }

    [Fact]
    public async Task A_carousel_on_a_slide_the_program_took_away_shows_the_last_one_left()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = Carousel([[TextContent(\"One\")], [TextContent(\"Two\")], [TextContent(\"Three\")]])\n");
        cut.Find("button[aria-label=Next]").Click();
        cut.Find("button[aria-label=Next]").Click();
        cut.WaitForAssertion(() => Assert.Contains("3 / 3", cut.Markup));

        Update(cut, "root = Carousel([[TextContent(\"One\")]])\n");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("1 / 1", cut.Markup);
            Assert.Contains("One", cut.Markup);
        });
        AssertRendererAlive(context, cut);
    }

    [Fact]
    public async Task A_gallery_viewer_on_a_photo_the_program_took_away_shows_the_last_one_left()
    {
        await using var context = NewContext();
        var photos = string.Join(", ", Enumerable.Range(1, 3).Select(index => $"{{src: \"/{index}.png\", alt: \"Photo {index}\"}}"));
        var cut = Render(context, $"root = ImageGallery([{photos}])\n");
        cut.FindAll("[role=button]")[2].Click();
        cut.WaitForAssertion(() => Assert.Contains("All photos (3 of 3)", cut.Markup));

        Update(cut, "root = ImageGallery([{src: \"/1.png\", alt: \"Photo 1\"}])\n");

        cut.WaitForAssertion(() => Assert.Contains("All photos (1 of 1)", cut.Markup));
        AssertRendererAlive(context, cut);
    }

    [Theory]
    [InlineData("Enter", true)]
    [InlineData(" ", true)]
    [InlineData("a", false)]
    [InlineData("Tab", false)]
    public async Task A_gallery_tile_opens_from_the_keyboard(string key, bool opens)
    {
        await using var context = NewContext();
        var cut = Render(context, "root = ImageGallery([{src: \"/1.png\", alt: \"One\"}, {src: \"/2.png\", alt: \"Two\"}])\n");

        cut.FindAll("[role=button]")[1].KeyDown(new KeyboardEventArgs { Key = key });

        cut.WaitForAssertion(() => Assert.Equal(opens, cut.Markup.Contains("All photos (2 of 2)", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_gallery_thumbnail_moves_the_viewer_from_the_keyboard()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = ImageGallery([{src: \"/1.png\", alt: \"One\"}, {src: \"/2.png\", alt: \"Two\"}])\n");
        cut.FindAll("[role=button]")[0].Click();
        cut.WaitForAssertion(() => Assert.Contains("All photos (1 of 2)", cut.Markup));

        cut.FindAll("[role=dialog] [role=button]")[1].KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => Assert.Contains("All photos (2 of 2)", cut.Markup));
    }

    [Fact]
    public async Task A_text_area_keeps_a_huge_row_count_to_a_sane_box()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = TextArea(\"notes\", null, 100000)\n");

        Assert.Equal("50", cut.Find("textarea").GetAttribute("rows"));
    }

    // MudBlazor draws a dialog in its provider, outside the program's own markup.
    [Fact]
    public async Task A_modal_the_program_no_longer_has_closes_and_its_actions_are_gone()
    {
        await using var context = NewContext();
        var dialogs = context.Render<MudDialogProvider>();
        var cut = Render(context, "$open = true\n$n = 0\nroot = Stack([TextContent(\"Taps \" + $n), Modal(\"Count\", $open, [Button(\"Tap\", @Set($n, $n + 1))])])\n");
        Assert.Contains("Tap", dialogs.Markup);

        Update(cut, "$open = true\n$n = 0\nroot = Stack([TextContent(\"Taps \" + $n)])\n");

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(dialogs.FindAll("button"));
            Assert.Empty(dialogs.FindAll(".mud-overlay"));
        });
        Assert.Equal("0", cut.Instance.Session.State.Get("$n").ToString());
    }

    [Fact]
    public async Task A_modal_comes_back_when_the_program_does()
    {
        await using var context = NewContext();
        var dialogs = context.Render<MudDialogProvider>();
        const string withModal = "$open = true\nroot = Stack([Modal(\"Count\", $open, [TextContent(\"Body\")])])\n";
        var cut = Render(context, withModal);
        Update(cut, "$open = true\nroot = Stack([TextContent(\"None\")])\n");
        cut.WaitForAssertion(() => Assert.DoesNotContain("Body", dialogs.Markup));

        Update(cut, withModal);

        cut.WaitForAssertion(() => Assert.Contains("Body", dialogs.Markup));
    }

    [Fact]
    public async Task A_modal_closes_when_its_host_goes_away()
    {
        await using var context = NewContext();
        var dialogs = context.Render<MudDialogProvider>();
        var host = context.Render<RemovableHost>(parameters => parameters
            .Add(component => component.Program, "$open = true\nroot = Modal(\"Count\", $open, [Button(\"Tap\")])\n"));
        Assert.Contains("Tap", dialogs.Markup);

        host.Render(parameters => parameters.Add(component => component.Show, false));

        dialogs.WaitForAssertion(() =>
        {
            Assert.Empty(dialogs.FindAll("button"));
            Assert.Empty(dialogs.FindAll(".mud-overlay"));
        });
        Assert.False(context.Renderer.UnhandledException.IsCompleted);
    }

    [Theory]
    [InlineData(300, 300)]
    [InlineData(1000, 1000)]
    public async Task A_table_too_big_to_draw_draws_what_fits_and_says_so(int rows, int columns)
    {
        await using var context = NewContext();
        var longest = string.Join(",", Enumerable.Repeat("\"x\"", rows));
        var cut = Render(context, $"d = [{longest}]\ne = []\nroot = Table([Col(\"c\", d),{string.Join(",", Enumerable.Repeat("Col(\"c\", e)", columns - 1))}])\n");

        Assert.Empty(cut.Instance.Session.Current.Errors);
        Assert.Equal(50, cut.FindAll("tbody tr").Count);
        Assert.Equal(50, cut.FindAll("thead th").Count);
        Assert.Contains($"Showing 50 of {rows} rows and 50 of {columns} columns.", cut.Markup);
    }

    [Fact]
    public async Task A_table_that_fits_says_nothing_about_it()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = Table([Col(\"Day\", [\"Mon\", \"Tue\"])])\n");

        Assert.DoesNotContain("Showing", cut.Markup);
    }

    [Theory]
    [InlineData(600, 600)]
    [InlineData(1000, 1000)]
    public async Task A_horizontal_bar_chart_too_big_to_draw_draws_what_fits_and_says_so(int labels, int series)
    {
        await using var context = NewContext();
        var names = string.Join(",", Enumerable.Repeat("\"l\"", labels));
        var cut = Render(context, $"d = [{names}]\ne = []\nroot = HorizontalBarChart(d, [{string.Join(",", Enumerable.Repeat("Series(\"s\", e)", series))}])\n");

        Assert.Empty(cut.Instance.Session.Current.Errors);
        Assert.Contains($"Showing 50 of {labels} labels and 50 of {series} series.", cut.Markup);
        Assert.InRange(cut.Markup.Length, 1, 2_000_000);
    }

    [Fact]
    public async Task A_bound_date_that_is_not_midnight_is_not_rewritten_by_drawing_it()
    {
        await using var context = NewContext();
        const string stored = "2026-03-15T19:30:00Z";
        var cut = Render(context, $"$day = \"{stored}\"\nroot = DatePicker(\"day\", null, null, $day)\n");
        cut.WaitForState(() => cut.FindComponents<MudDatePicker>().Count == 1);

        cut.Render();

        Assert.Equal(stored, cut.Instance.Session.State.Get("$day").ToString());
        Assert.True(cut.Instance.Session.Forms.Get(null, "day").IsNull);
    }

    [Fact]
    public async Task A_picker_echoing_the_day_it_was_given_writes_nothing()
    {
        await using var context = NewContext();
        const string stored = "2026-03-15T19:30:00Z";
        var cut = Render(context, $"$day = \"{stored}\"\nroot = DatePicker(\"day\", null, null, $day)\n");
        var picker = cut.FindComponent<MudDatePicker>();

        await cut.InvokeAsync(() => picker.Instance.DateChanged.InvokeAsync(picker.Instance.Date));

        Assert.Equal(stored, cut.Instance.Session.State.Get("$day").ToString());
    }

    [Fact]
    public async Task A_bound_range_is_not_rewritten_by_drawing_it_or_by_an_echo()
    {
        await using var context = NewContext();
        const string program = "$range = {from: \"2026-03-01T19:30:00Z\", to: \"2026-03-09T19:30:00Z\"}\nroot = DatePicker(\"range\", \"range\", null, $range)\n";
        var cut = Render(context, program);
        var before = cut.Instance.Session.State.Get("$range");
        var picker = cut.FindComponent<MudDateRangePicker>();

        await cut.InvokeAsync(() => picker.Instance.DateRangeChanged.InvokeAsync(picker.Instance.DateRange));

        Assert.Equal(before, cut.Instance.Session.State.Get("$range"));
    }

    // The picker raises its change event when its parameter moves, so this is the echo a guard has to catch.
    [Fact]
    public async Task A_program_moving_a_bound_date_keeps_what_it_wrote()
    {
        await using var context = NewContext();
        const string moved = "2026-04-20T19:30:00Z";
        var cut = Render(context, $"$day = \"2026-03-15T19:30:00Z\"\nroot = Stack([DatePicker(\"day\", null, null, $day), Button(\"Move\", @Set($day, \"{moved}\")), TextContent($day)])\n");

        await cut.ClickWhere("button", "Move");
        cut.WaitForAssertion(() => Assert.Contains(moved, cut.Markup));
        await cut.InvokeAsync(() => { });

        Assert.Equal(moved, cut.Instance.Session.State.Get("$day").ToString());
    }

    [Fact]
    public async Task A_program_moving_a_bound_range_keeps_what_it_wrote()
    {
        await using var context = NewContext();
        var cut = Render(context, "$range = {from: \"2026-03-01T19:30:00Z\", to: \"2026-03-09T19:30:00Z\"}\nroot = Stack([DatePicker(\"range\", \"range\", null, $range), Button(\"Move\", @Set($range, {from: \"2026-04-01T19:30:00Z\", to: \"2026-04-09T19:30:00Z\"})), TextContent($range.from)])\n");

        await cut.ClickWhere("button", "Move");
        cut.WaitForAssertion(() => Assert.Contains("2026-04-01T19:30:00Z", cut.Markup));
        await cut.InvokeAsync(() => { });

        Assert.True(cut.Instance.Session.State.Get("$range").TryGetObject(out var range));
        Assert.Equal("2026-04-01T19:30:00Z", range["from"].ToString());
        Assert.Equal("2026-04-09T19:30:00Z", range["to"].ToString());
    }

    [Fact]
    public async Task A_date_the_user_picks_is_written()
    {
        await using var context = NewContext();
        var cut = Render(context, "$day = \"2026-03-15T19:30:00Z\"\nroot = DatePicker(\"day\", null, null, $day)\n");
        var picker = cut.FindComponent<MudDatePicker>();

        await cut.InvokeAsync(() => picker.Instance.DateChanged.InvokeAsync(new DateTime(2026, 4, 1)));

        Assert.Equal(new DateTime(2026, 4, 1), DateValues.Read(cut.Instance.Session.State.Get("$day")));
    }

    [Fact]
    public async Task An_input_in_a_modal_inside_a_form_lands_in_that_form()
    {
        await using var context = NewContext();
        var dialogs = context.Render<MudDialogProvider>();
        var cut = Render(context, "root = Form(\"f\", Buttons([Button(\"Go\")]), [Modal(\"Details\", true, [Input(\"crop\")])])\n");

        dialogs.Find("input").Input("corn");

        cut.WaitForAssertion(() => Assert.Equal("corn", cut.Instance.Session.Forms.Get("f", "crop").ToString()));
        Assert.True(cut.Instance.Session.Forms.Get(null, "crop").IsNull);
    }

    [Fact]
    public async Task A_form_inside_a_modal_outside_any_form_stays_its_own()
    {
        await using var context = NewContext();
        var dialogs = context.Render<MudDialogProvider>();
        var cut = Render(context, "root = Modal(\"Details\", true, [Input(\"page\"), Form(\"f\", Buttons([Button(\"Go\")]), [FormControl(\"Crop\", Input(\"crop\"))])])\n");

        dialogs.FindAll("input")[0].Input("top");
        dialogs.FindAll("input")[1].Input("corn");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("top", cut.Instance.Session.Forms.Get(null, "page").ToString());
            Assert.Equal("corn", cut.Instance.Session.Forms.Get("f", "crop").ToString());
        });
    }
}
