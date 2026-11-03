using System.Text.Json;
using Bunit;
using OpenUI.Lang.Blazor.Tests.TestComponents;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;
using OpenUI.Lang.Values;

using OpenUI.Lang.Blazor.Mud;

namespace OpenUI.Lang.Blazor.Tests.Mud;

/// <summary>What the package's choice fields get from <see cref="MudView"/>, with a view of plain markup.</summary>
public sealed class ChoiceHelperTests
{
    private static readonly LibraryDefinition Library = TestLibrary.MudWith("openui-choices", views => views
        .Add<AnyView>("Stack")
        .Add<AnyView>("Form")
        .Add<AnyView>("Buttons")
        .Add<AnyView>("Button")
        .Add<AnyView>("TextContent")
        .Add<ChoiceView>("Select")
        .Add<ChoiceView>("Chips")
        .Add<ChoiceView>("OptionCards")
        .Add<ChoiceView>("SwitchGroup")
        .Add<ChoiceView>("CheckBoxGroup")
        .Add<FirstNumberView>("Slider"));

    private static IRenderedComponent<OpenUIProgram> Render(BunitContext context, string program) =>
        context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, program));

    private static Task Click(IRenderedComponent<OpenUIProgram> cut, string selector) =>
        cut.InvokeAsync(() => cut.Find(selector).Click());

    [Fact]
    public async Task A_select_gives_its_options_labels_and_writes_the_value_of_the_one_picked()
    {
        await using var context = TestLibrary.NewContext(Library);
        var cut = Render(context, "$day = \"\"\nroot = Select(\"day\", [SelectItem(\"mon\", \"Monday\"), SelectItem(\"tue\", \"Tuesday\")], null, null, $day)\n");

        await Click(cut, ".choose[data-key=tue]");

        cut.WaitForAssertion(() => Assert.Equal("Tuesday", cut.Find(".chosen").GetAttribute("data-label")));
        Assert.Equal("tue", cut.Instance.Session.State.Get("$day").ToString());
    }

    // An item of these definitions holds a string, so this takes a definition of its own: the helpers hand back what the program wrote.
    [Fact]
    public async Task A_number_stays_a_number_when_it_is_picked()
    {
        var numbers = new LibraryDefinition("numbers", "NumberPick",
        [
            new("All",
            [
                new("NumberPick", "Picks a number.", typeof(ChoiceView),
                [
                    new PropDefinition("items", new ArrayShape(TypeShape.Any)) { Required = true },
                    new PropDefinition("value", TypeShape.Any) { Access = PropAccess.ReadWrite },
                ]),
                new("NumberItem", "One number.", typeof(AnyView),
                [
                    new PropDefinition("value", TypeShape.Number) { Required = true },
                    new PropDefinition("label", TypeShape.String) { Required = true },
                ]),
            ], []),
        ]);
        await using var context = TestLibrary.NewContext(numbers);
        var cut = context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, "$pick = null\nroot = NumberPick([NumberItem(3, \"Three\")], $pick)\n"));

        await Click(cut, ".choose[data-key=\"3\"]");

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Instance.Session.State.Get("$pick").TryGetNumber(out var number));
            Assert.Equal(3, number);
        });
        Assert.Equal("Three", cut.Find(".chosen").GetAttribute("data-label"));
    }

    [Fact]
    public async Task A_number_in_a_standard_item_is_an_error_the_program_is_told_about()
    {
        await using var context = TestLibrary.NewContext(Library);
        var cut = Render(context, "$rows = [{id: 3, name: \"Three\"}]\n$pick = null\nroot = Select(\"pick\", @Each($rows, \"r\", SelectItem(r.id, r.name)), null, null, $pick)\n");

        Assert.Empty(cut.FindAll(".choose"));
        Assert.Contains(cut.Instance.Session.Current.Errors, error => error.Code == OpenUIErrorCode.TypeMismatch);
    }

    [Fact]
    public async Task A_key_no_option_has_shows_as_itself()
    {
        await using var context = TestLibrary.NewContext(Library);
        var cut = Render(context, "$day = \"gone\"\nroot = Select(\"day\", [SelectItem(\"mon\", \"Monday\")], null, null, $day)\n");

        Assert.Equal("gone", cut.Find(".chosen").GetAttribute("data-label"));
    }

    [Fact]
    public async Task A_single_chip_field_holds_one_pick_and_unpicks_it()
    {
        await using var context = TestLibrary.NewContext(Library);
        var cut = Render(context, "root = Form(\"f\", Buttons([Button(\"Go\")]), [Chips(\"size\", \"single\", [ChipItem(\"s\", \"Small\"), ChipItem(\"l\", \"Large\")])])\n");

        await Click(cut, ".toggle[data-key=s]");
        await Click(cut, ".toggle[data-key=l]");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("False", cut.Find(".toggle[data-key=s]").GetAttribute("data-chosen"));
            Assert.Equal("True", cut.Find(".toggle[data-key=l]").GetAttribute("data-chosen"));
        });

        await Click(cut, ".toggle[data-key=l]");

        cut.WaitForAssertion(() => Assert.Equal("False", cut.Find(".toggle[data-key=l]").GetAttribute("data-chosen")));
    }

    [Fact]
    public async Task A_multiple_chip_field_holds_a_list_in_the_order_picked()
    {
        await using var context = TestLibrary.NewContext(Library);
        var cut = Render(context, "root = Form(\"f\", Buttons([Button(\"Go\")]), [Chips(\"size\", \"multiple\", [ChipItem(\"s\", \"Small\"), ChipItem(\"m\", \"Medium\"), ChipItem(\"l\", \"Large\")])])\n");

        await Click(cut, ".toggle[data-key=l]");
        await Click(cut, ".toggle[data-key=s]");
        await Click(cut, ".toggle[data-key=m]");
        await Click(cut, ".toggle[data-key=s]");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("True", cut.Find(".toggle[data-key=l]").GetAttribute("data-chosen"));
            Assert.Equal("False", cut.Find(".toggle[data-key=s]").GetAttribute("data-chosen"));
            Assert.Equal("True", cut.Find(".toggle[data-key=m]").GetAttribute("data-chosen"));
        });
        Assert.True(cut.Instance.Session.Forms.Get("f", "size").TryGetArray(out var picks));
        Assert.Equal(["l", "m"], picks.Select(pick => pick.ToString()));
    }

    [Fact]
    public async Task An_option_card_is_labelled_by_its_title()
    {
        await using var context = TestLibrary.NewContext(Library);
        var cut = Render(context, "root = OptionCards(\"style\", \"single\", [OptionCard(\"deep\", \"Deep Focus\")])\n");

        Assert.Equal("Deep Focus", cut.Find(".choose").TextContent);
    }

    [Fact]
    public async Task A_flag_field_writes_the_map_with_every_item_in_it()
    {
        await using var context = TestLibrary.NewContext(Library);
        var cut = Render(context, "$flags = {}\nroot = SwitchGroup(\"flags\", [SwitchItem(\"One\", \"\", \"one\", true), SwitchItem(\"Two\", \"\", \"two\")], null, $flags)\n");
        Assert.Equal("True", cut.Find(".flag[data-name=one]").GetAttribute("data-on"));

        await Click(cut, ".flag[data-name=two]");

        cut.WaitForAssertion(() => Assert.Equal("True", cut.Find(".flag[data-name=two]").GetAttribute("data-on")));
        Assert.True(cut.Instance.Session.State.Get("$flags").TryGetObject(out var flags));
        Assert.True(flags["one"].TryGetBoolean(out var one) && one);
        Assert.True(flags["two"].TryGetBoolean(out var two) && two);
    }

    [Fact]
    public async Task A_default_is_shown_and_goes_into_the_form_field()
    {
        await using var context = TestLibrary.NewContext(Library);
        var cut = Render(context, "root = Form(\"f\", Buttons([Button(\"Go\")]), [Slider(\"wind\", \"continuous\", 0, 25, 1, [7])])\n");

        Assert.Equal("7", cut.Find(".first").TextContent);
        cut.WaitForAssertion(() => Assert.True(cut.Instance.Session.Forms.Get("f", "wind").TryGetArray(out var seeded) && seeded.Count == 1));
    }

    [Fact]
    public async Task An_option_card_field_with_no_type_holds_one_pick_and_a_chip_field_a_list()
    {
        await using var context = TestLibrary.NewContext(Library);
        var cut = Render(context, "root = Form(\"f\", Buttons([Button(\"Go\")]), [OptionCards(\"cards\", null, [OptionCard(\"a\", \"A\"), OptionCard(\"b\", \"B\")]), Chips(\"chips\", null, [ChipItem(\"c\", \"C\"), ChipItem(\"d\", \"D\")])])\n");

        await Click(cut, ".toggle[data-key=a]");
        await Click(cut, ".toggle[data-key=b]");
        await Click(cut, ".toggle[data-key=c]");
        await Click(cut, ".toggle[data-key=d]");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("b", cut.Instance.Session.Forms.Get("f", "cards").ToString());
            Assert.True(cut.Instance.Session.Forms.Get("f", "chips").TryGetArray(out var chips) && chips.Count == 2);
        });
    }

    [Fact]
    public async Task A_kit_writes_no_seeding_and_every_field_with_a_default_seeds()
    {
        await using var context = TestLibrary.NewContext(Library);
        var cut = Render(context, "root = Form(\"f\", Buttons([Button(\"Go\")]), [Chips(\"chips\", \"multiple\", [ChipItem(\"c\", \"C\")], null, [\"c\"]), OptionCards(\"cards\", null, [OptionCard(\"a\", \"A\")], null, \"a\"), SwitchGroup(\"switches\", [SwitchItem(\"On\", \"\", \"on\", true)]), CheckBoxGroup(\"checks\", [CheckBoxItem(\"On\", \"\", \"on\", true)]), Slider(\"wind\", \"continuous\", 0, 25, 1, [7])])\n");

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Instance.Session.Forms.Get("f", "chips").TryGetArray(out var chips) && chips.Count == 1);
            Assert.Equal("a", cut.Instance.Session.Forms.Get("f", "cards").ToString());
            Assert.True(cut.Instance.Session.Forms.Get("f", "switches").TryGetObject(out var switches) && switches.ContainsKey("on"));
            Assert.True(cut.Instance.Session.Forms.Get("f", "checks").TryGetObject(out var checks) && checks.ContainsKey("on"));
            Assert.True(cut.Instance.Session.Forms.Get("f", "wind").TryGetArray(out _));
        });
    }

    // The rules belong to the standard contracts: a host's own component with one of their names keeps to itself.
    [Fact]
    public async Task A_component_of_another_definition_with_the_same_name_does_not_seed()
    {
        var library = new LibraryDefinition("own", "Slider",
        [
            new("All",
            [
                new("Slider", "Its own thing.", typeof(FirstNumberView),
                [
                    new PropDefinition("name", TypeShape.String) { Required = true },
                    new PropDefinition("defaultValue", TypeShape.Any),
                ])
                {
                    Traits = new ComponentTraits(FieldNameProp: "name", FieldComponentType: "own-slider"),
                },
            ], []),
        ]);
        await using var context = TestLibrary.NewContext(library);

        var cut = context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, "root = Slider(\"wind\", 7)\n"));

        Assert.Equal("7", cut.Find(".first").TextContent);
        Assert.True(cut.Instance.Session.Forms.Get(null, "wind").IsNull);
    }

    [Fact]
    public async Task Item_components_come_out_flat_and_in_order()
    {
        await using var context = TestLibrary.NewContext(TestLibrary.MudWith("openui-items", views => views.Add<ItemsView>("Stack").Add<AnyView>("TextContent")));
        var cut = context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, "root = Stack([TextContent(\"a\"), [TextContent(\"b\"), \"not a component\"], TextContent(\"c\")])\n"));

        Assert.Equal(["a", "b", "c"], cut.FindAll(".item").Select(item => item.TextContent));
    }

    [Theory]
    [InlineData("[7, 9]", 7d)]
    [InlineData("7", 7d)]
    [InlineData("\"7\"", 7d)]
    [InlineData("[]", null)]
    [InlineData("null", null)]
    public void The_first_number_of_a_slider_s_value(string json, double? expected) =>
        Assert.Equal(expected, DisplayValues.FirstNumber(OuiValue.FromJson(JsonDocument.Parse(json).RootElement)));
}
