using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using OpenUI.Lang.Blazor.Mud;
using OpenUI.Lang.Runtime;

namespace OpenUI.Lang.Blazor.Tests.Mud;

/// <summary>The markdown a session may have drawn at once, whoever made the session.</summary>
public sealed class MarkdownAllowanceTests
{
    // A time that fails the test by name when the work does not stop, in place of the run's hang limit.
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(90);

    private static BunitContext NewContext()
    {
        var context = TestLibrary.NewContext(services => services.AddMudServices().AddOpenUIComponents(TestLibrary.Mud));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Render<MudPopoverProvider>();
        return context;
    }

    // Each text is a link bracket repeated to just under the limit for one text, which costs the parser time with the
    // square of its length, and each differs from the others so that none is found in the cache.
    private static string CostlyTexts(int count)
    {
        var costly = string.Concat(Enumerable.Repeat("[a](", (Markdown.MaxChars - 8) / 4));
        return $"costly = \"{costly}\"\nroot = Stack([{string.Join(", ", Enumerable.Range(0, count).Select(index => $"TextContent(costly + \"{index}\")"))}])\n";
    }

    private static int Plain(IRenderedComponent<OpenUIView> cut) => cut.FindAll("div[style='white-space: pre-wrap']").Count;

    [Fact]
    public async Task A_host_s_own_session_stops_parsing_at_the_allowance_and_shows_the_rest_as_it_is()
    {
        await using var context = NewContext();
        await using var session = OpenUISession.Create(TestLibrary.Mud.Library);
        session.Load(CostlyTexts(100));
        var started = System.Diagnostics.Stopwatch.StartNew();

        var cut = await Task.Run(() => context.Render<OpenUIView>(parameters => parameters
            .Add(component => component.Session, session)
            .Add(component => component.Components, TestLibrary.Mud.Components))).WaitAsync(Budget);

        // The allowance holds 8 of them, and the time they take fewer on a slow machine.
        Assert.InRange(Plain(cut), 92, 99);
        Assert.Contains("[a]([a](", cut.Markup);
        Assert.Contains("Shown unformatted.", cut.Markup);
        Assert.InRange(started.Elapsed.TotalSeconds, 0, 25);
    }

    [Fact]
    public async Task A_program_given_its_own_options_stops_parsing_at_the_allowance_too()
    {
        await using var context = NewContext();
        var started = System.Diagnostics.Stopwatch.StartNew();

        var cut = await Task.Run(() => context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, CostlyTexts(20))
            .Add(component => component.Options, new OpenUISessionOptions()))).WaitAsync(Budget);

        Assert.InRange(cut.FindAll("div[style='white-space: pre-wrap']").Count, 12, 19);
        Assert.InRange(started.Elapsed.TotalSeconds, 0, 25);
    }

    [Fact]
    public async Task Drawing_again_shows_the_same_texts_as_it_did()
    {
        await using var context = NewContext();
        await using var session = OpenUISession.Create(TestLibrary.Mud.Library);
        session.Load(CostlyTexts(12));
        var cut = context.Render<OpenUIView>(parameters => parameters
            .Add(component => component.Session, session)
            .Add(component => component.Components, TestLibrary.Mud.Components));
        var plain = Plain(cut);
        Assert.InRange(plain, 4, 11);

        for (var pass = 0; pass < 5; pass++)
        {
            session.Load(CostlyTexts(12));
        }

        cut.WaitForAssertion(() => Assert.Equal(plain, Plain(cut)));
    }

    [Fact]
    public async Task An_ordinary_long_reply_stays_formatted()
    {
        await using var context = NewContext();
        var section = string.Concat(Enumerable.Repeat("A **point** worth making, with a [link](https://example.com), and a few more words to fill the line out. ", 10));
        var program = $"root = Stack([{string.Join(", ", Enumerable.Range(0, 100).Select(index => $"TextContent(\"{section}{index}\")"))}])\n";

        // 108 KB, more than three times what one text may hold.
        var cut = context.Render<OpenUIProgram>(parameters => parameters.Add(component => component.Program, program));

        Assert.Equal(100, cut.FindAll("strong").Count / 10);
        Assert.DoesNotContain("pre-wrap", cut.Markup);
        Assert.DoesNotContain("Shown unformatted.", cut.Markup);
    }

    // The text is 24 KB of link brackets built by doubling, then each chunk is one statement that changes the text a little, so
    // that every chunk parses a costly text the cache has not seen.
    [Fact]
    public async Task A_stream_that_changes_a_costly_text_on_every_chunk_stops_parsing_when_the_time_is_spent()
    {
        await using var context = NewContext();
        var program = "a0 = \"[](\"\n" + string.Concat(Enumerable.Range(1, 13).Select(index => $"a{index} = a{index - 1} + a{index - 1}\n")) + "p = \"0\"\nroot = TextContent(a13 + p)\n";
        var cut = context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, program)
            .Add(component => component.IsStreaming, true));
        var started = System.Diagnostics.Stopwatch.StartNew();

        await Task.Run(() =>
        {
            for (var chunk = 1; chunk <= 200; chunk++)
            {
                program += $"p = \"{chunk}\"\n";
                cut.Render(parameters => parameters
                    .Add(component => component.Program, program)
                    .Add(component => component.IsStreaming, true));
            }
        }).WaitAsync(Budget);

        // Two hundred parses of this text take a minute and more.
        Assert.InRange(started.Elapsed.TotalSeconds, 0, 25);
        Assert.Contains("Shown unformatted.", cut.Markup);
    }

    [Fact]
    public async Task Past_the_budget_an_empty_text_draws_nothing_and_says_nothing_and_a_text_says_it_is_unformatted()
    {
        await using var context = NewContext();
        var cut = context.Render<OpenUIProgram>(parameters => parameters.Add(component => component.Program, "root = Stack([TextContent(\"\"), TextContent(\"first\")])\n"));
        MarkdownAllowance.Of(cut.FindComponent<MarkdownText>().Instance.Context).Time.Spend(TimeSpan.FromMinutes(1));

        cut.Render(parameters => parameters.Add(component => component.Program, "root = Stack([MarkDownRenderer(\"\"), TextContent(\"second \" + \"text\")])\n"));

        cut.WaitForAssertion(() => Assert.Contains("second text", cut.Markup));
        Assert.Single(cut.FindAll("div.mud-typography-caption"), note => note.TextContent == "Shown unformatted.");
        Assert.DoesNotContain("<div style=\"white-space: pre-wrap\"></div>", cut.Markup);
    }

    [Fact]
    public async Task A_stream_that_grows_one_text_does_not_use_the_allowance_up()
    {
        await using var context = NewContext();
        var paragraph = string.Concat(Enumerable.Repeat("Some **bold** words in a paragraph of a reply that goes on. ", 20));
        var cut = context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, "root = TextContent(\"\")\n")
            .Add(component => component.IsStreaming, true));

        // Sixty chunks of ten paragraphs, a total of 600 KB parsed over the stream.
        for (var chunk = 1; chunk <= 60; chunk++)
        {
            var text = string.Concat(Enumerable.Repeat(paragraph, chunk / 6 + 1));
            cut.Render(parameters => parameters
                .Add(component => component.Program, $"root = TextContent(\"{text}\")\n")
                .Add(component => component.IsStreaming, true));
        }

        Assert.Contains("<strong>bold</strong>", cut.Markup);
        Assert.DoesNotContain("pre-wrap", cut.Markup);
    }

    [Fact]
    public void A_piece_that_leaves_gives_its_characters_to_one_that_was_cut()
    {
        var allowance = new MarkdownAllowance();
        var (first, second) = (new object(), new object());
        var redrawn = 0;

        Assert.True(allowance.Fit(first, MarkdownAllowance.Total - 10, () => { }));
        Assert.False(allowance.Fit(second, 100, () => redrawn++));
        allowance.Release(first);

        Assert.Equal(1, redrawn);
        Assert.True(allowance.Fit(second, 100, () => redrawn++));
    }

    [Fact]
    public void Cut_pieces_get_the_characters_back_in_the_order_they_were_cut()
    {
        var allowance = new MarkdownAllowance();
        var (holder, first, second, third) = (new object(), new object(), new object(), new object());
        var redrawn = new List<string>();
        allowance.Fit(holder, MarkdownAllowance.Total, () => { });
        allowance.Fit(first, 60, () => redrawn.Add("first"));
        allowance.Fit(second, 30, () => redrawn.Add("second"));
        allowance.Fit(third, 30, () => redrawn.Add("third"));

        allowance.Release(holder);

        Assert.Equal(["first", "second", "third"], redrawn);
    }

    [Fact]
    public void A_release_stops_handing_back_when_the_characters_are_gone()
    {
        var allowance = new MarkdownAllowance();
        var (holder, other) = (new object(), new object());
        var redrawn = 0;
        allowance.Fit(holder, MarkdownAllowance.Total, () => { });
        for (var index = 0; index < 5; index++)
        {
            allowance.Fit(new object(), MarkdownAllowance.Total / 2, () => redrawn++);
        }

        allowance.Release(holder);

        Assert.Equal(2, redrawn);
    }

    [Fact]
    public void A_spent_budget_draws_only_what_is_already_drawn()
    {
        var budget = new ParseBudget(TimeSpan.Zero, TimeProvider.System);
        var fresh = "fresh " + Guid.NewGuid();
        var known = "known **" + Guid.NewGuid() + "**";
        Markdown.Block(known);

        Assert.False(Markdown.TryBlock(fresh, null, false, budget, out _));
        Assert.True(Markdown.TryBlock(known, null, false, budget, out var markup));
        Assert.Contains("<strong>", markup.Value);
    }

    [Fact]
    public void A_budget_runs_out_and_comes_back_a_second_in_every_minute_up_to_the_whole()
    {
        var clock = new ManualClock();
        var budget = new ParseBudget(TimeSpan.FromSeconds(5), clock);
        budget.Spend(TimeSpan.FromSeconds(4.9));
        Assert.False(budget.IsSpent);

        budget.Spend(TimeSpan.FromSeconds(0.2));
        Assert.True(budget.IsSpent);

        // 5.1 s spent, and six seconds give back a tenth of one.
        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.True(budget.IsSpent);

        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.False(budget.IsSpent);
        budget.Spend(TimeSpan.FromSeconds(1.5));
        Assert.True(budget.IsSpent);

        // An hour gives back more than there was to spend, and no more than the whole budget.
        clock.Advance(TimeSpan.FromHours(1));
        budget.Spend(TimeSpan.FromSeconds(4.9));
        Assert.False(budget.IsSpent);
        budget.Spend(TimeSpan.FromSeconds(0.2));
        Assert.True(budget.IsSpent);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan time) => _ticks += time.Ticks;
    }

    [Fact]
    public void A_piece_drawn_again_replaces_what_it_held()
    {
        var allowance = new MarkdownAllowance();
        var piece = new object();

        for (var pass = 0; pass < 100; pass++)
        {
            Assert.True(allowance.Fit(piece, MarkdownAllowance.Total, () => { }));
        }
    }

    [Fact]
    public void A_piece_that_does_not_fit_is_not_told_to_draw_again_until_it_does()
    {
        var allowance = new MarkdownAllowance();
        var (first, second, third) = (new object(), new object(), new object());
        var redrawn = 0;
        allowance.Fit(first, 100, () => { });
        allowance.Fit(second, MarkdownAllowance.Total, () => redrawn++);
        allowance.Fit(third, 100, () => { });

        allowance.Release(third);

        Assert.Equal(0, redrawn);
    }
}
