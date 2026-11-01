using System.Diagnostics;
using System.Text;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Semantics;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Hostile;

/// <summary>
/// One test for each guard that could be removed with every other test still passing, and exact
/// boundaries for the budgets, so that a stack guard reporting the same code cannot stand in for a limit.
/// </summary>
public partial class HostileInputTests
{
    private static OpenUILibrary BoxLibrary { get; } = OpenUILibrary.CreateBuilder("box").Root("Box")
        .Add(new ComponentSpec("Box", "", [new PropSpec("value", TypeShape.Any, false)], default, "Box(value?: any)"))
        .Build();

    private static string StatementOf(int length) => "big = \"" + new string('x', length - 8) + "\"";

    [Fact]
    public void The_default_limits_are_the_values_in_the_design()
    {
        var limits = OpenUILimits.Default;

        Assert.Equal(4 * 1024 * 1024, limits.MaxInputChars);
        Assert.Equal(256 * 1024, limits.MaxStatementChars);
        Assert.Equal(10_000, limits.MaxStatements);
        Assert.Equal(128, limits.MaxSyntaxDepth);
        Assert.Equal(256, limits.MaxEvalDepth);
        Assert.Equal(64, limits.MaxValueDepth);
        Assert.Equal(200, limits.MaxErrors);
        Assert.Equal(32, limits.MaxSessionFetchesPerSecond);
        Assert.Equal(64, limits.MaxRefreshingQueries);
        Assert.Equal(1024 * 1024, limits.MaxStringLength);
        Assert.Equal(16 * 1024 * 1024, limits.MaxTextChars);
        Assert.Equal(1000, limits.MaxActionSteps);
        Assert.Equal(16, limits.MaxMutationsPerAction);
    }

    public static TheoryData<string, string, string> ShortTextLimits() => new()
    {
        { "joined by +", "\"abcdefgh\" + \"ijklmnop\"", "longer than 10 characters" },
        { "an array joined", "\"\" + [\"abcdefgh\", \"ijklmno\"]", "longer than 10 characters" },
    };

    [Theory]
    [MemberData(nameof(ShortTextLimits))]
    public void A_string_longer_than_the_limit_is_refused(string shape, string expression, string message)
    {
        var limits = new OpenUILimits { MaxStringLength = 10 };
        var render = OpenUIEvaluator.Evaluate(new OpenUIParser(BoxLibrary).Parse($"root = Box({expression})\n"), limits: limits);
        var error = Assert.Single(render.Errors, e => e.Code.Value == LimitCode);
        Assert.True(error.Message.Contains(message, StringComparison.Ordinal), shape);
    }

    [Fact]
    public void A_string_at_the_limit_is_kept()
    {
        var limits = new OpenUILimits { MaxStringLength = 16 };
        var render = OpenUIEvaluator.Evaluate(new OpenUIParser(BoxLibrary).Parse("root = Box(\"abcdefgh\" + \"ijklmnop\")\n"), limits: limits);
        Assert.Empty(render.Errors);
        Assert.True(render.Root!["value"].Value.TryGetString(out var text) && text.Length == 16);
    }

    [Theory]
    [InlineData("\"abcdefgh\" + \"ijklmnop\"", 10)]
    [InlineData("\"\" + [\"abcdefgh\", \"ijklmno\"]", 18)]
    public void Text_past_the_pass_limit_is_refused(string expression, int limit)
    {
        var limits = new OpenUILimits { MaxTextChars = limit };
        var render = OpenUIEvaluator.Evaluate(new OpenUIParser(BoxLibrary).Parse($"root = Box({expression})\n"), limits: limits);
        var error = Assert.Single(render.Errors, e => e.Code.Value == LimitCode);
        Assert.Contains($"created more than {limit} characters", error.Message);
    }

    [Fact]
    public void A_value_holding_more_text_than_the_limit_is_refused()
    {
        var limits = new OpenUILimits { MaxTextChars = 10 };
        var render = OpenUIEvaluator.Evaluate(new OpenUIParser(BoxLibrary).Parse("root = Box([\"abcdefgh\", \"ijklmnop\"])\n"), limits: limits);
        var error = Assert.Single(render.Errors, e => e.Code.Value == LimitCode);
        Assert.Contains("expands to more than 10 characters", error.Message);
    }

    [Fact]
    public void A_statement_at_the_limit_is_kept_and_one_character_more_is_dropped_on_its_own()
    {
        var limit = OpenUILimits.Default.MaxStatementChars;
        const string Before = "root = Box(1)\n";
        const string After = "\nafter = 1\n";

        var kept = new OpenUIParser(BoxLibrary).Parse(Before + StatementOf(limit) + After);
        Assert.True(kept.TryGetStatement("big", out _));
        Assert.DoesNotContain(LimitCode, Codes(kept.Errors));

        var dropped = new OpenUIParser(BoxLibrary).Parse(Before + StatementOf(limit + 1) + After);
        Assert.False(dropped.TryGetStatement("big", out _));
        Assert.True(dropped.TryGetStatement("root", out _));
        Assert.True(dropped.TryGetStatement("after", out _));
        Assert.Contains(LimitCode, Codes(dropped.Errors));

        // The whole text is under the input limit, so that limit cannot be what reported this.
        Assert.False(dropped.Incomplete);
    }

    [Fact]
    public void A_pending_statement_past_the_limit_is_left_out_and_reported_when_the_stream_ends()
    {
        var stream = new OpenUIParser(BoxLibrary).CreateStream();
        var text = StatementOf(OpenUILimits.Default.MaxStatementChars + 1);

        var pending = stream.Append(text);
        Assert.False(pending.TryGetStatement("big", out _));

        var final = stream.Complete();
        Assert.False(final.TryGetStatement("big", out _));
        Assert.Contains(LimitCode, Codes(final.Errors));
    }

    [Fact]
    public async Task A_host_event_handler_that_throws_is_caught_and_traced()
    {
        using var listener = new RecordingListener();
        Trace.Listeners.Add(listener);
        try
        {
            await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Events = new ThrowingEvents() });
            session.Load("root = Stack([go])\ngo = Button(\"Go\", @ToAssistant(\"hi\"))");
            var button = FirstChild(session.Current.Root!, 0);
            Assert.True(button["action"].Value.TryGetAction(out var action));

            await session.RunActionAsync(action, new ActionOrigin(button, null));
            await session.WhenIdleAsync().WaitAsync(Budget);

            Assert.Contains(listener.Lines, line => line.Contains("host event handler threw", StringComparison.Ordinal));
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    [Fact]
    public async Task A_host_event_handler_that_hangs_cannot_hold_up_disposal()
    {
        var session = OpenUISession.Create(Library, new OpenUISessionOptions { Events = new HangingEvents() });
        session.Load("root = Stack([go])\ngo = Button(\"Go\", @ToAssistant(\"hi\"))");
        var button = FirstChild(session.Current.Root!, 0);
        Assert.True(button["action"].Value.TryGetAction(out var action));
        await session.RunActionAsync(action, new ActionOrigin(button, null));

        await session.DisposeAsync().AsTask().WaitAsync(Budget);
    }

    [Fact]
    public void Bad_statements_past_the_error_limit_list_the_limit_and_one_marker()
    {
        var program = new StringBuilder("root = Stack([])\n");
        for (var i = 0; i < 5000; i++)
        {
            program.Append('s').Append(i).Append(" = Nope").Append(i).Append("()\n");
        }

        var errors = OpenUIEvaluator.Evaluate(new OpenUIParser(Library).Parse(program.ToString())).Errors;

        Assert.Equal(OpenUILimits.Default.MaxErrors + 1, errors.Count);
        Assert.Equal(LimitCode, errors[^1].Code.Value);
    }

    [Fact]
    public void An_evaluator_error_list_stops_at_the_limit_with_one_marker()
    {
        var bag = new DiagnosticBag(3);
        for (var i = 0; i < 50; i++)
        {
            bag.Add(new OpenUIError { Source = OpenUIErrorSource.Runtime, Code = OpenUIErrorCode.RuntimeError, Message = "x", StatementId = "s" + i });
        }

        Assert.Equal(4, bag.Items.Count);
        Assert.Equal(LimitCode, bag.Items[^1].Error.Code.Value);
    }

    [Fact]
    public void A_bag_lists_an_error_once_and_tells_two_messages_apart()
    {
        var bag = new DiagnosticBag(10);
        OpenUIError Limit(string message) => new() { Source = OpenUIErrorSource.Runtime, Code = OpenUIErrorCode.LimitExceeded, Message = message };

        bag.Add(Limit("first"));
        bag.Add(Limit("first"));
        bag.Add(Limit("second"));

        Assert.Equal(["first", "second"], bag.Items.Select(d => d.Error.Message));
    }

    [Fact]
    public async Task A_stopped_mutation_reports_its_own_limit_when_that_limit_was_already_listed()
    {
        const string program = "$u = \"not a url\"\nsave = Mutation(\"save\", {a: \"long enough to pass the string limit\" + $u})\n"
            + "root = Stack([Button(\"a\", Action([@Run(save)])), Button(\"b\", Action([@OpenUrl($u)]))])\n";
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Limits = OpenUILimits.Default with { MaxStringLength = 8 } });
        session.Load(program);
        ValueTask<ActionOutcome> ClickAsync(int index)
        {
            session.Current.Root!["children"].Value.TryGetArray(out var items);
            items![index].TryGetComponent(out var button);
            button!["action"].Value.TryGetAction(out var action);
            return session.RunActionAsync(action!, new ActionOrigin(button, null));
        }

        Assert.Equal(LimitCode, (await ClickAsync(0)).Error?.Code.Value);
        await ClickAsync(1);
        Assert.Equal(OpenUIErrorCode.RuntimeError, Assert.Single(session.Current.Errors, error => error.Code != OpenUIErrorCode.LimitExceeded).Code);
        Assert.Equal(LimitCode, (await ClickAsync(0)).Error?.Code.Value);
    }

    [Fact]
    public void A_full_error_list_still_says_that_nothing_renders()
    {
        var options = new OpenUIParserOptions { Limits = OpenUILimits.Default with { MaxErrors = 3 } };
        var program = string.Concat(Enumerable.Range(0, 20).Select(i => $"a{i} = Nope{i}()\n"));

        var document = new OpenUIParser(Library, options).Parse(program);

        Assert.Equal(5, document.Errors.Count);
        Assert.Equal(LimitCode, document.Errors[3].Code.Value);
        Assert.Equal(OpenUIErrorCode.NoRoot, document.Errors[4].Code);
    }

    [Fact]
    public async Task Text_appended_past_the_input_limit_is_not_stored()
    {
        const string First = "root = Box(1)\n";
        var limits = new OpenUILimits { MaxInputChars = First.Length + 1 };
        await using var session = OpenUISession.Create(BoxLibrary, new OpenUISessionOptions { Limits = limits });

        session.Append(First);
        session.Append("later = 1\nmore = 2\n");
        session.EndStream();

        Assert.Contains(LimitCode, Codes(session.Current.Errors));

        // A merge reads the stored text under the same limit, so the size is the only place this shows.
        Assert.Equal(limits.MaxInputChars, session.StoredTextLength);
    }

    [Fact]
    public void Filter_charges_fuel_for_each_element_even_when_no_op_runs()
    {
        const int Elements = 1000;
        var big = "big = " + BigArray(Elements) + "\n";

        // Both read big once; the unknown op matches nothing and costs nothing, so only the loop can differ.
        var filtering = LeastFuel(big + "root = Box(@Count(@Filter(big, \"\", \"~\", 1)))\n");
        var counting = LeastFuel(big + "root = Box(@Count(big))\n");

        Assert.True(filtering >= counting + Elements, $"Filtering took {filtering}, counting {counting}.");
    }

    private static int LeastFuel(string program, OpenUILibrary? library = null)
    {
        var (low, high) = (1, OpenUILimits.Default.Fuel);
        while (low < high)
        {
            var fuel = low + ((high - low) / 2);
            var errors = OpenUIEvaluator.Evaluate(new OpenUIParser(library ?? BoxLibrary).Parse(program), limits: new OpenUILimits { Fuel = fuel }).Errors;
            if (Codes(errors).Contains(LimitCode))
            {
                low = fuel + 1;
            }
            else
            {
                high = fuel;
            }
        }

        return low;
    }

    [Fact]
    public void A_pattern_has_no_clock_in_its_answer()
    {
        Assert.Equal(System.Text.RegularExpressions.Regex.InfiniteMatchTimeout, EcmaRegex.Build("^a$")!.MatchTimeout);
    }

    [Theory]
    [InlineData("^a*$", 3)]
    [InlineData("(a|b){3}", 6)]
    [InlineData("((a){3}){3}", 9)]
    [InlineData("a{2,5}", 5)]
    [InlineData("a{9,}", 9)]
    [InlineData("a{0}", 1)]
    [InlineData("[a-z\\]]{4}", 4)]
    [InlineData("\\{{3}", 3)]
    [InlineData("a{x}", 4)]
    [InlineData("(a{4}", 4)]
    [InlineData("((a{1000}){1000}){1000}", EcmaRegex.MaxPatternSize + 1)]
    [InlineData("a{99999999999999999999}", EcmaRegex.MaxPatternSize + 1)]
    public void A_patterns_size_is_what_its_counted_repetitions_expand_to(string pattern, long size)
    {
        Assert.Equal(size, EcmaRegex.Size(pattern));
    }

    [Fact]
    public void A_pattern_that_expands_past_the_limit_is_not_compiled()
    {
        Assert.NotNull(EcmaRegex.Build("a{2000}"));
        Assert.Null(EcmaRegex.Build("(a{1000}){11}"));
    }

    [Fact]
    public void A_match_costs_its_text_times_its_size()
    {
        var small = EcmaRegex.MatchSteps("a", 1000) - EcmaRegex.MatchSteps("a", 0);
        var wide = EcmaRegex.MatchSteps("a{2000}", 1000) - EcmaRegex.MatchSteps("a{2000}", 0);

        Assert.True(small >= 64_000, $"small {small}");
        Assert.True(wide >= 1_000_000, $"wide {wide}");
        Assert.True(EcmaRegex.MatchSteps("a{2000}", 0) >= 128_000);
    }

    [Fact]
    public void A_value_longer_than_the_matched_limit_fails_its_pattern_and_is_reported()
    {
        var rules = OuiObject.Create([KeyValuePair.Create("pattern", (OuiValue)"^a*$")]);
        Assert.Null(ValidationRules.Check(rules, new string('a', EcmaRegex.MaxMatchedChars)));
        Assert.Equal("Invalid format", ValidationRules.Check(rules, new string('a', EcmaRegex.MaxMatchedChars + 1)));
    }

    [Fact]
    public async Task A_form_with_a_value_too_long_for_its_pattern_lists_the_limit()
    {
        await using var session = OpenUISession.Create(Library);
        session.Load("$v = \"" + new string('a', EcmaRegex.MaxMatchedChars + 1) + "\"\nroot = Form(\"F\", [Input(\"f\", \"p\", $v, {pattern: \"^a*$\"})])\n");

        var validation = session.ValidateForm("F");

        Assert.Equal("Invalid format", Assert.Single(validation.Errors).Message);
        Assert.Contains(LimitCode, Codes(session.Current.Errors));
    }

    [Fact]
    public async Task A_pattern_pays_for_the_text_it_reads()
    {
        const string pattern = "^a*$";
        var program = "$v = \"" + new string('a', 1000) + "\"\nroot = Form(\"F\", [Input(\"f\", \"p\", $v, {pattern: \"" + pattern + "\"})])\n";
        var price = (int)EcmaRegex.MatchSteps(pattern, 1000);

        await using var enough = OpenUISession.Create(Library, new OpenUISessionOptions { Limits = new OpenUILimits { Fuel = price + 100_000 } });
        enough.Load(program);
        Assert.DoesNotContain(enough.ValidateForm("F").Errors, error => error.Message.Contains("fuel", StringComparison.Ordinal));

        await using var poor = OpenUISession.Create(Library, new OpenUISessionOptions { Limits = new OpenUILimits { Fuel = price - 1 } });
        poor.Load(program);
        Assert.Contains(poor.ValidateForm("F").Errors, error => error.Message.Contains("fuel", StringComparison.Ordinal));
    }

    [Fact]
    public async Task One_giant_statement_read_a_character_at_a_time_is_not_parsed_on_every_character()
    {
        var text = "root = Box(\"" + new string('x', OpenUILimits.Default.MaxStatementChars - 20);

        var document = await Bounded(() =>
        {
            var stream = new OpenUIParser(BoxLibrary).CreateStream();
            foreach (var c in text)
            {
                stream.Append(new ReadOnlySpan<char>(in c));
            }

            return stream.Complete();
        });

        Assert.True(document.TryGetStatement("root", out _));
    }

    // Levels are counted with the innermost value as one, so 127 brackets around a value are 128 levels.
    public static TheoryData<string, string> NestingForms() => new()
    {
        { "(", ")" },
        { "[", "]" },
        { "{a: ", "}" },
        { "Box(", ")" },
        { "!", "" },
        { "true ? 1 : ", "" },
    };

    [Theory]
    [MemberData(nameof(NestingForms))]
    public void A_statement_of_128_levels_is_kept_and_129_is_dropped(string open, string close)
    {
        static OpenUIDocument Parse(string open, string close, int brackets) =>
            new OpenUIParser(BoxLibrary).Parse("a = " + Repeat(open, brackets) + "1" + Repeat(close, brackets) + "\nroot = Box(1)\n");

        var kept = Parse(open, close, 127);
        Assert.True(kept.TryGetStatement("a", out _));
        Assert.DoesNotContain(LimitCode, Codes(kept.Errors));

        var dropped = Parse(open, close, 128);
        Assert.False(dropped.TryGetStatement("a", out _));
        Assert.Contains(LimitCode, Codes(dropped.Errors));
        Assert.True(dropped.TryGetStatement("root", out _));
    }

    [Fact]
    public void A_reference_chain_resolves_up_to_the_evaluation_depth_and_stops_one_past_it()
    {
        static OpenUIErrorList Run(int links)
        {
            var program = new StringBuilder("root = Box(r").Append(links).Append(")\nr0 = \"end\"\n");
            for (var i = 1; i <= links; i++)
            {
                program.Append('r').Append(i).Append(" = r").Append(i - 1).Append('\n');
            }

            return OpenUIEvaluator.Evaluate(new OpenUIParser(BoxLibrary).Parse(program.ToString())).Errors;
        }

        // Three levels are the root statement, its component and its prop; each link is one more.
        var most = OpenUILimits.Default.MaxEvalDepth - 3;
        Assert.DoesNotContain(LimitCode, Codes(Run(most)));
        Assert.Contains(LimitCode, Codes(Run(most + 1)));
    }

    [Fact]
    public void A_literal_value_of_64_levels_is_kept_and_65_is_dropped_at_evaluation()
    {
        static OpenUIErrorList Run(int arrays) =>
            OpenUIEvaluator.Evaluate(new OpenUIParser(BoxLibrary).Parse("root = Box(" + Repeat("[", arrays) + "1" + Repeat("]", arrays) + ")\n")).Errors;

        Assert.DoesNotContain(LimitCode, Codes(Run(63)));
        Assert.Contains(LimitCode, Codes(Run(64)));
    }

    private sealed class RecordingListener : TraceListener
    {
        private readonly List<string> _lines = [];

        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (_lines)
                {
                    return [.. _lines];
                }
            }
        }

        public override void Write(string? message)
        {
            lock (_lines)
            {
                _lines.Add(message ?? "");
            }
        }

        public override void WriteLine(string? message) => Write(message);
    }

    private sealed class HangingEvents : IOpenUIHostEventHandler
    {
        // Ignores its token on purpose: only the session's own lifetime can let disposal go on.
        public ValueTask OnEventAsync(OpenUIHostEvent hostEvent, CancellationToken ct) =>
            new(new TaskCompletionSource().Task);
    }
}
