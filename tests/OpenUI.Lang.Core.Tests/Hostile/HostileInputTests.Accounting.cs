using System.Text;
using OpenUI.Lang.Runtime;

namespace OpenUI.Lang.Core.Tests.Hostile;

/// <summary>
/// Work and size accounting as a class: whatever scales with an attacker-sized string or collection
/// pays before it runs, and every text a composite holds is in its measure. Each row runs in a child
/// with a 1 GiB heap and must end in a limit diagnostic within the budget.
/// </summary>
public partial class HostileInputTests
{
    private static readonly TimeSpan SecondsBudget = TimeSpan.FromSeconds(20);

    private static string LongName => new('z', 200_000);

    private static string HalfMillion => "ha = \"" + Repeat("ab", 100_000) + "\"\nhay = ha + ha + ha + ha + ha\n";

    public static TheoryData<string, string, string, string> ClassOfWork()
    {
        const string Fuel = "ran out of fuel";
        const string Text = "characters";
        const string Steps = "more than 1000 steps";
        const string Keep = "root = Stack([Text(@Count(arr) + \"\")])\n";
        var big = "big = " + BigArray(50_000) + "\n";
        var name = LongName;
        var key = "o = {\"" + name + "\": 1}\n";
        var nineKeys = "o = {" + string.Join(", ", Enumerable.Range(0, 9).Select(i => $"k{i}: {i}")) + "}\n";
        var nested = "n = " + BigArray(660) + "\nm = " + BigArray(1000) + "\n";
        var items = "p = [" + Repeat("o ", 9000) + "]\nn = " + BigArray(20) + "\n";
        var deepEach = string.Concat(Enumerable.Range(0, 100).Select(i => $"@Each([1], \"a{i}\", ")) + "@Each(big, \"i\", @Set($x, 1))" + Repeat(")", 100);
        var deferred = string.Concat(Enumerable.Range(0, 8).Select(i => $"d{i} = {deepEach}\n"));
        var count = "root = Stack([Text(\"\" + @Count(e))])\n";
        var marks = "a = \"" + Repeat("a\\u0301", 30_000) + "\"\nc = \"" + Repeat("\\u00e1", 40_000) + "\"\nb = a + a + a + a + a + a + a + a\nd = c + c + c + c + c + c\n";
        return new TheoryData<string, string, string, string>
        {
            // Text a composite holds.
            { "evaluate", "object keys fan out", Text, key + "a = [" + Repeat("o ", 2000) + "]\nroot = Stack([Text(\"\" + @Count(a))])\n" },
            { "session", "object keys fan out into tool arguments", Text, key + "a = [" + Repeat("o ", 40_000) + "]\nq = Query(\"t\", {a: a}, {})\nroot = Stack([Text(\"p\")])\n" },
            { "evaluate", "a statement name in every component", Text, "n = " + BigArray(40_000) + "\n" + name + " = @Each(n, \"i\", Text(\"y\"))\nroot = Stack(" + name + ")\n" },
            { "evaluate", "a binding name in every component", Text, "n = " + BigArray(40_000) + "\nroot = Stack(@Each(n, \"i\", Input(\"f\", \"p\", $" + name + ")))\n" },
            { "evaluate", "a state name in every action step", Text, "n = " + BigArray(40_000) + "\nroot = Stack([Text(\"\" + @Count(arr))])\narr = @Each(n, \"i\", @Set($" + name + ", 1))\n" },

            // Fan-out of steps.
            { "evaluate", "action groups multiply", Steps, "s = @Set($x, 1)\na = Action([" + Repeat("s ", 40_000) + "])\nb = Action([" + Repeat("a ", 40_000) + "])\nroot = Stack([Button(\"go\", b)])\n" },
            { "evaluate", "an action longer than the step limit", Steps, "s = @Set($x, 1)\na = Action([" + Repeat("s ", 20_000) + "])\nroot = Stack([Button(\"go\", a)])\n" },
            { "evaluate", "each deferred step copies its loop scope", Fuel, Keep.Replace("arr", "d0") + "$x = 0\n" + big + deferred },

            // Reads of a long name, once each.
            { "evaluate", "a long member read", Fuel, nineKeys + nested + "e = @Each(n, \"i\", @Count(@Each(m, \"j\", o." + name + ")))\n" + count },
            { "evaluate", "a long member plucked", Fuel, nineKeys + nested + "p = [" + Repeat("o ", 660) + "]\ne = @Each(n, \"i\", @Count(p." + name + "))\n" + count },
            { "evaluate", "a long index read", Fuel, nineKeys + nested + "k = \"" + name + "\"\ne = @Each(n, \"i\", @Count(@Each(m, \"j\", o[k])))\n" + count },
            { "evaluate", "a long path read per item", Fuel, nineKeys + items + "e = @Each(n, \"i\", @Count(@Filter(p, \"" + name + "\", \"==\", 1)))\n" + count },
            { "evaluate", "a long sort path per comparison", Fuel, nineKeys + items + "e = @Each(n, \"i\", @Count(@Sort(p, \"" + name + "\")))\n" + count },
            { "evaluate", "a path of many parts", Fuel, "p = " + BigArray(50_000) + "\nn = " + BigArray(50) + "\ne = @Each(n, \"i\", @Count(@Filter(p, \"" + Repeat("a.", 120_000) + "\", \"==\", 1)))\n" + count },
            { "evaluate", "a long key built per iteration", Fuel, nested + "e = @Each(n, \"i\", @Count(@Each(m, \"j\", @Count({\"" + name + "\": j}))))\n" + count },
            { "evaluate", "a long statement reference", Fuel, nested + name + " = 1\ne = @Each(n, \"i\", @Count(@Each(m, \"j\", " + name + ")))\n" + count },
            { "evaluate", "a long state reference", Fuel, nested + "e = @Each(n, \"i\", @Count(@Each(m, \"j\", $" + name + ")))\n" + count },
            { "evaluate", "reports hash the statement name", Fuel, "n = " + BigArray(50_000) + "\nroot = Stack([Text(\"\" + @Count(" + name + "))])\n" + name + " = @Each(n, \"i\", @Count([Stack(i)]))\n" },

            // Search and comparison.
            { "evaluate", "contains, half the text as the needle", Fuel, HalfMillion + "needle = ha + \"cd\" + ha + \"ab\"\nitems = [" + Repeat("hay ", 14) + "]\nf = @Filter(items, \"\", \"contains\", needle)\nroot = Stack([Text(\"\" + @Count(f))])\n" },
            { "evaluate", "contains, a long needle", Fuel, HalfMillion + "needle = \"" + Repeat("ab", 60_000) + "cb\"\nitems = [" + Repeat("hay ", 16) + "]\nf = @Filter(items, \"\", \"contains\", needle)\nroot = Stack([Text(\"\" + @Count(f))])\n" },
            { "evaluate", "sort collation on long strings", Fuel, HalfMillion + "items = [" + string.Join(" ", Enumerable.Range(0, 10).Select(i => $"hay + \"{(char)('b' + (i * 7 % 20))}\"")) + "]\nn = " + BigArray(50) + "\ne = @Each(n, \"i\", @Count(@Sort(items)))\n" + count },
            { "evaluate", "sort collation on combining marks", Fuel, marks + "items = [" + string.Join(" ", Enumerable.Range(0, 20).Select(i => $"{(i % 2 == 0 ? 'd' : 'b')} + \"{(char)('b' + (i * 7 % 20))}\"")) + "]\nn = " + BigArray(50) + "\ne = @Each(n, \"i\", @Count(@Sort(items)))\n" + count },
            { "evaluate", "a long operator per item", Fuel, "p = " + BigArray(50_000) + "\nn = " + BigArray(50) + "\ne = @Each(n, \"i\", @Count(@Filter(p, \"\", \"" + name + "\", 1)))\n" + count },
        };
    }

    [Theory]
    [MemberData(nameof(ClassOfWork))]
    public async Task What_scales_with_a_hostile_size_pays_for_it_or_stops(string scenario, string shape, string expected, string program)
    {
        var report = await HostileProbe.SurvivesAsync(scenario, program, SecondsBudget);
        Assert.True(HostileProbe.Codes(report).Contains(LimitCode), $"{shape}: {HostileProbe.Messages(report)}");
        Assert.Contains(expected, HostileProbe.Messages(report));
    }

    public static TheoryData<string, string, string> OneClick()
    {
        var steps = BigArray(900);
        var manyKeys = new StringBuilder();
        for (int k = 0, statement = 0; k < 100_000; k += 15_000, statement++)
        {
            manyKeys.Append($"x{statement} = [").AppendJoin(' ', Enumerable.Range(k, Math.Min(15_000, 100_000 - k)).Select(i => "$k" + i)).Append("]\n");
        }

        var keyUses = string.Join(" ", Enumerable.Range(0, 7).Select(i => $"Text(\"\" + @Count(x{i}))"));
        var sums = string.Concat(Enumerable.Range(0, 9).Select(i => $"h{i} = @Sum(w)\n"));
        var sumUses = string.Join(" ", Enumerable.Range(0, 9).Select(i => $"Text(\"\" + h{i} + $a)"));
        var w1 = "w1 = [" + Repeat("1 ", 90_000) + "]\nw2 = [" + Repeat("1 ", 89_999) + "2]\n";
        var targets = string.Join(", ", Enumerable.Range(0, 25_000).Select(i => "$k" + i));
        return new TheoryData<string, string, string>
        {
            { "click-events", "each event copies every state key", manyKeys + "r = " + steps + "\na = Action(@Each(r, \"i\", @ToAssistant(\"m\")))\nroot = Stack([Button(\"go\", a), " + keyUses + "])\n" },
            { "click", "a state write compares a huge value", "$g = false\n$a = null\n" + w1 + "r = " + BigArray(400) + "\nb = Action(@Each(r, \"i\", Action([@Set($a, $g ? w1 : 0), @Set($a, $g ? w2 : 0)])))\na = Action([@Set($g, true), b])\nroot = Stack([Button(\"go\", a)])\n" },
            { "click-errors", "an errors handler evaluates at each step", "$a = 0\nw = " + BigArray(99_000) + "\nr = " + steps + "\na = Action(@Each(r, \"i\", @Set($a, i)))\n" + sums + "root = Stack([Button(\"go\", a), " + sumUses + "])\n" },
            { "click-reader", "a render at each step", "$a = 0\nw = " + BigArray(99_000) + "\nr = " + steps + "\na = Action(@Each(r, \"i\", @Set($a, i)))\n" + sums + "root = Stack([Button(\"go\", a), " + sumUses + "])\n" },
            { "click-reader", "a heavy statement rendered at each step", "$x = 0\nn = " + BigArray(1200) + "\nheavy = @Each(n, \"i\", @Sum(n))\ns = @Set($x, $x + 1)\na = Action([" + Repeat("s ", 900) + "])\nroot = Stack([Text(\"c \" + @Count(heavy) + $x), Button(\"go\", a)])\n" },
            { "click", "a reset of many names at each step", "r = " + BigArray(60) + "\nz = @Reset(" + targets + ")\na = Action(@Each(r, \"i\", z))\nroot = Stack([Button(\"go\", a)])\n" },
        };
    }

    [Theory]
    [MemberData(nameof(OneClick))]
    public async Task One_click_pays_for_what_each_of_its_steps_costs(string scenario, string shape, string program)
    {
        var report = await HostileProbe.SurvivesAsync(scenario, program, SecondsBudget);
        Assert.True(HostileProbe.Messages(report).Contains("ran out of fuel", StringComparison.Ordinal), $"{shape}: {HostileProbe.Messages(report)}");
    }

    [Fact]
    public async Task One_click_cannot_write_to_a_backend_without_limit()
    {
        var program = "m = Mutation(\"write\", {}, {})\nr = " + BigArray(900) + "\na = Action(@Each(r, \"i\", @Run(m)))\nroot = Stack([Button(\"go\", a)])\n";
        var report = await HostileProbe.SurvivesAsync("click", program, SecondsBudget);
        Assert.Contains(LimitCode, HostileProbe.Codes(report));
        Assert.Equal(new OpenUILimits().MaxMutationsPerAction, report["calls"]!.GetValue<int>());
    }

    [Fact]
    public async Task State_defaults_that_pass_the_state_limit_are_dropped_and_a_click_survives_them()
    {
        var aliases = string.Concat(Enumerable.Range(0, 100).Select(i => $"$a{i} = big\n"));
        var program = "s = \"" + new string('x', 200_000) + "\"\nbig = [" + Repeat("s ", 50) + "]\n" + aliases + "root = Stack([Button(\"go\", @ToAssistant(\"m\"))])\n";
        var report = await HostileProbe.SurvivesAsync("click", program, SecondsBudget);
        Assert.Contains("would make the state hold more than", HostileProbe.Messages(report));
    }

    [Fact]
    public async Task A_click_pays_for_the_context_and_state_of_each_event_it_sends()
    {
        var events = new CountingEvents();
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Events = events });
        session.Load("s = \"" + new string('x', 200_000) + "\"\nbig = [" + Repeat("s ", 50) + "]\n$a = big\nroot = Stack([Button(\"go\", @ToAssistant(\"m\", big))])\n");
        var (button, action) = FirstAction(session);

        var outcome = await session.RunActionAsync(action, new ActionOrigin(button, null));
        await session.WhenIdleAsync();

        Assert.Equal(ActionOutcomeKind.Stopped, outcome.Kind);
        Assert.Equal(0, events.Count);
        Assert.Contains(LimitCode, Codes(session.Current.Errors));
    }

    [Fact]
    public async Task A_step_over_a_limit_ends_the_action_before_the_next_one_runs()
    {
        var limits = OpenUILimits.Default with { MaxTextChars = 1000 };
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Limits = limits });
        session.Load("root = Stack([Button(\"go\", Action([@Set($a, \"" + new string('x', 200) + "\"), @Set($b, 1)]))])\n");
        session.State.Set("$held", new string('x', 900));
        var (button, action) = FirstAction(session);

        var outcome = await session.RunActionAsync(action, new ActionOrigin(button, null));

        Assert.Equal(ActionOutcomeKind.Stopped, outcome.Kind);
        Assert.Equal(0, outcome.StepsRun);
        Assert.True(session.State.Get("$b").IsNull);
    }

    // Each event already pays for $s as page state. Its message pays again, so five events fit the limit
    // with one charge each and only three with two.
    [Fact]
    public async Task A_click_pays_for_the_message_of_each_event_it_sends()
    {
        var limits = OpenUILimits.Default with { MaxTextChars = 6500 };
        var events = new CountingEvents();
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Limits = limits, Events = events });
        session.Load("r = [1, 2, 3, 4, 5]\na = Action(@Each(r, \"i\", @ToAssistant($s)))\nroot = Stack([Button(\"go\", a)])\n");
        session.State.Set("$s", new string('x', 1000));
        var (button, action) = FirstAction(session);

        var outcome = await session.RunActionAsync(action, new ActionOrigin(button, null));
        await session.WhenIdleAsync();

        Assert.Equal(ActionOutcomeKind.Stopped, outcome.Kind);
        Assert.Equal(3, events.Count);
        Assert.Contains(LimitCode, Codes(session.Current.Errors));
    }

    [Theory]
    [InlineData("@Set($a, $x + $x)")]
    [InlineData("@ToAssistant($x + $x)")]
    [InlineData("@OpenUrl(\"https://\" + $x + $x)")]
    [InlineData("@Set($a, m)")]
    [InlineData("@Set($a, [m, 2])")]
    [InlineData("@Set($a, n)")]
    [InlineData("@ToAssistant(m)")]
    [InlineData("@ToAssistant(\"hi\", m)")]
    [InlineData("@OpenUrl(m)")]
    public async Task A_step_whose_expression_hits_a_limit_ends_the_action_before_the_next_one_runs(string step)
    {
        var limits = OpenUILimits.Default with { MaxStringLength = 20 };
        var events = new CountingEvents();
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Limits = limits, Events = events });
        session.Load("m = $x + $x\nn = [m, 2]\nroot = Stack([Button(\"go\", Action([" + step + ", @Set($b, 1)]))])\n");
        var (button, action) = FirstAction(session);
        session.State.Set("$x", "aaaaaaaaaaaa");
        session.State.Set("$a", "kept");

        var outcome = await session.RunActionAsync(action, new ActionOrigin(button, null));

        Assert.Equal(ActionOutcomeKind.Stopped, outcome.Kind);
        Assert.Equal(0, outcome.StepsRun);
        Assert.Equal(LimitCode, outcome.Error!.Code.Value);
        Assert.True(session.State.Get("$b").IsNull);
        Assert.Equal("kept", session.State.Get("$a").ToString());
    }

    [Fact]
    public async Task A_mutation_whose_arguments_read_a_dropped_statement_never_calls_its_tool()
    {
        var calls = 0;
        var tools = new OpenUITools().Add("write", (_, _) =>
        {
            Interlocked.Increment(ref calls);
            return ValueTask.FromResult(Values.OuiValue.Null);
        });
        var limits = OpenUILimits.Default with { MaxStringLength = 20 };
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools, Limits = limits });
        session.Load("m = $x + $x\nsave = Mutation(\"write\", {title: m})\nroot = Stack([Button(\"go\", Action([@Run(save), @Set($b, 1)]))])\n");
        var (button, action) = FirstAction(session);
        session.State.Set("$x", "aaaaaaaaaaaa");

        var outcome = await session.RunActionAsync(action, new ActionOrigin(button, null));
        await session.WhenIdleAsync();

        Assert.Equal(ActionOutcomeKind.Stopped, outcome.Kind);
        Assert.Equal(LimitCode, outcome.Error!.Code.Value);
        Assert.Equal(0, Volatile.Read(ref calls));
        Assert.True(session.State.Get("$b").IsNull);
    }

    [Fact]
    public async Task Queries_sharing_a_pass_all_skip_a_statement_dropped_for_a_limit()
    {
        var (tools, calls) = CountingTools();
        var limits = OpenUILimits.Default with { MaxStringLength = 20 };
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools, Limits = limits });
        session.Load("$x = \"aaaaaaaaaaaa\"\nm = $x + $x\nn = [m, 2]\nfirst = Query(\"t\", {v: m})\nsecond = Query(\"t\", {v: n})\nfree = Query(\"t\", {v: 1})\nroot = Stack([Text(\"x\")])\n");
        await session.WhenIdleAsync();

        Assert.Equal(1, calls());
        Assert.Contains(LimitCode, Codes(session.Current.Errors));
    }

    [Fact]
    public async Task A_default_dropped_for_the_state_limit_reads_back_as_null()
    {
        var limits = OpenUILimits.Default with { MaxTextChars = 300 };
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Limits = limits });
        var text = new string('x', 120);
        session.Load($"$a = \"{text}\"\n$b = \"{text}\"\n$c = \"{text}\"\nroot = Stack([Button(\"go\", @ToAssistant(\"m\"))])\n");

        Assert.Equal(text, session.State.Get("$a").ToString());
        Assert.True(session.State.Get("$c").IsNull);
        Assert.Contains(LimitCode, Codes(session.Current.Errors));
    }

    private static string RegexRounds(string shape, int textLength)
    {
        var random = new Random(7);
        var text = new string(Enumerable.Range(0, textLength).Select(_ => random.Next(2) == 0 ? 'a' : 'b').ToArray());
        return string.Join("\n---\n", Enumerable.Range(0, 4).Select(round =>
        {
            var inputs = Enumerable.Range(0, 70).Select(i => $"i{i} = Input(\"f{i}\", \"p\", $v, {{pattern: \"{shape}c{round}x{i}\"}})\n");
            return $"$v = \"{text}\"\n{string.Concat(inputs)}root = Form(\"F\", [{string.Join(", ", Enumerable.Range(0, 70).Select(i => "i" + i))}])\n";
        }));
    }

    [Theory]
    [InlineData("(a|b)*a(a|b){23}", 200_000)]
    [InlineData("(a|b)*a(a|b){23}", 16_000)]
    [InlineData("(a|b)*a(a|b){23}", 1000)]
    [InlineData("(a{0,50}b?){50}", 16_000)]
    [InlineData("(a{0,50}b?){50}", 1000)]
    [InlineData("(a{0,50}b?){50}", 100)]
    public async Task Hostile_patterns_are_bounded_and_leave_nothing_behind(string shape, int textLength)
    {
        var report = await HostileProbe.SurvivesAsync("validate-memory", RegexRounds(shape, textLength), SecondsBudget);
        Assert.InRange(report["retainedMb"]!.GetValue<int>(), 0, 16);
        Assert.Contains(LimitCode, HostileProbe.Codes(report));
    }

    [Fact]
    public async Task A_program_of_refreshing_queries_cannot_become_a_load_generator()
    {
        // Three seconds of ticks in the probe: at most four windows of fetches, however many queries there are.
        var queries = string.Concat(Enumerable.Range(0, 9000).Select(i => $"q{i} = Query(\"t\", {{}}, {{}}, 1)\n"));
        var report = await HostileProbe.SurvivesAsync("timer", queries + "root = Stack([Text(\"p\")])\n", SecondsBudget);

        Assert.Contains(LimitCode, HostileProbe.Codes(report));
        Assert.InRange(report["calls"]!.GetValue<int>(), 1, 5 * OpenUILimits.Default.MaxSessionFetchesPerSecond);
    }

    [Fact]
    public async Task Tool_arguments_keep_no_text_alive_between_refreshes()
    {
        // Each key would hold 60 MB of escapes twice over: the first pass shares one budget, and each tick has its own.
        var chunk = "c = \"" + Repeat("\\u0001", 30_000) + "\"\nd = c + c\ne = d + d\nf = e + e\ng = f + f\ng2 = g + g\na = [" + Repeat("g2 ", 10) + "]\n";
        var queries = string.Concat(Enumerable.Range(0, 16).Select(i => $"q{i} = Query(\"t\", {{a: a}}, {{}}, 1)\n"));
        var report = await HostileProbe.SurvivesAsync("timer", chunk + queries + "root = Stack([Text(\"p\")])\n", SecondsBudget);
        Assert.Contains(LimitCode, HostileProbe.Codes(report));
    }

    // Each row is a program with a name in it. Making the name 64,000 characters long must cost at
    // least the steps listed, or an operation has stopped paying for what it reads.
    public static TheoryData<string, string, int> PricesOfALongName() => new()
    {
        { "a member read", "o = {a: 1}\nroot = Box(o.NAME)\n", 1000 },
        { "a pluck, per item", "o = {a: 1}\np = [o, o, o, o]\nroot = Box(@Count(p.NAME))\n", 4000 },
        { "an index read", "o = {a: 1}\nk = \"NAME\"\nroot = Box(o[k])\n", 1000 },
        { "a key built", "root = Box({NAME: $a})\n", 1000 },
        { "a statement reference", "NAME = 1\nroot = Box(NAME)\n", 3000 },
        { "a state reference", "root = Box($NAME + 1)\n", 1000 },
        { "a filter path, per item", "p = [1, 2, 3, 4]\nroot = Box(@Count(@Filter(p, \"NAME\", \"==\", 1)))\n", 4000 },
        { "a sort path, per comparison", "p = [1, 2, 3, 4]\nroot = Box(@Count(@Sort(p, \"NAME\")))\n", 6000 },
        { "a collation, per comparison", "a = \"NAME\"\np = [" + Repeat("a, ", 63) + "a]\nroot = Box(@Count(@Sort(p, \"\")))\n", 1_000_000 },
    };

    [Theory]
    [MemberData(nameof(PricesOfALongName))]
    public void Reading_a_long_name_costs_its_length(string shape, string program, int steps)
    {
        var longer = LeastFuel(program.Replace("NAME", new string('z', 64_000), StringComparison.Ordinal));
        var shorter = LeastFuel(program.Replace("NAME", "z", StringComparison.Ordinal));
        Assert.True(longer - shorter >= steps, $"{shape}: {shorter} to {longer}.");
    }

    [Fact]
    public void A_binding_and_a_field_error_pay_for_the_long_names_they_read()
    {
        const string Program = "root = Stack([NAME])\nNAME = Input(\"f\", \"p\", $NAME)\n";
        var name = new string('z', 64_000);
        var longer = LeastFuel(Program.Replace("NAME", name, StringComparison.Ordinal), Library);
        var shorter = LeastFuel(Program.Replace("NAME", "z", StringComparison.Ordinal), Library);
        Assert.True(longer - shorter >= 4000, $"{shorter} to {longer}.");

        const string Typed = "n = [1, 2, 3, 4]\nNAME = @Each(n, \"i\", @Count([Stack(i)]))\nroot = Stack([Text(\"\" + @Count(NAME))])\n";
        var reported = LeastFuel(Typed.Replace("NAME", name, StringComparison.Ordinal), Library);
        var quiet = LeastFuel(Typed.Replace("NAME", "z", StringComparison.Ordinal), Library);
        Assert.True(reported - quiet >= 4000 + 3000, $"{quiet} to {reported}.");
    }

    [Fact]
    public void A_path_pays_for_each_part_it_splits_into()
    {
        var dots = LeastFuel("root = Box(@Count(@Filter([], \"" + new string('.', 64_000) + "\", \"==\", 1)))\n");
        var letters = LeastFuel("root = Box(@Count(@Filter([], \"" + new string('z', 64_000) + "\", \"==\", 1)))\n");
        Assert.True(dots - letters >= 64_000, $"{letters} to {dots}.");
    }

    [Fact]
    public void Contains_pays_for_the_length_of_the_text_times_the_length_of_the_part()
    {
        var text = new string('a', 64_000);
        var worst = LeastFuel($"root = Box(@Filter([\"{text}\"], \"\", \"contains\", \"{new string('a', 32_000)}\"))\n");
        var best = LeastFuel($"root = Box(@Filter([\"{text}\"], \"\", \"contains\", \"a\"))\n");
        Assert.True(worst - best >= 32_000 * 32_000 / 4096, $"{best} to {worst}.");
    }

    [Fact]
    public void A_deferred_step_pays_for_the_loop_scope_it_copies()
    {
        var thousand = BigArray(1000);
        var open = string.Concat(Enumerable.Range(0, 50).Select(i => $"@Each([1], \"a{i}\", "));
        var deep = LeastFuel($"root = Box({open}@Each({thousand}, \"i\", @Set($x, 1)){Repeat(")", 50)})\n");
        var flat = LeastFuel($"root = Box(@Each({thousand}, \"i\", @Set($x, 1)))\n");
        Assert.True(deep - flat >= 50 * 1000, $"{flat} to {deep}.");
    }

    [Fact]
    public void An_action_group_pays_for_the_steps_it_copies()
    {
        const string Action = "s = @Set($x, 1)\na = Action([s, s, s, s, s, s, s, s, s, s])\n";
        var twice = LeastFuel(Action + "root = Box(Action([a, a]))\n");
        var once = LeastFuel(Action + "root = Box(Action([a]))\n");
        Assert.True(twice - once >= 10, $"{once} to {twice}.");
    }

    public static TheoryData<string, string, int> TextInAMeasure() => new()
    {
        { "an object key", "{zzzz: 1}", 4 },
        { "a statement name in a component", "zzzz", 4 + "Box".Length + "value".Length },
        { "a state name in a set", "@Set($zzzz, 1)", 5 },
        { "a state name in a reset", "@Reset($zzzz)", 5 },
        { "a statement name in a run", "@Run(zzzz)", 4 },
    };

    [Theory]
    [MemberData(nameof(TextInAMeasure))]
    public void Every_text_a_composite_holds_is_in_its_measure(string shape, string expression, int atLeast)
    {
        var program = $"zzzz = Box(1)\nroot = Box({expression})\n";
        var render = OpenUIEvaluator.Evaluate(new OpenUIParser(BoxLibrary).Parse(program));
        Assert.True(render.Root!["value"].Value.Measure.Chars >= atLeast, shape);
    }

    [Fact]
    public void A_components_measure_counts_each_text_it_holds_exactly()
    {
        var render = OpenUIEvaluator.Evaluate(new OpenUIParser(BoxLibrary).Parse("zzzz = Box(1)\nroot = Box(zzzz)\n"));
        render.Root!["value"].Value.TryGetComponent(out var box);

        var expected = "Box".Length + "zzzz".Length + box!.Id.Value.Length + "value".Length;
        Assert.Equal(expected, ((Values.OuiValue)box).Measure.Chars);
    }

    [Fact]
    public void The_key_for_tool_arguments_does_not_grow_with_them()
    {
        Values.OuiValue Text(int length) => new string('x', length);

        Assert.Equal(OpenUISession.ArgumentsKey(Text(10)).Length, OpenUISession.ArgumentsKey(Text(100_000)).Length);
        Assert.NotEqual(OpenUISession.ArgumentsKey(Text(10)), OpenUISession.ArgumentsKey(Text(11)));
    }

    // The string limit is 10. The text budget is just under what the string would cost, so which
    // message comes tells whether the length check fired before the text was paid for.
    [Theory]
    [InlineData("a pair adds up past the limit", "s = \"12345678\"\nt = s + s\n", 15)]
    [InlineData("a join adds up past the limit", "s = \"12345678\"\na = [s, s, s]\nt = \"\" + a\n", 25)]
    [InlineData("numbers join longer than the estimate", "a = [1234567890, 1234567890]\nt = \"\" + a\n", 1000)]
    public void A_string_too_long_to_keep_is_refused_before_it_costs_text(string shape, string statements, int textBudget)
    {
        var limits = OpenUILimits.Default with { MaxStringLength = 10, MaxTextChars = textBudget };
        var render = OpenUIEvaluator.Evaluate(new OpenUIParser(Library).Parse(statements + "root = Stack([Text(t)])\n"), limits: limits);
        Assert.True(render.Errors.Any(error => error.Message.StartsWith("A string is longer", StringComparison.Ordinal)), shape + ": " + string.Join("; ", render.Errors.Select(e => e.Message)));
    }

    [Fact]
    public void A_binding_name_is_in_the_measure_of_its_component()
    {
        var render = OpenUIEvaluator.Evaluate(new OpenUIParser(Library).Parse("root = Stack([Input(\"f\", \"p\", $" + new string('z', 500) + ")])\n"));
        Assert.True(((Values.OuiValue)render.Root!).Measure.Chars >= 500);
    }

    [Fact]
    public async Task An_event_pays_for_each_state_key_it_copies()
    {
        var limits = OpenUILimits.Default with { Fuel = 30_000 };
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Limits = limits, Events = new HostileProbe.IgnoredEvents() });
        session.Load("root = Stack([Button(\"b\", @ToAssistant(\"m\"))])\n");
        for (var i = 0; i < 5000; i++)
        {
            session.State.Set("$k" + i, 1.0);
        }

        var (button, action) = FirstAction(session);

        await session.RunActionAsync(action, new ActionOrigin(button, null));

        Assert.Contains(LimitCode, Codes(session.Current.Errors));
    }

    [Fact]
    public async Task A_long_program_stops_an_action_sooner_than_a_short_one()
    {
        var limits = new OpenUILimits { Fuel = 3000 };
        const string Action = "root = Stack([Button(\"go\", Action([@Set($x, 1), @Set($x, 2), @Set($x, 3)]))])\n";
        Assert.Equal(ActionOutcomeKind.Completed, (await ClickAsync(Action, limits)).Kind);
        Assert.Equal(ActionOutcomeKind.Stopped, (await ClickAsync(Action + "text = \"" + new string('x', 200_000) + "\"\n", limits)).Kind);
    }

    [Fact]
    public async Task An_action_hands_the_callers_thread_back_between_its_steps()
    {
        var outcome = await Task.Run(async () =>
        {
            await using var session = OpenUISession.Create(Library);
            session.Load("root = Stack([Button(\"go\", Action([@Set($x, 1), @Set($x, 2)]))])\n");
            var (button, action) = FirstAction(session);
            var thread = new QueuedContext();
            SynchronizationContext.SetSynchronizationContext(thread);
            var run = session.RunActionAsync(action, new ActionOrigin(button, null)).AsTask();
            SynchronizationContext.SetSynchronizationContext(null);

            Assert.False(run.IsCompleted);
            Assert.True(session.State.Get("$x").TryGetNumber(out var first) && first == 1);
            thread.RunQueued();
            return await run;
        });

        Assert.Equal(ActionOutcomeKind.Completed, outcome.Kind);
    }

    private static (Values.OuiComponent Button, Values.OuiAction Action) FirstAction(OpenUISession session)
    {
        session.Current.Root!["children"].Value.TryGetArray(out var items);
        items![0].TryGetComponent(out var button);
        button!.Props.Single(prop => prop.Value.TryGetAction(out _)).Value.TryGetAction(out var action);
        return (button, action!);
    }

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

        public void RunQueued()
        {
            while (_queue.TryDequeue(out var work))
            {
                work.Callback(work.State);
            }
        }
    }

    private static async Task<ActionOutcome> ClickAsync(string program, OpenUILimits limits)
    {
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Limits = limits });
        session.Load(program);
        var (button, action) = FirstAction(session);
        return await session.RunActionAsync(action, new ActionOrigin(button, null));
    }

    [Fact]
    public async Task Clicks_cannot_fill_the_state_store_without_limit()
    {
        var chunk = "$g = false\nc = \"" + Repeat("c", 200_000) + "\"\ns = c + c + c + c + c\n";
        var buttons = string.Concat(Enumerable.Range(0, 200).Select(b =>
        {
            var sets = string.Join(", ", Enumerable.Range(0, 8).Select(i => $"@Set($k{i}x{b}, $g ? s + \"{i}\" : 0)"));
            return $"b{b} = Button(\"go\", Action([@Set($g, true), {sets}]))\n";
        }));
        var root = "root = Stack([" + string.Join(" ", Enumerable.Range(0, 200).Select(b => "b" + b)) + "])\n";
        var report = await HostileProbe.SurvivesAsync("click-all", chunk + buttons + root, TimeSpan.FromSeconds(60));
        Assert.Contains("The state holds more than", HostileProbe.Messages(report));
    }
}
