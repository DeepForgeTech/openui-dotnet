using Bunit;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor.Tests;

public sealed class BindingAndActionTests
{
    [Fact]
    public async Task A_bound_input_writes_its_variable_and_every_reader_follows()
    {
        await using var context = TestLibrary.NewContext();
        var cut = Render(context, "$name = \"Ann\"\nroot = Stack([Input(\"Name\", $name), Text(\"Hello \" + $name)])\n");
        Assert.Equal("Ann", cut.Find("input").GetAttribute("value"));

        cut.Find("input").Input("Dale");

        cut.WaitForAssertion(() => Assert.Equal("Hello Dale", cut.Find("p.text").TextContent));
        Assert.Equal("Dale", cut.Instance.Session.State.Get("$name").ToString());
    }

    [Fact]
    public async Task An_action_writing_a_variable_reaches_a_bound_input()
    {
        await using var context = TestLibrary.NewContext();
        var cut = Render(context, "$name = \"Ann\"\nroot = Stack([Input(\"Name\", $name), Button(\"Set\", @Set($name, \"Bo\")), Button(\"Reset\", @Reset($name))])\n");

        cut.FindAll("button")[0].Click();
        cut.WaitForAssertion(() => Assert.Equal("Bo", cut.Find("input").GetAttribute("value")));

        cut.FindAll("button")[1].Click();
        cut.WaitForAssertion(() => Assert.Equal("Ann", cut.Find("input").GetAttribute("value")));
    }

    [Fact]
    public async Task An_unbound_input_keeps_what_was_entered_in_its_form_field()
    {
        await using var context = TestLibrary.NewContext();
        var cut = Render(context, "root = Form(\"contact\", [Input(\"Email\", \"none\")])\n");
        Assert.Equal("none", cut.Find("input").GetAttribute("value"));

        cut.Find("input").Input("a@b.co");

        cut.WaitForAssertion(() => Assert.Equal("a@b.co", cut.Find("input").GetAttribute("value")));
        Assert.Equal("a@b.co", cut.Instance.Session.Forms.Get("contact", "Email").ToString());
    }

    [Fact]
    public async Task A_host_event_carries_the_message_the_context_and_the_form_fields()
    {
        await using var context = TestLibrary.NewContext();
        var events = new List<OpenUIHostEvent>();
        var cut = context.Render<OpenUIProgram>(p => p
            .Add(x => x.Library, TestLibrary.Definition)
            .Add(x => x.OnHostEvent, (OpenUIHostEvent e) => events.Add(e))
            .Add(x => x.Program, "$note = \"\"\nroot = Form(\"order\", [Input(\"Note\", $note), Button(\"Send\", @ToAssistant(\"Save it\", {id: 42}))])\n"));

        cut.Find("input").Input("Rush");
        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Single(events));
        Assert.Equal("continue_conversation", events[0].Type);
        Assert.Equal("Save it", events[0].HumanFriendlyMessage);
        Assert.Equal(42, events[0].Params["context"].TryGetObject(out var sent) && sent["id"].TryGetNumber(out var id) ? id : 0);
        Assert.Equal("order", events[0].FormName);
        Assert.True(events[0].FormState["order"].TryGetObject(out var fields));
        Assert.True(fields["Note"].TryGetObject(out var note));
        Assert.Equal("Rush", note["value"].ToString());
    }

    [Fact]
    public async Task A_button_with_no_action_sends_its_label_and_the_form_state_to_the_assistant()
    {
        await using var context = TestLibrary.NewContext();
        var events = new List<OpenUIHostEvent>();
        var cut = context.Render<OpenUIProgram>(p => p
            .Add(x => x.Library, TestLibrary.Definition)
            .Add(x => x.OnHostEvent, (OpenUIHostEvent e) => events.Add(e))
            .Add(x => x.Program, "root = Form(\"order\", [Input(\"Note\"), Button(\"Send it\")])\n"));

        cut.Find("input").Input("Rush");
        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Single(events));
        Assert.Equal("continue_conversation", events[0].Type);
        Assert.Equal("Send it", events[0].HumanFriendlyMessage);
        Assert.True(events[0].FormState["order"].TryGetObject(out var fields));
        Assert.True(fields["Note"].TryGetObject(out var note));
        Assert.Equal("Rush", note["value"].ToString());
    }

    [Fact]
    public async Task OpenUrl_reaches_the_host_only_for_an_allowed_scheme()
    {
        await using var context = TestLibrary.NewContext();
        var events = new List<OpenUIHostEvent>();
        var cut = context.Render<OpenUIProgram>(p => p
            .Add(x => x.Library, TestLibrary.Definition)
            .Add(x => x.OnHostEvent, (OpenUIHostEvent e) => events.Add(e))
            .Add(x => x.Program, "root = Stack([Button(\"Good\", @OpenUrl(\"https://example.com\")), Button(\"Bad\", @OpenUrl(\"javascript:alert(1)\"))])\n"));

        cut.FindAll("button")[0].Click();
        cut.FindAll("button")[1].Click();

        cut.WaitForAssertion(() => Assert.Single(events));
        Assert.Equal("open_url", events[0].Type);
        Assert.Equal("https://example.com", events[0].Params["url"].ToString());
        Assert.Contains(cut.Instance.Session.Current.Errors, error => error.Component == "@OpenUrl");
    }

    [Fact]
    public async Task A_mutation_runs_through_its_tool_and_a_failure_stops_the_action_and_shows()
    {
        await using var context = TestLibrary.NewContext();
        var tools = new OpenUITools().Add("save", (args, _) => args.TryGetObject(out var input) && input["text"].ToString() == "bad"
            ? throw new OpenUIToolException("That one is rejected.", Diagnostics.OpenUIErrorCode.ToolError)
            : ValueTask.FromResult<OuiValue>(new OuiObject.Builder().Set("saved", input!["text"]).ToObject()));
        var cut = context.Render<OpenUIProgram>(p => p
            .Add(x => x.Library, TestLibrary.Definition)
            .Add(x => x.Tools, tools)
            .Add(x => x.Program, """
                $text = "good"
                $done = "no"
                save = Mutation("save", {text: $text})
                root = Stack([Input("Text", $text), Button("Save", Action([@Run(save), @Set($done, "yes")])), Text(save.status + "/" + save.error + "/" + $done)])

                """));

        cut.Find("button").Click();
        cut.WaitForAssertion(() => Assert.Equal("success//yes", cut.Find("p.text").TextContent));

        cut.Find("input").Input("bad");
        cut.WaitForAssertion(() => Assert.Equal("bad", cut.Find("input").GetAttribute("value")));
        cut.Find("button").Click();
        cut.WaitForAssertion(() => Assert.Equal("error/That one is rejected./yes", cut.Find("p.text").TextContent));
    }

    [Fact]
    public async Task A_form_with_a_failing_field_blocks_its_action_and_the_field_says_why_until_it_is_fixed()
    {
        await using var context = TestLibrary.NewContext();
        var events = new List<OpenUIHostEvent>();
        var cut = context.Render<OpenUIProgram>(p => p
            .Add(x => x.Library, TestLibrary.Definition)
            .Add(x => x.OnHostEvent, (OpenUIHostEvent e) => events.Add(e))
            .Add(x => x.Program, "root = Form(\"contact\", [Input(\"Email\", null, {required: true}), Button(\"Send\", @ToAssistant(\"Sent\"))])\n"));
        Assert.Empty(cut.Find(".field-error").TextContent);

        cut.Find("button").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.Find(".field-error").TextContent));
        Assert.Empty(events);

        cut.Find("input").Input("a@b.co");
        cut.WaitForAssertion(() => Assert.Empty(cut.Find(".field-error").TextContent));

        cut.Find("button").Click();
        cut.WaitForAssertion(() => Assert.Single(events));
    }

    private static IRenderedComponent<OpenUIProgram> Render(BunitContext context, string program) =>
        context.Render<OpenUIProgram>(p => p.Add(x => x.Library, TestLibrary.Definition).Add(x => x.Program, program));
}
