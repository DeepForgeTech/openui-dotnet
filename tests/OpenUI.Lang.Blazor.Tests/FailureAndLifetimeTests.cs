using Bunit;
using Microsoft.AspNetCore.Components;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor.Tests;

public sealed class FailureAndLifetimeTests
{
    [Fact]
    public async Task A_component_that_throws_costs_only_itself_and_is_reported_once()
    {
        await using var context = TestLibrary.NewContext();
        var lists = new List<OpenUIErrorList>();
        var cut = context.Render<OpenUIProgram>(p => p
            .Add(x => x.Library, TestLibrary.Definition)
            .Add(x => x.OnErrors, (OpenUIErrorList errors) => lists.Add(errors))
            .Add(x => x.Program, "root = Stack([Text(\"Before\"), Fragile(\"boom\"), Text(\"After\")])\n"));

        cut.WaitForAssertion(() => Assert.Contains(lists, list => list.Any(error => error.Code == OpenUIErrorCode.RenderError)));

        Assert.Equal(2, cut.FindAll("p.text").Count);
        Assert.Contains("Fragile", cut.Find(".oui-diagnostic").TextContent);
        Assert.DoesNotContain("secret detail", cut.Markup);
        Assert.Single(cut.Instance.Session.Current.Errors, error => error.Code == OpenUIErrorCode.RenderError);
    }

    [Fact]
    public async Task A_failed_component_gets_another_try_when_its_props_change_and_the_host_can_replace_the_fallback()
    {
        await using var context = TestLibrary.NewContext();
        var cut = context.Render<OpenUIProgram>(p => p
            .Add(x => x.Library, TestLibrary.Definition)
            .Add(x => x.FailedContent, (RenderFragment<OuiComponent>)(node => builder => builder.AddMarkupContent(0, $"<b class=\"mine\">{node.Type} broke</b>")))
            .Add(x => x.Program, "$text = \"boom\"\nroot = Stack([Input(\"Text\", $text), Fragile($text)])\n"));
        cut.WaitForAssertion(() => Assert.Equal("Fragile broke", cut.Find("b.mine").TextContent));

        cut.Find("input").Input("fine");

        cut.WaitForAssertion(() => Assert.Equal("fine", cut.Find("p.fragile").TextContent));
        Assert.Empty(cut.FindAll("b.mine"));
    }

    // Each Text is two values, itself and its string, and the Stack and its list are two more.
    private static string TextsProgram(int texts) =>
        $"r = [{string.Concat(Enumerable.Repeat("1 ", texts))}]\nroot = Stack(@Each(r, \"i\", Text(\"y\")))\n";

    [Theory]
    [InlineData(2499, true)]
    [InlineData(2500, false)]
    public async Task Without_options_a_program_draws_5000_values_and_a_bigger_one_is_refused_with_the_error_shown(int texts, bool drawn)
    {
        await using var context = TestLibrary.NewContext();

        var cut = context.Render<OpenUIProgram>(p => p.Add(x => x.Library, TestLibrary.Definition).Add(x => x.Program, TextsProgram(texts)));

        Assert.Equal(drawn ? texts : 0, cut.FindAll("p.text").Count);
        Assert.Equal(drawn, cut.FindAll(".oui-errors").Count == 0);
        Assert.Equal(!drawn, cut.Markup.Contains("x-limit-exceeded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Options_the_host_passes_are_used_as_they_are()
    {
        await using var context = TestLibrary.NewContext();

        var cut = context.Render<OpenUIProgram>(p => p
            .Add(x => x.Library, TestLibrary.Definition)
            .Add(x => x.Options, new OpenUISessionOptions())
            .Add(x => x.Program, TextsProgram(2500)));

        Assert.Equal(2500, cut.FindAll("p.text").Count);
    }

    [Fact]
    public async Task A_program_that_does_not_parse_shows_its_errors_and_the_host_can_replace_them()
    {
        await using var context = TestLibrary.NewContext();
        const string notAProgram = "Sorry, I can't draw that.";

        var plain = context.Render<OpenUIProgram>(p => p.Add(x => x.Library, TestLibrary.Definition).Add(x => x.Program, notAProgram));
        var replaced = context.Render<OpenUIProgram>(p => p
            .Add(x => x.Library, TestLibrary.Definition)
            .Add(x => x.ErrorContent, (RenderFragment<OpenUIErrorList>)(errors => builder => builder.AddContent(0, $"{errors.Count} problem")))
            .Add(x => x.Program, notAProgram));

        Assert.Contains("parse-failed", plain.Find(".oui-errors").TextContent);
        Assert.Equal("1 problem", replaced.Markup.Trim());
    }

    [Fact]
    public async Task A_host_session_that_already_has_errors_reports_them_on_the_first_render()
    {
        await using var context = TestLibrary.NewContext();
        await using var session = OpenUISession.Create(TestLibrary.Definition.Library);
        session.Load("Not a program.");
        var lists = new List<OpenUIErrorList>();

        context.Render<OpenUIView>(p => p
            .Add(x => x.Session, session)
            .Add(x => x.Components, TestLibrary.Definition.Components)
            .Add(x => x.OnErrors, (OpenUIErrorList errors) => lists.Add(errors)));

        Assert.Contains(lists, list => list.HasFatal);
    }

    [Fact]
    public async Task Unknown_components_wait_for_the_end_of_the_stream_and_the_host_can_replace_the_line()
    {
        await using var context = TestLibrary.NewContext();
        const string program = "$on = true\nroot = Box($on ? Nope(\"x\") : null)\n";
        var cut = context.Render<OpenUIProgram>(p => p
            .Add(x => x.Library, TestLibrary.Definition)
            .Add(x => x.IsStreaming, true)
            .Add(x => x.Program, program));
        Assert.Empty(cut.FindAll(".oui-diagnostic"));

        cut.Render(p => p.Add(x => x.IsStreaming, false));
        Assert.Contains("Nope", cut.Find(".oui-diagnostic").TextContent);

        var replaced = context.Render<OpenUIProgram>(p => p
            .Add(x => x.Library, TestLibrary.Definition)
            .Add(x => x.UnknownContent, (RenderFragment<OuiComponent>)(node => builder => builder.AddContent(0, $"No {node.Type} here")))
            .Add(x => x.Program, program));
        Assert.Contains("No Nope here", replaced.Markup);
    }

    [Fact]
    public async Task A_component_the_registry_lacks_is_named_instead_of_left_blank()
    {
        await using var context = TestLibrary.NewContext();
        await using var session = OpenUISession.Create(TestLibrary.Definition.Library);
        session.Load("root = Text(\"Hello\")\n");

        var cut = context.Render<OpenUIView>(p => p
            .Add(x => x.Session, session)
            .Add(x => x.Components, new ComponentRegistry()));

        Assert.Contains("Text", cut.Find(".oui-diagnostic").TextContent);
    }

    [Fact]
    public async Task While_a_stream_is_open_the_pending_content_shows_and_the_host_can_replace_it()
    {
        await using var context = TestLibrary.NewContext();
        var plain = context.Render<OpenUIProgram>(p => p.Add(x => x.Library, TestLibrary.Definition).Add(x => x.IsStreaming, true).Add(x => x.Program, "ro"));
        Assert.NotNull(plain.Find(".oui-pending"));

        plain.Render(p => p.Add(x => x.Program, "root = Text(\"There\")\n"));
        Assert.Empty(plain.FindAll(".oui-pending"));

        var replaced = context.Render<OpenUIProgram>(p => p
            .Add(x => x.Library, TestLibrary.Definition)
            .Add(x => x.IsStreaming, true)
            .Add(x => x.PendingContent, (RenderFragment<RenderSnapshot>)(_ => builder => builder.AddContent(0, "Writing")))
            .Add(x => x.Program, "root = Text(\"There\")\n"));
        Assert.Contains("Writing", replaced.Markup);

        replaced.Render(p => p.Add(x => x.IsStreaming, false));
        Assert.DoesNotContain("Writing", replaced.Markup);
    }

    [Fact]
    public async Task Disposing_the_component_disposes_its_session_and_cancels_its_tools()
    {
        var context = TestLibrary.NewContext();
        var cancelled = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var tools = new OpenUITools().Add("slow", OpenUIToolEffect.Read, async (_, ct) =>
        {
            started.TrySetResult();
            using (ct.Register(() => cancelled.TrySetResult()))
            {
                await Task.Delay(Timeout.Infinite, ct);
            }

            return OuiValue.Null;
        });
        var cut = context.Render<OpenUIProgram>(p => p
            .Add(x => x.Library, TestLibrary.Definition)
            .Add(x => x.Tools, tools)
            .Add(x => x.Program, "slow = Query(\"slow\", {}, {})\nroot = Text(\"Here\")\n"));
        var session = cut.Instance.Session;
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await context.DisposeAsync();

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Throws<ObjectDisposedException>(() => session.Append("x"));
    }

    [Fact]
    public async Task Disposing_the_component_stops_a_stream_of_chunks()
    {
        var context = TestLibrary.NewContext();
        var stopped = new TaskCompletionSource();

        async IAsyncEnumerable<string> Chunks([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return "root = Text(\"First\")\n";
            using (ct.Register(() => stopped.TrySetResult()))
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
        }

        var cut = context.Render<OpenUIProgram>(p => p.Add(x => x.Library, TestLibrary.Definition).Add(x => x.Chunks, Chunks()));
        cut.WaitForAssertion(() => Assert.Contains("First", cut.Markup));

        await context.DisposeAsync();

        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_view_leaves_a_session_it_does_not_own_alive_and_stops_listening_to_it()
    {
        await using var session = OpenUISession.Create(TestLibrary.Definition.Library);
        var context = TestLibrary.NewContext();
        var cut = context.Render<OpenUIView>(p => p
            .Add(x => x.Session, session)
            .Add(x => x.Components, TestLibrary.Definition.Components));

        await context.DisposeAsync();

        session.Load("root = Text(\"Still alive\")\n");
        Assert.NotNull(session.Current.Root);
    }

    [Fact]
    public async Task Prerendering_draws_the_program_and_runs_no_tools()
    {
        await using var context = TestLibrary.NewContext(interactive: false);
        var calls = 0;
        var tools = new OpenUITools().Add("load", OpenUIToolEffect.Read, (_, _) =>
        {
            calls++;
            return ValueTask.FromResult(OuiValue.Null);
        });

        var cut = context.Render<OpenUIProgram>(p => p
            .Add(x => x.Library, TestLibrary.Definition)
            .Add(x => x.Tools, tools)
            .Add(x => x.Program, "data = Query(\"load\", {}, {text: \"Default\"})\nroot = Text(data.text)\n"));

        Assert.Equal("Default", cut.Find("p.text").TextContent);
        Assert.Equal(0, calls);
    }
}
