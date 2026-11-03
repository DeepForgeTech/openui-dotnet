using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using OpenUI.Lang.Blazor.Mud;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor.Tests.Mud;

/// <summary>The MudBlazor views of the inputs, the editable table and the charts beyond the first two.</summary>
public sealed class MudFormsAndChartsTests
{
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
            .Add(component => component.Program, program));

    // One field in a form whose Send button tells the assistant.
    private static string Sending(string field, string name = "f") =>
        $"root = Form(\"{name}\", Buttons([Button(\"Send\", @ToAssistant(\"sent\"))]), [FormControl(\"Field\", {field})])\n";

    private static OuiValue Field(IRenderedComponent<OpenUIProgram> cut, string field, string form = "f") => cut.Instance.Session.Forms.Get(form, field);

    // The value of a field in the form state an event carries.
    private static OuiValue Sent(OpenUIHostEvent e, string form, string field, string part = "value") =>
        e.FormState[form].TryGetObject(out var fields) && fields[field].TryGetObject(out var entry) ? entry[part] : OuiValue.Null;

    private static string[] Strings(OuiValue value) =>
        value.TryGetArray(out var items) ? [.. items.Select(item => item.ToString())] : [];

    private static void AssertDrawn(IRenderedComponent<OpenUIProgram> cut)
    {
        Assert.DoesNotContain(cut.Instance.Session.Current.Errors, error => error.Code == OpenUIErrorCode.RenderError);
        Assert.DoesNotContain("failed to render", cut.Markup);
    }

    // The gallery programs are not embedded in the sample yet, so these read the files from the source tree.
    [Theory]
    [InlineData("forms.oui")]
    [InlineData("tables.oui")]
    [InlineData("charts.oui")]
    public async Task A_gallery_program_parses_clean_and_draws(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!root.GetFiles("OpenUI.Lang.sln").Any())
        {
            root = root.Parent ?? throw new InvalidOperationException("The repository root is not above the test binaries.");
        }

        await using var context = NewContext();
        var cut = Render(context, File.ReadAllText(Path.Combine(root.FullName, "samples", "OpenUI.Lang.Samples.BlazorApp", "Programs", "gallery", file)));

        Assert.Empty(cut.Instance.Session.Current.Errors);
        Assert.DoesNotContain("oui-diagnostic", cut.Markup);
    }

    [Fact]
    public async Task A_label_shows_its_text()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = Label(\"Field notes\")\n");

        Assert.Contains("Field notes", cut.Markup);
    }

    [Fact]
    public async Task A_text_area_has_its_rows_writes_what_is_typed_and_says_when_a_rule_fails()
    {
        await using var context = NewContext();
        var events = new List<OpenUIHostEvent>();
        var cut = Render(context, Sending("TextArea(\"notes\", \"Notes\", 5, {required: true})"), events);
        Assert.Equal("5", cut.Find("textarea").GetAttribute("rows"));

        await cut.ClickWhere("button", "Send");

        cut.WaitForAssertion(() => Assert.Contains("is required", cut.Markup));
        Assert.Empty(events);

        cut.Find("textarea").Input("Wet corner");
        cut.WaitForAssertion(() => Assert.DoesNotContain("is required", cut.Markup));
        await cut.ClickWhere("button", "Send");

        cut.WaitForAssertion(() => Assert.Single(events));
        Assert.Equal("Wet corner", Field(cut, "notes").ToString());
    }

    [Fact]
    public async Task A_text_area_bound_to_a_variable_writes_it()
    {
        await using var context = NewContext();
        var cut = Render(context, "$note = \"a\"\nroot = Stack([TextArea(\"note\", null, null, null, $note), TextContent(\"Note \" + $note)])\n");

        cut.Find("textarea").Input("b");

        cut.WaitForAssertion(() => Assert.Contains("Note b", cut.Markup));
    }

    [Fact]
    public async Task A_radio_group_shows_its_default_holds_it_in_the_form_and_writes_the_one_picked()
    {
        await using var context = NewContext();
        var cut = Render(context, Sending("RadioGroup(\"crop\", [RadioItem(\"Corn\", \"Row crop\", \"corn\"), RadioItem(\"Wheat\", \"\", \"wheat\")], \"wheat\")"));

        Assert.Contains("Row crop", cut.Markup);
        cut.WaitForAssertion(() => Assert.Equal("wheat", Field(cut, "crop").ToString()));

        await cut.InvokeAsync(() => cut.FindComponent<MudRadioGroup<string>>().Instance.ValueChanged.InvokeAsync("corn"));

        cut.WaitForAssertion(() => Assert.Equal("corn", Field(cut, "crop").ToString()));
    }

    [Fact]
    public async Task A_radio_group_that_is_required_says_so_on_a_failed_submit()
    {
        await using var context = NewContext();
        var cut = Render(context, Sending("RadioGroup(\"crop\", [RadioItem(\"Corn\", \"\", \"corn\")], null, {required: true})"));

        await cut.ClickWhere("button", "Send");

        cut.WaitForAssertion(() => Assert.Contains("is required", cut.Markup));
    }

    [Fact]
    public async Task A_check_box_group_writes_a_flag_for_every_item_and_seeds_the_ones_that_default_on()
    {
        await using var context = NewContext();
        var cut = Render(context, Sending("CheckBoxGroup(\"checks\", [CheckBoxItem(\"Wind\", \"Under 10 mph\", \"wind\", true), CheckBoxItem(\"Rain\", \"\", \"rain\")])"));

        Assert.Contains("Under 10 mph", cut.Markup);
        cut.WaitForAssertion(() =>
        {
            Assert.True(Field(cut, "checks").TryGetObject(out var seeded));
            Assert.True(seeded["wind"].TryGetBoolean(out var wind) && wind);
        });

        await cut.InvokeAsync(() => cut.FindComponents<MudCheckBox<bool>>()[1].Instance.ValueChanged.InvokeAsync(true));

        cut.WaitForAssertion(() =>
        {
            Assert.True(Field(cut, "checks").TryGetObject(out var flags));
            Assert.Equal(2, flags.Count);
            Assert.True(flags["wind"].TryGetBoolean(out var wind) && wind);
            Assert.True(flags["rain"].TryGetBoolean(out var rain) && rain);
        });
    }

    [Fact]
    public async Task A_check_box_group_with_nothing_ticked_fails_its_required_rule()
    {
        await using var context = NewContext();
        var cut = Render(context, Sending("CheckBoxGroup(\"checks\", [CheckBoxItem(\"Wind\", \"\", \"wind\")], {required: true})"));

        await cut.ClickWhere("button", "Send");

        cut.WaitForAssertion(() => Assert.Contains("is required", cut.Markup));
    }

    [Fact]
    public async Task Chips_hold_a_list_by_default_in_the_order_picked_and_one_value_when_single()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = Form(\"f\", Buttons([Button(\"Send\")]), [Chips(\"sizes\", null, [ChipItem(\"s\", \"Small\"), ChipItem(\"m\", \"Medium\"), ChipItem(\"l\", \"Large\")]), Chips(\"one\", \"single\", [ChipItem(\"a\", \"Alpha\"), ChipItem(\"b\", \"Beta\")])])\n");

        await cut.ClickWhere(".mud-chip", "Large");
        await cut.ClickWhere(".mud-chip", "Small");
        await cut.ClickWhere(".mud-chip", "Alpha");
        await cut.ClickWhere(".mud-chip", "Beta");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["l", "s"], Strings(Field(cut, "sizes")));
            Assert.Equal("b", Field(cut, "one").ToString());
        });

        await cut.ClickWhere(".mud-chip", "Large");

        cut.WaitForAssertion(() => Assert.Equal(["s"], Strings(Field(cut, "sizes"))));
    }

    [Fact]
    public async Task Chips_seed_their_default_in_the_shape_the_type_stores_and_a_disabled_chip_cannot_be_picked()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = Form(\"f\", Buttons([Button(\"Send\")]), [Chips(\"many\", \"multiple\", [ChipItem(\"a\", \"Alpha\"), ChipItem(\"b\", \"Beta\", null, true)], null, \"a\"), Chips(\"one\", \"single\", [ChipItem(\"a\", \"Alpha\")], null, [\"a\"])])\n");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["a"], Strings(Field(cut, "many")));
            Assert.Equal("a", Field(cut, "one").ToString());
        });
        Assert.Contains(cut.FindAll(".mud-chip"), chip => chip.TextContent.Contains("Beta") && chip.ClassList.Contains("mud-disabled"));
    }

    [Fact]
    public async Task A_required_chips_field_says_so_on_a_failed_submit()
    {
        await using var context = NewContext();
        var cut = Render(context, Sending("Chips(\"sizes\", \"multiple\", [ChipItem(\"s\", \"Small\")], {required: true})"));

        await cut.ClickWhere("button", "Send");

        cut.WaitForAssertion(() => Assert.Contains("is required", cut.Markup));
    }

    [Fact]
    public async Task Option_cards_pick_one_unless_the_type_says_otherwise_and_show_a_subtitle()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = Form(\"f\", Buttons([Button(\"Send\")]), [OptionCards(\"plan\", null, [OptionCard(\"basic\", \"Basic\", \"Free\"), OptionCard(\"pro\", \"Pro\")]), OptionCards(\"extras\", \"multiple\", [OptionCard(\"a\", \"Alpha\"), OptionCard(\"b\", \"Beta\")])])\n");
        Assert.Contains("Free", cut.Markup);

        await cut.ClickWhere("button", "Basic");
        await cut.ClickWhere("button", "Pro");
        await cut.ClickWhere("button", "Beta");
        await cut.ClickWhere("button", "Alpha");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("pro", Field(cut, "plan").ToString());
            Assert.Equal(["b", "a"], Strings(Field(cut, "extras")));
        });
        Assert.Equal("true", cut.FindAll("button").First(button => button.TextContent.Contains("Pro")).GetAttribute("aria-pressed"));
    }

    [Fact]
    public async Task Option_cards_that_are_required_say_so_on_a_failed_submit()
    {
        await using var context = NewContext();
        var cut = Render(context, Sending("OptionCards(\"plan\", \"single\", [OptionCard(\"basic\", \"Basic\")], {required: true})"));

        await cut.ClickWhere("button", "Send");

        cut.WaitForAssertion(() => Assert.Contains("is required", cut.Markup));
    }

    [Fact]
    public async Task A_date_picker_writes_the_day_as_an_ISO_string_for_local_midnight()
    {
        await using var context = NewContext();
        var cut = Render(context, Sending("DatePicker(\"day\")"));

        await cut.InvokeAsync(() => cut.FindComponent<MudDatePicker>().Instance.DateChanged.InvokeAsync(new DateTime(2026, 3, 15)));

        cut.WaitForAssertion(() =>
        {
            Assert.True(Field(cut, "day").TryGetString(out var stored));
            Assert.EndsWith("Z", stored);
            Assert.Equal(new DateTime(2026, 3, 15), DateTime.Parse(stored, null, System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime());
        });
        Assert.Equal(new DateTime(2026, 3, 15), cut.FindComponent<MudDatePicker>().Instance.Date);
    }

    [Fact]
    public async Task A_range_date_picker_writes_from_and_to()
    {
        await using var context = NewContext();
        var cut = Render(context, Sending("DatePicker(\"window\", \"range\")"));

        await cut.InvokeAsync(() => cut.FindComponent<MudDateRangePicker>().Instance.DateRangeChanged.InvokeAsync(new DateRange(new DateTime(2026, 3, 15), new DateTime(2026, 3, 18))));

        cut.WaitForAssertion(() =>
        {
            Assert.True(Field(cut, "window").TryGetObject(out var range));
            Assert.Equal(["from", "to"], range.Select(entry => entry.Key));
            Assert.Equal(new DateTime(2026, 3, 18), DateTime.Parse(range["to"].ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime());
        });
        var shown = cut.FindComponent<MudDateRangePicker>().Instance.DateRange;
        Assert.Equal(new DateTime(2026, 3, 15), shown?.Start);
    }

    [Fact]
    public async Task A_date_picker_with_no_date_fails_its_required_rule_on_submit()
    {
        await using var context = NewContext();
        var cut = Render(context, Sending("DatePicker(\"day\", \"single\", {required: true})"));

        await cut.ClickWhere("button", "Send");

        cut.WaitForAssertion(() => Assert.Contains("is required", cut.Markup));
    }

    [Fact]
    public async Task A_form_action_sends_what_every_input_holds()
    {
        await using var context = NewContext();
        var events = new List<OpenUIHostEvent>();
        var cut = Render(context, "root = Form(\"order\", Buttons([Button(\"Send\", @ToAssistant(\"sent\"))]), [TextArea(\"notes\"), RadioGroup(\"crop\", [RadioItem(\"Corn\", \"\", \"corn\")], \"corn\"), Chips(\"sizes\", \"multiple\", [ChipItem(\"s\", \"Small\")], null, [\"s\"]), CheckBoxGroup(\"checks\", [CheckBoxItem(\"Wind\", \"\", \"wind\", true)]), OptionCards(\"plan\", \"single\", [OptionCard(\"pro\", \"Pro\")], null, \"pro\")])\n", events);
        cut.Find("textarea").Input("Wet corner");

        await cut.ClickWhere("button", "Send");

        cut.WaitForAssertion(() => Assert.Single(events));
        var sent = events[0];
        Assert.Equal("order", sent.FormName);
        Assert.Equal("Wet corner", Sent(sent, "order", "notes").ToString());
        Assert.Equal("corn", Sent(sent, "order", "crop").ToString());
        Assert.Equal(["s"], Strings(Sent(sent, "order", "sizes")));
        Assert.True(Sent(sent, "order", "checks").TryGetObject(out var checks) && checks["wind"].TryGetBoolean(out var wind) && wind);
        Assert.Equal("pro", Sent(sent, "order", "plan").ToString());
    }

    private const string Crops =
        "root = EditableTable(\"crops\", [{type: \"text\", key: \"name\", header: \"Crop\"}, {type: \"number\", key: \"acres\", header: \"Acres\"}, {type: \"date-single\", key: \"planted\", header: \"Planted\"}, {type: \"select\", key: \"stage\", header: \"Stage\", options: [{value: \"veg\", label: \"Vegetative\"}, {value: \"tassel\", label: \"Tasseling\"}]}, {type: \"url\", key: \"link\", header: \"Link\"}], [{id: \"r1\", values: [\"Corn\", 120, \"2026-04-20\", \"veg\", \"https://example.com/corn\"]}, {id: \"r2\", values: [\"Wheat\", 80, \"2026-03-15\", \"veg\", \"https://example.com/wheat\"]}])\n";

    [Fact]
    public async Task An_editable_table_shows_each_row_with_a_cell_for_every_column_type()
    {
        await using var context = NewContext();
        var cut = Render(context, Crops);

        Assert.Equal(["Crop", "Acres", "Planted", "Stage", "Link"], cut.FindAll("th").Select(header => header.TextContent));
        Assert.Equal(2, cut.FindAll("tbody tr").Count);
        foreach (var shown in new[] { "Corn", "Wheat", "https://example.com/corn", "https://example.com/wheat" })
        {
            Assert.Contains($"value=\"{shown}\"", cut.Markup);
        }

        Assert.Equal("120", cut.FindComponents<MudNumericField<double>>()[0].Find("input").GetAttribute("value"));
        Assert.Equal("Apr 20, 2026", cut.FindComponents<MudDatePicker>()[0].Find("input").GetAttribute("value"));
        Assert.Equal("Vegetative", cut.FindComponents<MudSelect<string>>()[0].Find("input").GetAttribute("value"));
        Assert.DoesNotContain("changes made", cut.Markup);
    }

    [Fact]
    public async Task Editing_a_table_counts_the_changes_and_saving_sends_every_row_in_the_shape_it_came_in()
    {
        await using var context = NewContext();
        var events = new List<OpenUIHostEvent>();
        var cut = Render(context, Crops, events);

        await cut.InvokeAsync(() => cut.FindComponents<MudTextField<string>>()[0].Instance.ValueChanged.InvokeAsync("Maize"));
        await cut.InvokeAsync(() => cut.FindComponents<MudNumericField<double>>()[0].Instance.ValueChanged.InvokeAsync(150));
        await cut.InvokeAsync(() => cut.FindComponents<MudDatePicker>()[0].Instance.DateChanged.InvokeAsync(new DateTime(2026, 5, 1)));
        await cut.InvokeAsync(() => cut.FindComponents<MudSelect<string>>()[0].Instance.ValueChanged.InvokeAsync("tassel"));
        await cut.InvokeAsync(() => cut.FindComponents<MudTextField<string>>().Where(field => field.Find("input").GetAttribute("type") == "url").ElementAt(1).Instance.ValueChanged.InvokeAsync("https://example.com/durum"));

        cut.WaitForAssertion(() => Assert.Contains("5 changes made", cut.Markup));
        Assert.Empty(events);

        await cut.ClickWhere("button", "Save Changes");

        cut.WaitForAssertion(() => Assert.Single(events));
        var saved = Field(cut, "crops", "crops");
        Assert.True(saved.TryGetArray(out var rows));
        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].TryGetObject(out var first));
        Assert.Equal("r1", first["id"].ToString());
        Assert.True(first["values"].TryGetArray(out var values));
        Assert.Equal("Maize", values[0].ToString());
        Assert.True(values[1].TryGetNumber(out var acres) && acres == 150);
        Assert.Equal(new DateTime(2026, 5, 1), DateTime.Parse(values[2].ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime());
        Assert.Equal("tassel", values[3].ToString());
        Assert.True(rows[1].TryGetObject(out var second));
        Assert.True(second["values"].TryGetArray(out var untouched));
        Assert.Equal("2026-03-15", untouched[2].ToString());
        Assert.Equal("https://example.com/durum", untouched[4].ToString());
        Assert.Equal(("continue_conversation", "Save Changes", "crops"), (events[0].Type, events[0].HumanFriendlyMessage, events[0].FormName));
        Assert.Equal("editabletable", Sent(events[0], "crops", "crops", "componentType").ToString());
        cut.WaitForAssertion(() => Assert.DoesNotContain("changes made", cut.Markup));
    }

    [Fact]
    public async Task Resetting_a_table_puts_the_original_cells_back()
    {
        await using var context = NewContext();
        var cut = Render(context, Crops);

        await cut.InvokeAsync(() => cut.FindComponents<MudNumericField<double>>()[0].Instance.ValueChanged.InvokeAsync(150));
        cut.WaitForAssertion(() => Assert.Contains("1 changes made", cut.Markup));
        await cut.ClickWhere("button", "Reset");

        cut.WaitForAssertion(() => Assert.DoesNotContain("changes made", cut.Markup));
        Assert.Equal("120", cut.FindComponents<MudNumericField<double>>()[0].Find("input").GetAttribute("value"));
        Assert.True(Field(cut, "crops", "crops").IsNull);
    }

    [Fact]
    public async Task A_table_keeps_its_edits_while_the_program_around_it_changes()
    {
        await using var context = NewContext();
        var cut = Render(context, "$n = 0\nroot = Stack([Button(\"Add\", @Set($n, $n + 1)), TextContent(\"Count \" + $n)," + Crops.Replace("root = ", "").TrimEnd() + "])\n");

        await cut.InvokeAsync(() => cut.FindComponents<MudNumericField<double>>()[0].Instance.ValueChanged.InvokeAsync(150));
        await cut.ClickWhere("button", "Add");

        cut.WaitForAssertion(() => Assert.Contains("Count 1", cut.Markup));
        Assert.Equal("150", cut.FindComponents<MudNumericField<double>>()[0].Find("input").GetAttribute("value"));
    }

    [Theory]
    [InlineData("root = EditableTable(\"t\", [], [])")]
    [InlineData("root = EditableTable(\"t\", [{type: \"number\", key: \"a\", header: \"A\"}], [{id: \"r\", values: [\"not a number\"]}, {id: \"s\", values: []}])")]
    [InlineData("root = EditableTable(\"t\", [{type: \"select\", key: \"a\", header: \"A\"}, {type: \"date-single\", key: \"b\", header: \"B\"}], [{id: \"r\", values: [\"x\", \"not a date\"]}])")]
    [InlineData("root = EditableTable()")]
    public async Task A_table_with_odd_data_still_draws(string program)
    {
        await using var context = NewContext();
        var cut = Render(context, program + "\n");

        AssertDrawn(cut);
    }

    [Fact]
    public async Task A_horizontal_bar_chart_scales_each_bar_to_the_largest_value()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = HorizontalBarChart([\"Corn\", \"Wheat\"], [Series(\"Acres\", [120, 60]), Series(\"Plots\", [30, 90])], \"grouped\", \"Count\", \"Crop\")\n");

        foreach (var text in new[] { "Corn", "Wheat", "Acres", "Plots", "Count", "Crop", "120", "90" })
        {
            Assert.Contains(text, cut.Markup);
        }

        foreach (var width in new[] { "100%", "50%", "25%", "75%" })
        {
            Assert.Contains($"width: {width}", cut.Markup);
        }
    }

    [Fact]
    public async Task A_stacked_horizontal_bar_chart_puts_a_row_s_series_end_to_end()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = HorizontalBarChart([\"Corn\"], [Series(\"Acres\", [75]), Series(\"Plots\", [25])], \"stacked\")\n");

        Assert.Contains("width: 75%", cut.Markup);
        Assert.Contains("width: 25%", cut.Markup);
        Assert.Equal(2, cut.FindAll("[title]").Count);
    }

    [Fact]
    public async Task A_single_stacked_bar_splits_the_bar_by_value_and_names_each_part()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = SingleStackedBarChart([\"Sprayed\", \"Waiting\"], [1, 3])\n");

        Assert.Contains("width: 25%", cut.Markup);
        Assert.Contains("width: 75%", cut.Markup);
        Assert.Contains("Sprayed: 1", cut.Markup);
        Assert.Contains("Waiting: 3", cut.Markup);
    }

    [Fact]
    public async Task A_radial_chart_draws_a_track_and_a_bar_for_each_value()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = RadialChart([\"Corn\", \"Wheat\"], [80, 40])\n");

        Assert.Equal(4, cut.FindAll("svg circle").Count);
        Assert.Equal(["Corn: 80", "Wheat: 40"], cut.FindAll("svg title").Select(title => title.TextContent));
        var lengths = cut.FindAll("svg circle").Where((_, index) => index % 2 == 1).Select(circle => circle.GetAttribute("stroke-dasharray")!.Split(' ')).ToList();
        var outer = double.Parse(lengths[0][0]) / double.Parse(lengths[0][1]);
        var inner = double.Parse(lengths[1][0]) / double.Parse(lengths[1][1]);
        Assert.Equal(0.75, outer, 2);
        Assert.Equal(0.375, inner, 2);
    }

    [Theory]
    [InlineData("PieChart", "[], []")]
    [InlineData("PieChart", "[\"A\", \"B\"], [0, 0]")]
    [InlineData("PieChart", "[\"A\", \"B\"], [-4, 2]")]
    [InlineData("PieChart", "[\"A\", \"B\", \"C\"], [1]")]
    [InlineData("PieChart", "[\"A\"], [1, 2, 3]")]
    [InlineData("PieChart", "[\"A\", \"B\"], [1e308, 2e308]")]
    [InlineData("PieChart", "[\"A\"], [\"lots\"]")]
    [InlineData("RadialChart", "[], []")]
    [InlineData("RadialChart", "[\"A\", \"B\"], [0, 0]")]
    [InlineData("RadialChart", "[\"A\", \"B\", \"C\"], [1]")]
    [InlineData("RadialChart", "[\"A\"], [1e308, 2e308]")]
    [InlineData("SingleStackedBarChart", "[], []")]
    [InlineData("SingleStackedBarChart", "[\"A\", \"B\"], [0, 0]")]
    [InlineData("SingleStackedBarChart", "[\"A\", \"B\", \"C\"], [1]")]
    [InlineData("SingleStackedBarChart", "[\"A\"], [1e308, 2e308]")]
    [InlineData("RadarChart", "[], []")]
    [InlineData("RadarChart", "[\"A\"], [Series(\"S\", [1])]")]
    [InlineData("RadarChart", "[\"A\", \"B\", \"C\"], [Series(\"Short\", [1]), Series(\"Long\", [1, 2, 3, 4, 5])]")]
    [InlineData("RadarChart", "[\"A\", \"B\", \"C\"], [Series(\"Huge\", [1e308, -1e308, 2e308])]")]
    [InlineData("RadarChart", "[\"A\", \"B\", \"C\"], [Series(\"Flat\", [0, 0, 0])]")]
    [InlineData("HorizontalBarChart", "[], []")]
    [InlineData("HorizontalBarChart", "[\"A\"], []")]
    [InlineData("HorizontalBarChart", "[\"A\", \"B\", \"C\"], [Series(\"Short\", [1]), Series(\"Long\", [1, 2, 3, 4, 5])], \"stacked\"")]
    [InlineData("HorizontalBarChart", "[\"A\"], [Series(\"Huge\", [1e308, 2e308, -1e308])], \"stacked\"")]
    [InlineData("HorizontalBarChart", "[\"A\", \"B\"], [Series(\"Neg\", [-5, -2])]")]
    [InlineData("AreaChart", "[], []")]
    [InlineData("AreaChart", "[\"A\"], [Series(\"Huge\", [1e308, -1e308, 2e308])], \"step\"")]
    [InlineData("ScatterChart", "[]")]
    [InlineData("ScatterChart", "[ScatterSeries(\"Empty\", [])]")]
    [InlineData("ScatterChart", "[ScatterSeries(\"One\", [Point(1, 1)])]")]
    [InlineData("ScatterChart", "[ScatterSeries(\"Flat\", [Point(2, 2), Point(2, 2)])]")]
    [InlineData("ScatterChart", "[ScatterSeries(\"Huge\", [Point(1e308, -1e308), Point(2e308, 1), Point(1, 2e308)])]")]
    [InlineData("ScatterChart", "[ScatterSeries(\"Words\", [Point(\"a\", \"b\")])]")]
    public async Task A_chart_with_odd_data_still_draws_something(string component, string arguments)
    {
        await using var context = NewContext();
        var cut = Render(context, $"root = {component}({arguments})\n");

        AssertDrawn(cut);
    }
}
