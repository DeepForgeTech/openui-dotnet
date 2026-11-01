using OpenUI.Lang.Core.Tests.Conformance;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Semantics;

namespace OpenUI.Lang.Core.Tests.Streaming;

/// <summary>
/// Language.md section 4, read from the document after each chunk rather than only at the end of the
/// stream. One test or group per numbered rule; the streaming fixtures cover the result at stream end.
/// </summary>
public sealed class MidStreamTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private static OpenUILibrary Library { get; } = ConformanceAdapter.CreateLibrary(FixtureCatalog.LoadLibrary());

    private static OpenUIStreamParser NewStream() => new OpenUIParser(Library).CreateStream();

    // The text of each child of a root Stack, as the evaluator renders it.
    private static List<string> Texts(OpenUIDocument document)
    {
        var root = OpenUIEvaluator.Evaluate(document).Root;
        if (root is null)
        {
            return [];
        }

        Assert.True(root["children"].Value.TryGetArray(out var children));
        return children.Select(child =>
        {
            Assert.True(child.TryGetComponent(out var text));
            return OpenUI.Lang.Values.Ecma.ToString(text["text"].Value);
        }).ToList();
    }

    private static Statement Named(OpenUIDocument document, string name)
    {
        Assert.True(document.TryGetStatement(name, out var statement), name + " is not in the document");
        return statement;
    }

    // Rule 1.

    [Fact]
    public void A_statement_stays_pending_until_its_newline_and_the_start_of_the_next_line()
    {
        var stream = NewStream();

        var noNewline = stream.Append("a = Text(\"x\")");
        Assert.True(Named(noNewline, "a").IsPending);
        Assert.Empty(noNewline.Delta.Completed);

        var newline = stream.Append("\n");
        Assert.True(Named(newline, "a").IsPending);
        Assert.Empty(newline.Delta.Completed);

        var nextLine = stream.Append("b");
        Assert.False(Named(nextLine, "a").IsPending);
        Assert.Equal(["a"], nextLine.Delta.Completed.ToArray());
    }

    [Fact]
    public void A_ternary_on_following_lines_does_not_complete_early()
    {
        var stream = NewStream();

        Assert.True(Named(stream.Append("a = true\n"), "a").IsPending);

        // With the ':' still to come the tail does not parse, so rule 2 leaves it out; it has not completed.
        var open = stream.Append("  ? 1\n");
        Assert.False(open.TryGetStatement("a", out _));
        Assert.Empty(open.Delta.Completed);

        Assert.True(Named(stream.Append("  : 2\n"), "a").IsPending);

        var document = stream.Append("b = 1");
        Assert.False(Named(document, "a").IsPending);
        Assert.True(Named(document, "b").IsPending);
    }

    [Fact]
    public void A_completed_statement_is_not_parsed_again_by_later_chunks()
    {
        var stream = NewStream();
        var first = Named(stream.Append("a = Text(\"x\")\nb"), "a").Syntax;

        stream.Append(" = 1\nc = 2");
        var later = stream.Append("\nd = 3");

        Assert.Same(first, Named(later, "a").Syntax);
        Assert.False(Named(later, "a").IsPending);
    }

    // Rule 2.

    [Fact]
    public void The_pending_tail_renders_after_every_chunk_with_its_string_and_brackets_closed()
    {
        var stream = NewStream();

        var open = stream.Append("root = Stack([Text(\"he");
        Assert.True(Named(open, "root").IsPending);
        Assert.True(Named(open, "root").NeededImplicitClose);
        Assert.True(open.Incomplete);
        Assert.False(open.IsFinal);
        Assert.Equal(["he"], Texts(open));

        var more = stream.Append("llo\", \"ignored\"");
        Assert.True(more.Incomplete);
        Assert.Equal(["hello"], Texts(more));

        var closed = stream.Append(")])");
        Assert.True(Named(closed, "root").IsPending);
        Assert.False(Named(closed, "root").NeededImplicitClose);
        Assert.False(closed.Incomplete);
        Assert.Equal(["hello"], Texts(closed));
    }

    [Fact]
    public void A_string_cut_inside_an_escape_gets_a_backslash_before_it_closes()
    {
        var document = NewStream().Append("root = Stack([Text(\"a\\");

        Assert.True(document.Incomplete);
        Assert.Equal(["a\\"], Texts(document));
    }

    [Fact]
    public void A_closing_bracket_that_does_not_match_is_skipped()
    {
        var document = NewStream().Append("root = Stack([Text(\"a\"))");

        Assert.True(document.Incomplete);
        Assert.Equal(["a"], Texts(document));
    }

    [Theory]
    [InlineData("a =")]
    [InlineData("a = ")]
    [InlineData("a")]
    public void A_pending_tail_that_cannot_parse_is_not_produced(string chunk)
    {
        var document = NewStream().Append(chunk);

        Assert.Empty(document.Statements);
        Assert.DoesNotContain(document.Errors, e => e.Code == OpenUIErrorCode.ParseException);
    }

    // Rule 3.

    [Fact]
    public void A_truncated_pending_statement_does_not_replace_a_completed_one()
    {
        var stream = NewStream();
        stream.Append("root = Stack([a])\na = Text(\"first\")\n");

        var document = stream.Append("a = Text(\"sec");

        Assert.False(Named(document, "a").IsPending);
        Assert.Equal(["first"], Texts(document));
    }

    [Fact]
    public void A_pending_statement_that_needed_no_closing_does_replace_a_completed_one()
    {
        var stream = NewStream();
        stream.Append("root = Stack([a])\na = Text(\"first\")\n");

        var document = stream.Append("a = Text(\"second\")");

        Assert.True(Named(document, "a").IsPending);
        Assert.Equal(["second"], Texts(document));
    }

    // Rule 4.

    [Fact]
    public void A_fence_that_arrives_late_discards_what_was_parsed_and_reports_the_reset()
    {
        var stream = NewStream();
        var prose = stream.Append("a = Text(\"prose\")\nb");
        Assert.False(prose.Delta.WasReset);
        Assert.False(Named(prose, "a").IsPending);

        var fenced = stream.Append("\n```openui\nroot = Stack([Text(\"f\")])\n");

        Assert.True(fenced.Delta.WasReset);
        Assert.Contains("a", fenced.Delta.Removed);
        Assert.False(fenced.TryGetStatement("a", out _));
        Assert.Equal(["f"], Texts(fenced));

        Assert.False(stream.Append("x").Delta.WasReset);
    }

    [Fact]
    public void A_regenerated_response_starts_the_program_again()
    {
        var stream = NewStream();
        var first = stream.Append("]]>openui:content\nroot = Stack([a])\na = Text(\"first\")\nb = 1\n");
        Assert.False(Named(first, "a").IsPending);
        Assert.Equal(["first"], Texts(first));

        var second = stream.Append("]]>openui:content\nroot = Stack([Text(\"second\")])");

        Assert.True(second.Delta.WasReset);
        Assert.Contains("a", second.Delta.Removed);
        Assert.Contains("b", second.Delta.Removed);
        Assert.False(second.TryGetStatement("a", out _));
        Assert.Equal(["second"], Texts(second));
    }

    // Rule 5.

    [Fact]
    public void An_array_element_not_yet_defined_is_left_out()
    {
        var stream = NewStream();

        var none = stream.Append("root = Stack([a, b])\n");
        Assert.Empty(Texts(none));
        Assert.Equal(["a", "b"], none.Unresolved.Order(StringComparer.Ordinal).ToArray());

        var one = stream.Append("a = Text(\"x\")");
        Assert.Equal(["x"], Texts(one));
        Assert.Equal(["b"], one.Unresolved.ToArray());

        var both = stream.Append("\nb = Text(\"y\")");
        Assert.Equal(["x", "y"], Texts(both));
        Assert.Empty(both.Unresolved);
    }

    // Rule 6.

    [Fact]
    public async Task A_query_does_not_run_until_the_stream_ends()
    {
        var calls = 0;
        var tools = new OpenUITools().Add("t", OpenUIToolEffect.Read, (args, _) =>
        {
            Interlocked.Increment(ref calls);
            return ValueTask.FromResult(args);
        });
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools });

        session.Append("q = Query(\"t\", {}, null)\nroot = Stack([Text(\"\" + q)])\n");
        session.Append("x = 1\n");
        _ = session.Current;
        await session.WhenIdleAsync().WaitAsync(Budget);
        Assert.Equal(0, Volatile.Read(ref calls));

        session.EndStream();
        await session.WhenIdleAsync().WaitAsync(Budget);
        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task A_default_read_from_a_truncated_statement_is_replaced_by_the_full_statement()
    {
        await using var session = OpenUISession.Create(Library);

        session.Append("$x = \"par");
        Assert.Equal("par", session.State.Get("$x").ToString());

        session.Append("tial\"\nroot = Stack([Text($x)])");
        Assert.Equal("partial", session.State.Get("$x").ToString());
    }

    [Fact]
    public async Task A_default_does_not_replace_state_the_user_changed_while_streaming()
    {
        await using var session = OpenUISession.Create(Library);

        session.Append("$x = \"par");
        session.State.Set("$x", "mine");
        session.Append("tial\"\nroot = Stack([Text($x)])");
        session.EndStream();

        Assert.Equal("mine", session.State.Get("$x").ToString());
    }

    // Rule 7.

    [Fact]
    public void Ending_the_stream_completes_the_pending_tail_including_a_redefinition()
    {
        var stream = NewStream();
        stream.Append("root = Stack([a])\na = Text(\"first\")\n");
        Assert.Equal(["first"], Texts(stream.Append("a = Text(\"sec")));

        var final = stream.Complete();

        Assert.True(final.IsFinal);
        Assert.True(final.Incomplete);
        Assert.False(Named(final, "a").IsPending);
        Assert.Contains("a", final.Delta.Completed);
        Assert.Equal(["sec"], Texts(final));
    }

    [Fact]
    public void Chunks_after_the_end_of_the_stream_are_refused_by_the_parser_stream()
    {
        var stream = NewStream();
        stream.Complete();

        Assert.True(stream.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => stream.Append("a = 1"));
    }

    [Fact]
    public async Task Chunks_after_the_end_of_the_stream_start_a_new_program_in_a_session()
    {
        await using var session = OpenUISession.Create(Library);
        session.Append("root = Stack([Text(\"old\")])\n");
        session.EndStream();

        session.Append("root = Stack([Text(\"new\")])");

        Assert.Equal(["new"], Texts(session.Current.Document));
    }

    // What changed in each revision.

    [Fact]
    public void The_delta_names_what_completed_and_what_changed_in_the_pending_tail()
    {
        var stream = NewStream();

        var first = stream.Append("a = 1");
        Assert.Empty(first.Delta.Completed);
        Assert.Equal(["a"], first.Delta.PendingChanged.ToArray());

        var same = stream.Append("");
        Assert.Empty(same.Delta.PendingChanged);

        var second = stream.Append("\nb = ");
        Assert.Equal(["a"], second.Delta.Completed.ToArray());
        Assert.Empty(second.Delta.PendingChanged);

        var third = stream.Append("2");
        Assert.Empty(third.Delta.Completed);
        Assert.Equal(["b"], third.Delta.PendingChanged.ToArray());
    }

    // The opening sentence of the section: a valid render after every chunk.

    public static TheoryData<string> ProgramCases() => [.. FixtureTestDiscoverer.CasesOf(FixtureSuite.CharByChar).Select(c => c.Id)];

    [Theory]
    [MemberData(nameof(ProgramCases))]
    public void Every_chunk_of_a_fixture_program_renders_without_an_exception(string caseId)
    {
        var stream = new OpenUIParser(ConformanceAdapter.CreateLibrary(FixtureCatalog.LoadLibrary())).CreateStream();
        foreach (var rune in FixtureRunner.ProgramTextOf(FixtureCatalog.Get(caseId)).EnumerateRunes())
        {
            var document = stream.Append(rune.ToString());
            var render = OpenUIEvaluator.Evaluate(document);
            Assert.DoesNotContain(render.Errors, e => e.Code == OpenUIErrorCode.ParseException);
        }
    }
}
