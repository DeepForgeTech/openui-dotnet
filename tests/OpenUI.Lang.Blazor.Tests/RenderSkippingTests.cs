using Bunit;
using OpenUI.Lang.Blazor.Tests.TestComponents;
using OpenUI.Lang.Library;

namespace OpenUI.Lang.Blazor.Tests;

/// <summary>A view whose props are equal by value to the last ones does not draw again, unless something else it reads changed.</summary>
public sealed class RenderSkippingTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_view_whose_props_did_not_change_does_not_draw_when_a_sibling_does()
    {
        await using var context = TestLibrary.NewContext();
        var cut = Render(context, "$name = \"Ann\"\nroot = Stack([Input(\"Name\", $name), Text(\"Hello \" + $name), Box(Text(\"Fixed\"))])\n");
        var greeting = TextNamed(cut, "Hello Ann");
        var fixedText = TextNamed(cut, "Fixed");
        var box = cut.FindComponent<BoxView>();
        var (greetingDraws, fixedDraws, boxDraws) = (greeting.RenderCount, fixedText.RenderCount, box.RenderCount);

        cut.Find("input").Input("Dale");

        cut.WaitForAssertion(() => Assert.Equal("Hello Dale", greeting.Find("p").TextContent));
        Assert.True(greeting.RenderCount > greetingDraws);
        Assert.Equal(fixedDraws, fixedText.RenderCount);
        Assert.Equal(boxDraws, box.RenderCount);
    }

    [Fact]
    public async Task A_field_the_host_writes_draws_in_its_input_though_the_props_did_not_change()
    {
        await using var context = TestLibrary.NewContext();
        var cut = Render(context, "root = Form(\"contact\", [Box(Input(\"Email\", \"none\"))])\n");

        await cut.InvokeAsync(() => cut.Instance.Session.Forms.Set("contact", "Email", "a@b.co", "input"));

        cut.WaitForAssertion(() => Assert.Equal("a@b.co", cut.Find("input").GetAttribute("value")));
    }

    [Fact]
    public async Task A_failed_submit_shows_its_error_in_a_field_whose_props_did_not_change()
    {
        await using var context = TestLibrary.NewContext();
        var cut = Render(context, "root = Form(\"contact\", [Box(Input(\"Email\", null, {required: true})), Button(\"Send\", @ToAssistant(\"Sent\"))])\n");
        Assert.Empty(cut.Find(".field-error").TextContent);

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.Find(".field-error").TextContent));
    }

    [Fact]
    public async Task A_refresh_draws_every_view_again()
    {
        await using var context = TestLibrary.NewContext();
        var cut = Render(context, "root = Stack([Box(Text(\"Fixed\"))])\n");
        var fixedText = TextNamed(cut, "Fixed");
        var draws = fixedText.RenderCount;

        await cut.InvokeAsync(() => fixedText.Instance.Context.Refresh());

        cut.WaitForAssertion(() => Assert.True(fixedText.RenderCount > draws));
    }

    [Fact]
    public async Task The_end_of_the_stream_draws_a_view_whose_props_did_not_change()
    {
        await using var context = TestLibrary.NewContext();
        var cut = context.Render<OpenUIProgram>(p => p
            .Add(x => x.IsStreaming, true)
            .Add(x => x.Program, "root = Stack([Box(Text(\"Fixed\"))])\n"));
        var fixedText = TextNamed(cut, "Fixed");
        var draws = fixedText.RenderCount;

        await cut.InvokeAsync(() => cut.Instance.Session.EndStream());

        cut.WaitForAssertion(() => Assert.True(fixedText.RenderCount > draws));
    }

    [Fact]
    public async Task A_view_draws_its_own_busy_state_though_its_props_did_not_change()
    {
        var source = new LibraryDefinition("once", "Once", [new("All", [new("Once", "Runs once at a time.", typeof(RunOnceView), [])], [])]);
        await using var context = TestLibrary.NewContext(source);
        var cut = context.Render<OpenUIProgram>(parameters => parameters.Add(component => component.Program, "root = Once()\n"));
        var view = cut.FindComponent<RunOnceView>();

        // A press through the page, so the view draws as it does for a user.
        var running = cut.Find("button.once").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Equal("True", cut.Find("button.once").TextContent));

        await cut.InvokeAsync(view.Instance.Open);
        await running.WaitAsync(Budget);
        cut.WaitForAssertion(() => Assert.Equal("False", cut.Find("button.once").TextContent));
    }

    [Fact]
    public async Task A_writable_view_draws_what_the_user_wrote_before_the_program_answers()
    {
        await using var context = TestLibrary.NewContext();
        var cut = Render(context, "$v = \"a\"\nroot = Stack([HeldInput(\"V\", $v)])\n");
        var view = cut.FindComponent<HeldInputView>().Instance;

        cut.Find("input.held").Input("b");

        cut.WaitForAssertion(() => Assert.Equal("b", cut.Instance.Session.State.Get("$v").ToString()));
        // The draw that follows the write comes before the session evaluates it, so without the hold it would show "a".
        Assert.Equal(["a", "b"], view.Drawn.Take(2));
        Assert.DoesNotContain("a", view.Drawn.Skip(1));
    }

    [Fact]
    public async Task A_writable_view_shows_the_programs_answer_once_it_comes()
    {
        await using var context = TestLibrary.NewContext();
        var cut = Render(context, "$v = \"a\"\nroot = Stack([HeldInput(\"V\", $v), Button(\"Set\", @Set($v, \"z\"))])\n");

        cut.Find("input.held").Input("b");
        cut.WaitForAssertion(() => Assert.Equal("b", cut.Instance.Session.State.Get("$v").ToString()));
        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Equal("z", cut.Find("input.held").GetAttribute("value")));
    }

    private static IRenderedComponent<TextView> TextNamed(IRenderedComponent<OpenUIProgram> cut, string text) =>
        cut.FindComponents<TextView>().Single(view => view.Find("p").TextContent == text);

    private static IRenderedComponent<OpenUIProgram> Render(BunitContext context, string program) =>
        context.Render<OpenUIProgram>(p => p.Add(x => x.Program, program));
}
