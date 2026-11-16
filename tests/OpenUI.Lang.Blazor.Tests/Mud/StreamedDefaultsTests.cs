using System.Buffers;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using OpenUI.Lang.Blazor.Mud;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor.Tests.Mud;

/// <summary>A field's default goes into its form once the program has finished, however the program arrived.</summary>
public sealed class StreamedDefaultsTests
{
    private static BunitContext NewContext()
    {
        var context = TestLibrary.NewContext(services => services.AddMudServices().AddOpenUIComponents(TestLibrary.Mud));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Render<MudPopoverProvider>();
        return context;
    }

    private static string InForm(string field) => $"root = Form(\"f\", Buttons([Button(\"Go\")]), [FormControl(\"Field\", {field})])\n";

    public static TheoryData<string, string> Fields() => new()
    {
        { InForm("Chips(\"x\", \"multiple\", [ChipItem(\"alpha\", \"A\"), ChipItem(\"beta\", \"B\"), ChipItem(\"gamma\", \"C\")], null, [\"alpha\", \"beta\"])"), "[\"alpha\",\"beta\"]" },
        { InForm("Chips(\"x\", \"single\", [ChipItem(\"dawn\", \"D\"), ChipItem(\"dusk\", \"U\")], null, \"dawn\")"), "\"dawn\"" },
        { InForm("OptionCards(\"x\", \"single\", [OptionCard(\"ground\", \"G\"), OptionCard(\"air\", \"A\")], null, \"ground\")"), "\"ground\"" },
        { InForm("OptionCards(\"x\", null, [OptionCard(\"ground\", \"G\"), OptionCard(\"air\", \"A\")], null, \"ground\")"), "\"ground\"" },
        { InForm("RadioGroup(\"x\", [RadioItem(\"Corn\", \"\", \"corn\"), RadioItem(\"Wheat\", \"\", \"wheat\")], \"corn\")"), "\"corn\"" },
        { InForm("Slider(\"x\", \"continuous\", 0, 100, 1, [50])"), "[50]" },
        { InForm("CheckBoxGroup(\"x\", [CheckBoxItem(\"A\", \"\", \"a\", true), CheckBoxItem(\"B\", \"\", \"b\", true), CheckBoxItem(\"C\", \"\", \"c\")])"), "{\"a\":true,\"b\":true,\"c\":false}" },
        { "root = Form(\"f\", Buttons([Button(\"Go\")]), [SwitchGroup(\"x\", [SwitchItem(\"A\", \"\", \"a\", true), SwitchItem(\"B\", \"\", \"b\", true), SwitchItem(\"C\", \"\", \"c\")])])\n", "{\"a\":true,\"b\":true,\"c\":false}" },
    };

    private static string Held(IRenderedComponent<OpenUIProgram> cut) => Json(cut.Instance.Session.Forms.Get("f", "x"));

    private static string Json(OuiValue value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            value.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public async Task A_field_rendered_whole_holds_its_default(string program, string expected)
    {
        await using var context = NewContext();

        var cut = context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, program));

        cut.WaitForAssertion(() => Assert.Equal(expected, Held(cut)));
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public async Task A_field_streamed_a_character_at_a_time_holds_nothing_until_it_ends_and_then_its_default(string program, string expected)
    {
        await using var context = NewContext();
        var cut = context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, "")
            .Add(component => component.IsStreaming, true));

        for (var length = 1; length <= program.Length; length++)
        {
            var text = program[..length];
            cut.Render(parameters => parameters.Add(component => component.Program, text).Add(component => component.IsStreaming, true));
            Assert.True(cut.Instance.Session.Forms.Get("f", "x").IsNull, text);
        }

        cut.Render(parameters => parameters.Add(component => component.Program, program).Add(component => component.IsStreaming, false));

        cut.WaitForAssertion(() => Assert.Equal(expected, Held(cut)));
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public async Task A_field_streamed_in_chunks_of_any_size_holds_its_default(string program, string expected)
    {
        foreach (var size in new[] { 3, 7, 11, 17 })
        {
            await using var context = NewContext();

            var cut = context.Render<OpenUIProgram>(parameters => parameters
                .Add(component => component.Chunks, Chunked(program, size)));

            cut.WaitForAssertion(() => Assert.Equal(expected, Held(cut)));
        }
    }

    private static async IAsyncEnumerable<string> Chunked(string text, int size)
    {
        for (var start = 0; start < text.Length; start += size)
        {
            await Task.Yield();
            yield return text[start..Math.Min(start + size, text.Length)];
        }
    }

    [Fact]
    public async Task A_field_the_user_changed_while_it_streamed_keeps_the_change()
    {
        await using var context = NewContext();
        var program = InForm("RadioGroup(\"x\", [RadioItem(\"Corn\", \"\", \"corn\"), RadioItem(\"Wheat\", \"\", \"wheat\")], \"corn\")");
        var cut = context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, program)
            .Add(component => component.IsStreaming, true));

        await cut.InvokeAsync(() => cut.FindComponent<MudRadioGroup<string>>().Instance.ValueChanged.InvokeAsync("wheat"));
        cut.Render(parameters => parameters.Add(component => component.Program, program).Add(component => component.IsStreaming, false));

        cut.WaitForAssertion(() => Assert.Equal("\"wheat\"", Held(cut)));
    }

    [Fact]
    public async Task A_field_in_a_modal_gets_its_default_when_the_stream_ends()
    {
        await using var context = NewContext();
        context.Render<MudDialogProvider>();
        const string program = "root = Form(\"f\", Buttons([Button(\"Go\")]), [Modal(\"Pick\", true, [Slider(\"x\", \"continuous\", 0, 100, 1, [50])])])\n";
        var cut = context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, program)
            .Add(component => component.IsStreaming, true));
        Assert.True(cut.Instance.Session.Forms.Get("f", "x").IsNull);

        cut.Render(parameters => parameters.Add(component => component.Program, program).Add(component => component.IsStreaming, false));

        cut.WaitForAssertion(() => Assert.Equal("[50]", Held(cut)));
    }

    [Fact]
    public async Task A_prerender_shows_a_default_and_seeds_nothing()
    {
        await using var context = TestLibrary.NewContext(services => services.AddMudServices().AddOpenUIComponents(TestLibrary.Mud), interactive: false);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Render<MudPopoverProvider>();

        var cut = context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, InForm("Slider(\"x\", \"continuous\", 0, 100, 1, [50])")));

        Assert.Contains("mud-typography-caption\">50<", cut.Markup);
        Assert.DoesNotContain("x: 50", cut.Markup);
        Assert.True(cut.Instance.Session.Forms.Get("f", "x").IsNull);
    }

    [Theory]
    [InlineData("CheckBoxGroup(\"x\", [CheckBoxItem(\"A\", \"\", \"a\", true), CheckBoxItem(\"B\", \"\", \"b\")], null, $flags)")]
    [InlineData("SwitchGroup(\"x\", [SwitchItem(\"A\", \"\", \"a\", true), SwitchItem(\"B\", \"\", \"b\")], null, $flags)")]
    public async Task A_flag_drawn_on_beside_a_bound_map_that_omits_it_goes_into_the_map(string field)
    {
        await using var context = NewContext();

        var cut = context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, $"$flags = {{}}\nroot = {field}\n"));

        cut.WaitForAssertion(() => Assert.Equal("{\"a\":true,\"b\":false}", Json(cut.Instance.Session.State.Get("$flags"))));
    }

    [Fact]
    public async Task A_flag_the_user_turned_off_stays_off()
    {
        await using var context = NewContext();
        var cut = context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, "root = Form(\"f\", Buttons([Button(\"Go\")]), [SwitchGroup(\"x\", [SwitchItem(\"A\", \"\", \"a\", true)])])\n"));
        cut.WaitForAssertion(() => Assert.Equal("{\"a\":true}", Held(cut)));

        await cut.InvokeAsync(() => cut.FindComponent<MudSwitch<bool>>().Instance.ValueChanged.InvokeAsync(false));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("{\"a\":false}", Held(cut));
            Assert.DoesNotContain("mud-checked", cut.Find(".mud-switch-base").ClassList);
        });
    }
}
