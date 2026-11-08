using Bunit;
using OpenUI.Lang.Runtime;

namespace OpenUI.Lang.Blazor.Tests;

public sealed class StreamingIdentityTests
{
    [Fact]
    public async Task An_input_inline_in_the_statement_being_written_stays_the_same_input()
    {
        await using var context = TestLibrary.NewContext();
        var cut = context.Render<OpenUIProgram>(p => p
            .Add(x => x.IsStreaming, true)
            .Add(x => x.Program, """root = Stack([Input("Name", $name)"""));
        var instance = cut.Find("input").GetAttribute("data-instance");

        cut.Find("input").Input("Dale");

        // Each of these changes the parent of the input: more children, in the statement still open.
        var session = cut.Instance.Session;
        foreach (var chunk in new[] { """, Text("After one")""", """, Text("After two")""", """, Stack([Text("Nested")])""", "])\n" })
        {
            session.Append(chunk);
        }

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("p.text").Count));
        Assert.Equal(instance, cut.Find("input").GetAttribute("data-instance"));
        Assert.Equal("Dale", cut.Find("input").GetAttribute("value"));
    }

    [Fact]
    public async Task An_input_keeps_its_identity_and_value_when_siblings_arrive_before_it_and_its_default_arrives_after()
    {
        await using var context = TestLibrary.NewContext();
        var cut = context.Render<OpenUIProgram>(p => p
            .Add(x => x.IsStreaming, true)
            .Add(x => x.Program, "root = Stack([title, name, footer])\nname = Input(\"Name\", $name)\n"));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("input")));
        var instance = cut.Find("input").GetAttribute("data-instance");

        cut.Find("input").Input("Typed");
        var session = cut.Instance.Session;
        session.Append("$name = \"default\"\ntitle = Text(\"Title\")\n");
        session.Append("footer = Text(\"Footer\")\n");
        session.EndStream();

        cut.WaitForAssertion(() =>
        {
            var markup = cut.Markup;
            Assert.True(markup.IndexOf("Title", StringComparison.Ordinal) < markup.IndexOf("data-instance", StringComparison.Ordinal));
            Assert.Contains("Footer", markup);
        });
        Assert.Equal(instance, cut.Find("input").GetAttribute("data-instance"));
        Assert.Equal("Typed", cut.Find("input").GetAttribute("value"));
    }

    [Fact]
    public async Task A_rewrite_of_the_whole_program_keeps_an_input_whose_statement_survives()
    {
        await using var context = TestLibrary.NewContext();
        var cut = context.Render<OpenUIProgram>(p => p
            .Add(x => x.Program, "root = Stack([name])\nname = Input(\"Name\", $name)\n"));
        var instance = cut.Find("input").GetAttribute("data-instance");
        cut.Find("input").Input("Typed");

        cut.Render(p => p.Add(x => x.Program, "root = Stack([intro, name])\nintro = Text(\"Hello\")\nname = Input(\"Name\", $name)\n"));

        Assert.Single(cut.FindAll("p.text"));
        Assert.Equal(instance, cut.Find("input").GetAttribute("data-instance"));
        Assert.Equal("Typed", cut.Find("input").GetAttribute("value"));
    }

    [Fact]
    public async Task A_statement_used_twice_in_one_list_draws_twice()
    {
        await using var context = TestLibrary.NewContext();

        var cut = context.Render<OpenUIProgram>(p => p
            .Add(x => x.Program, "root = Stack([twice, twice])\ntwice = Text(\"Again\")\n"));

        Assert.Equal(2, cut.FindAll("p.text").Count);
    }

    [Fact]
    public async Task A_stream_from_another_thread_reaches_the_screen()
    {
        await using var context = TestLibrary.NewContext();
        await using var session = OpenUISession.Create(TestLibrary.Definition.Library);
        var cut = context.Render<OpenUIView>(p => p
            .Add(x => x.Session, session)
            .Add(x => x.Components, TestLibrary.Definition.Components));

        await Task.Run(() => session.Load("root = Text(\"From elsewhere\")\n"));

        cut.WaitForAssertion(() => Assert.Equal("From elsewhere", cut.Find("p.text").TextContent));
    }

    [Fact]
    public async Task Chunks_render_as_they_arrive_and_queries_wait_for_the_end()
    {
        await using var context = TestLibrary.NewContext();
        var gate = new TaskCompletionSource();
        var calls = 0;
        var tools = new OpenUITools().Add("load", OpenUIToolEffect.Read, (_, _) =>
        {
            calls++;
            return ValueTask.FromResult<Values.OuiValue>(new Values.OuiObject.Builder().Set("text", "Loaded").ToObject());
        });

        async IAsyncEnumerable<string> Chunks()
        {
            yield return "root = Stack([Text(data.text), Text(\"First\")])\n";
            await gate.Task;
            yield return "data = Query(\"load\", {}, {text: \"Waiting\"})\n";
        }

        var cut = context.Render<OpenUIProgram>(p => p
            .Add(x => x.Tools, tools)
            .Add(x => x.Chunks, Chunks()));

        cut.WaitForAssertion(() => Assert.Contains("First", cut.Markup));
        Assert.Equal(0, calls);

        gate.SetResult();

        cut.WaitForAssertion(() => Assert.Contains("Loaded", cut.Markup));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task A_chunk_source_that_fails_keeps_what_arrived_and_tells_the_host()
    {
        await using var context = TestLibrary.NewContext();
        Exception? failure = null;

        static async IAsyncEnumerable<string> Chunks()
        {
            yield return "root = Text(\"Partial\")\n";
            await Task.Yield();
            throw new IOException("The connection dropped.");
        }

        var cut = context.Render<OpenUIProgram>(p => p
            .Add(x => x.Chunks, Chunks())
            .Add(x => x.OnChunksFailed, (Exception error) => failure = error));

        cut.WaitForAssertion(() => Assert.IsType<IOException>(failure));
        Assert.Equal("Partial", cut.Find("p.text").TextContent);
        Assert.False(cut.Instance.Session.Current.IsStreaming);
    }
}
