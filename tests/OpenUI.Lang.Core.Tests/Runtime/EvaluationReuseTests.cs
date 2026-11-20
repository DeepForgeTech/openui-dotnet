using System.Globalization;
using System.Text;
using OpenUI.Lang.Core.Tests.Conformance;
using OpenUI.Lang.Library;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Runtime;

/// <summary>
/// A session takes a statement's result from its last render when nothing the statement read has changed. That must
/// never show: after every chunk and every write, a session that reuses renders exactly what one that evaluates every
/// statement renders, down to node ids, the errors and their order, and the fuel the pass spent.
/// </summary>
public sealed class EvaluationReuseTests
{
    private static readonly int[] ChunkSizes = [1, 4, 16];

    private static OpenUILibrary Library { get; } = ConformanceAdapter.CreateLibrary(FixtureCatalog.LoadLibrary());

    private static readonly string LongName = "statement" + new string('x', 150);

    private static readonly string A = new('a', 40);

    // Programs aimed at what a reused statement must repeat: errors in order and once, cycles, redefinitions, names
    // long enough to cost fuel to read, loops, a custom function, state read in several ways, and limits.
    private static readonly (string Name, string Text, OpenUILimits? Limits)[] Programs =
    [
        ("state", "$n = 1\n$name = \"x\"\nroot = Stack([t, u, Card(\"c\", [v]), f])\nt = Text(\"n=\" + $n)\nu = Text(\"static\")\nv = Tag($name, \"green\")\nf = Form(\"f\", [Input(\"field\", \"Field\", $name)])\n", null),
        ("errors", "root = Stack([a, b, a, Nope(1), c])\na = Text(1)\nb = Tag(\"t\", \"purple\")\nc = Stack([a, Text(missing)])\n", null),
        ("cycle", "root = Stack([a, b, c])\na = Stack([b])\nb = Stack([a])\nc = Text(\"after\")\n", null),
        ("redefined", "root = Stack([a, b])\na = Text(\"first\")\nb = Stack([a])\na = Text(\"second\")\nb = Stack([a, a])\n", null),
        ("long-names", $"root = Stack([{LongName}, {LongName}2])\n{LongName} = Stack([{LongName}2, Text(\"a\")])\n{LongName}2 = Text(\"b\" + $n)\n$n = 3\n", null),
        ("loops", "$n = 2\nrows = [{name: \"a\"}, {name: \"b\"}, {name: \"c\"}]\nsuffix = \"!\" + $n\nroot = Stack(@Each(rows, \"r\", Text(r.name + suffix)))\n", null),
        ("function", "$s = \"low\"\nroot = Stack([up, plain])\nup = Text(@Upper($s))\nplain = Text(\"plain\")\n", null),
        ("actions", "$clicks = 0\nroot = Stack([b1, b2])\nb1 = Button(\"One\", Action([@Set($clicks, $clicks + 1), @ToAssistant(\"one\")]))\nb2 = Button(\"Two\", Action([@Reset($clicks)]))\n", null),
        // A statement first reached shallow, then deeper than the limit allows, or after the text budget is spent.
        ("deeper", "s = Stack([Text(\"x\")])\nroot = Stack([s])\nroot = Stack([Stack([Stack([Stack([s])])])])\n", new OpenUILimits { MaxEvalDepth = 10 }),
        ("text-spent", $"s = Text((\"{A}\" + \"{A}\") == \"\" ? \"s1\" : \"s2\")\nroot = Stack([s])\nroot = Stack([Text((\"{A}\" + \"{A}\") == \"\" ? \"x\" : \"y\"), Text((\"{A}\" + \"{A}\") == \"\" ? \"x\" : \"y\"), s])\n", new OpenUILimits { MaxTextChars = 200 }),
        ("query", "$team = \"alpha\"\nrows = Query(\"listTickets\", {team: $team}, {rows: []})\nroot = Stack([Text(\"n\" + @Count(rows.rows)), list, other])\nlist = Stack(@Each(rows.rows, \"r\", Text(r.title)))\nother = Text(\"other\")\n", null),
        ("dashboard", Dashboard(12), null),
        ("dashboard-fuel", Dashboard(12), new OpenUILimits { Fuel = 400 }),
        ("dashboard-depth", Dashboard(12), new OpenUILimits { MaxEvalDepth = 9 }),
        ("dashboard-text", Dashboard(12), new OpenUILimits { MaxTextChars = 600 }),
        ("dashboard-errors", Dashboard(12).Replace("\"green\"", "\"purple\"", StringComparison.Ordinal), new OpenUILimits { MaxErrors = 5 }),
    ];

    public static TheoryData<string> ProgramNames() => [.. ProgramSet().Select(p => p.Name)];

    private static IEnumerable<(string Name, string Text, OpenUILimits? Limits)> ProgramSet() =>
        Programs.Concat(FixtureTestDiscoverer.CasesOf(FixtureSuite.CharByChar)
            .Select(c => ("fixture " + c.Id, FixtureRunner.ProgramTextOf(c), (OpenUILimits?)null)));

    [Theory]
    [MemberData(nameof(ProgramNames))]
    public async Task A_session_that_reuses_renders_what_one_that_evaluates_everything_renders(string name)
    {
        var (_, text, limits) = ProgramSet().First(p => p.Name == name);
        foreach (var size in ChunkSizes)
        {
            await using var reusing = Create(limits, reuse: true);
            await using var fresh = Create(limits, reuse: false);
            foreach (var session in new[] { reusing, fresh })
            {
                session.BeginStream();
            }

            foreach (var chunk in Split(text, size))
            {
                reusing.Append(chunk);
                fresh.Append(chunk);
                AssertSame(fresh, reusing, $"{name}, chunks of {size}, after \"{chunk}\"");
            }

            reusing.EndStream();
            fresh.EndStream();
            await Task.WhenAll(reusing.WhenIdleAsync(), fresh.WhenIdleAsync());
            AssertSame(fresh, reusing, $"{name}, chunks of {size}, at the end");

            // Each declared variable written twice, then back to its default: the first write changes what reads it,
            // the second is a value equal to the first, and the reset is the default again.
            var keys = reusing.Current.Document.Statements.Where(s => s.Kind == StatementKind.State).Select(s => s.Name).ToList();
            foreach (var key in keys)
            {
                foreach (var write in new Action<OpenUISession>[] { s => s.State.Set(key, "written"), s => s.State.Set(key, 7), s => s.State.Set(key, 7), s => s.State.Reset(key) })
                {
                    write(reusing);
                    write(fresh);
                    await Task.WhenAll(reusing.WhenIdleAsync(), fresh.WhenIdleAsync());
                    AssertSame(fresh, reusing, $"{name}, chunks of {size}, after a write to {key}");
                }
            }
        }
    }

    [Fact]
    public async Task A_write_keeps_the_instances_of_the_statements_that_do_not_read_it()
    {
        await using var session = Create(null, reuse: true);
        session.Load("$n = 1\nroot = Stack([t, u])\nt = Text(\"n=\" + $n)\nu = Text(\"static\")\n");
        var before = session.Current.Root!.Props[0].Value.TryGetArray(out var items) ? items : throw new InvalidOperationException();

        session.State.Set("$n", 2);

        var after = session.Current.Root!.Props[0].Value.TryGetArray(out var next) ? next : throw new InvalidOperationException();
        Assert.Same(before[1].Payload, after[1].Payload);
        Assert.NotSame(before[0].Payload, after[0].Payload);
        Assert.Equal("n=2", ((OuiComponent)after[0].Payload!)["text"].Value.ToString());
    }

    [Fact]
    public void A_chain_of_references_as_long_as_a_raised_depth_limit_allows_is_reused_as_it_is_evaluated()
    {
        var limits = new OpenUILimits { MaxEvalDepth = 100_000 };
        var text = Chain(5000);
        OnStack(64 * 1024 * 1024, () =>
        {
            var reusing = Create(limits, reuse: true);
            var fresh = Create(limits, reuse: false);
            foreach (var session in new[] { reusing, fresh })
            {
                session.Load(text);
                _ = session.Current;
                session.State.Set("$n", 1);
            }

            AssertSame(fresh, reusing, "after a write beside a chain of 5000");
            Assert.Contains("end", Describe(reusing.Current), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void With_little_stack_left_a_render_evaluates_as_usual_and_still_ends()
    {
        var limits = new OpenUILimits { MaxEvalDepth = 100_000 };
        var text = Chain(5000);
        var reusing = Create(limits, reuse: true);
        var fresh = Create(limits, reuse: false);
        OnStack(64 * 1024 * 1024, () =>
        {
            foreach (var session in new[] { reusing, fresh })
            {
                session.Load(text);
                _ = session.Current;
            }
        });

        // On a small stack the chain cannot be evaluated to its end. The render then stops at a limit, as one that
        // reuses nothing does, rather than overflowing the stack.
        string? reused = null, evaluated = null;
        OnStack(512 * 1024, () =>
        {
            reusing.State.Set("$n", 1);
            fresh.State.Set("$n", 1);
            reused = Describe(reusing.Current);
            evaluated = Describe(fresh.Current);
        });

        Assert.Contains("x-limit-exceeded", evaluated, StringComparison.Ordinal);
        Assert.Equal(evaluated, reused);
    }

    // root reads $n and a chain of statements, each the next one's text, 2 levels of evaluation each.
    private static string Chain(int length)
    {
        var text = new StringBuilder("$n = 0\nroot = Stack([Text(\"n=\" + $n), Text(a0)])\n");
        for (var i = 0; i < length; i++)
        {
            text.Append($"a{i} = a{i + 1}\n");
        }

        return text.Append($"a{length} = \"end\"\n").ToString();
    }

    private static void OnStack(int bytes, Action action)
    {
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            },
            bytes);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
        }
    }

    [Fact]
    public async Task A_statement_that_arrives_later_in_the_stream_is_read_by_the_statements_that_reference_it()
    {
        await using var session = Create(null, reuse: true);
        session.BeginStream();
        session.Append("root = Stack([a, b])\na = Text(\"a\")\n");
        Assert.Single(Children(session));

        session.Append("b = Text(\"b\")\n");

        Assert.Equal(2, Children(session).Count);
    }

    [Fact]
    public async Task A_custom_function_and_a_host_collator_run_on_every_render_as_before()
    {
        var calls = 0;
        var library = OpenUILibrary.FromSpec(
            LibrarySpec.FromJson(System.Text.Json.JsonSerializer.SerializeToElement(FixtureCatalog.LoadLibrary())),
            implement => implement.Function("Upper", (in FunctionArguments args) =>
            {
                calls++;
                return OpenUI.Lang.Values.Ecma.ToUpperCase(OpenUI.Lang.Values.Ecma.ToString(args["text"]));
            }));
        var collator = new CountingCollator();
        await using var session = OpenUISession.Create(library, new OpenUISessionOptions { Collator = collator });
        session.Load("$n = 1\nrows = [{t: \"b\"}, {t: \"a\"}]\nroot = Stack([up, sorted, count])\nup = Text(@Upper(\"x\"))\nsorted = Text(@Sort(rows, \"t\")[0].t)\ncount = Text(\"n=\" + $n)\n");
        _ = session.Current;
        var (callsBefore, comparesBefore) = (calls, collator.Compares);

        session.State.Set("$n", 2);
        _ = session.Current;

        Assert.Equal(callsBefore * 2, calls);
        Assert.Equal(comparesBefore * 2, collator.Compares);
    }

    private sealed class CountingCollator : IOuiCollator
    {
        public int Compares { get; private set; }

        public int Compare(string left, string right)
        {
            Compares++;
            return string.CompareOrdinal(left, right);
        }
    }

    private static OuiArray Children(OpenUISession session) =>
        session.Current.Root!.Props[0].Value.TryGetArray(out var items) ? items : throw new InvalidOperationException();

    // A query whose rows depend on its argument, built again on every fetch as a real tool would.
    private static OpenUITools Tools { get; } = new OpenUITools().Add("listTickets", OpenUIToolEffect.Read, (args, _) =>
    {
        var team = args.TryGetObject(out var fields) ? fields["team"].ToString() : "";
        var rows = Enumerable.Range(0, team.Length).Select(i => (OuiValue)OuiObject.Create([KeyValuePair.Create("title", (OuiValue)$"{team} {i}")]));
        return ValueTask.FromResult<OuiValue>(OuiObject.Create([KeyValuePair.Create("rows", (OuiValue)OuiArray.Create(rows.ToArray()))]));
    });

    private static OpenUISession Create(OpenUILimits? limits, bool reuse)
    {
        var session = OpenUISession.Create(Library, new OpenUISessionOptions { Limits = (limits ?? OpenUILimits.Default) with { MaxFetchesPerSecond = 1000, MaxSessionFetchesPerSecond = 1000 }, Tools = Tools });
        session.ReusesEvaluations = reuse;
        return session;
    }

    private static void AssertSame(OpenUISession expected, OpenUISession actual, string where)
    {
        var (want, got) = (expected.Current, actual.Current);
        Assert.True(Describe(want) == Describe(got), $"{where}:\nexpected {Describe(want)}\nactual   {Describe(got)}");
        Assert.True(((OuiValue)want.Root).Equals(got.Root), $"{where}: the trees differ");
    }

    private static string Describe(RenderSnapshot snapshot)
    {
        var text = new StringBuilder();
        text.Append("fuel ").Append(snapshot.Render.FuelSpent).Append("; root ");
        Describe(text, snapshot.Root);
        text.Append("; errors ");
        foreach (var error in snapshot.Errors)
        {
            text.Append('[').Append(error.Code).Append(' ').Append(error.StatementId).Append(' ').Append(error.Path).Append(' ').Append(error.Message).Append(']');
        }

        text.Append("; diagnostics ");
        foreach (var error in snapshot.Render.Diagnostics)
        {
            text.Append('[').Append(error.Code).Append(' ').Append(error.StatementId).Append(' ').Append(error.Message).Append(']');
        }

        return text.ToString();
    }

    // Everything a value holds, node ids and the sign of zero included.
    private static void Describe(StringBuilder text, OuiValue value)
    {
        if (value.TryGetComponent(out var component))
        {
            text.Append(component.Type).Append('#').Append(component.Id.Value).Append('@').Append(component.StatementId).Append('(');
            foreach (var prop in component.Props)
            {
                text.Append(prop.Name).Append(prop.Binding is { } key ? "<" + key.Name + ">" : "").Append('=');
                Describe(text, prop.Value);
                text.Append(',');
            }

            text.Append(')');
        }
        else if (value.TryGetArray(out var items))
        {
            text.Append('[');
            foreach (var item in items)
            {
                Describe(text, item);
                text.Append(',');
            }

            text.Append(']');
        }
        else if (value.TryGetObject(out var obj))
        {
            text.Append('{');
            foreach (var (key, item) in obj)
            {
                text.Append(key).Append(':');
                Describe(text, item);
                text.Append(',');
            }

            text.Append('}');
        }
        else if (value.TryGetAction(out var action))
        {
            text.Append("action(");
            foreach (var step in action.Steps)
            {
                text.Append(step.GetType().Name).Append(',');
            }

            text.Append(')');
        }
        else if (value.TryGetNumber(out var number))
        {
            text.Append(BitConverter.DoubleToInt64Bits(number).ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            text.Append(value.Kind).Append(':').Append(value.ToString());
        }
    }

    private static List<string> Split(string text, int size)
    {
        var runes = text.EnumerateRunes().Select(r => r.ToString()).ToList();
        return [.. runes.Chunk(size).Select(chunk => string.Concat(chunk))];
    }

    // The shape of the benchmarks' dashboard, without its query: a card per team, each with a metric, a tag and a button.
    private static string Dashboard(int sections)
    {
        var text = new StringBuilder("$team = \"alpha\"\n$clicks = 0\nroot = Stack([Text(\"Tickets for \" + $team, \"heading\"), total");
        for (var i = 1; i <= sections; i++)
        {
            text.Append($", team{i}");
        }

        text.Append("])\ntotal = Metric(\"Clicks\", $clicks)\n");
        for (var i = 1; i <= sections; i++)
        {
            text.Append($"team{i} = Card(\"Team {i}\", [count{i}, tag{i}, reply{i}])\n");
            text.Append($"count{i} = Metric(\"Open\", $clicks + {i})\n");
            text.Append($"tag{i} = Tag($team + \" {i}\", \"green\")\n");
            text.Append($"reply{i} = Button(\"Reply\", Action([@Set($clicks, $clicks + 1), @ToAssistant(\"Look at team {i}\")]))\n");
        }

        return text.ToString();
    }
}
