using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using OpenUI.Lang.Library;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Benchmarks;

/// <summary>
/// The inputs every benchmark class draws from. Small is a program from tests/fixtures/1.0. The
/// fixtures stop at a few hundred characters, so Medium and Large are generated in the same shape:
/// a ticket dashboard that reads a query, filters and sorts its rows, and repeats a card per team.
/// </summary>
internal static class Corpus
{
    /// <summary>Characters per chunk when a program is streamed: a few tokens, as a model reply arrives.</summary>
    public const int ChunkChars = 16;

    public static OpenUILibrary Library { get; } = CreateLibrary();

    public static string LibraryJson { get; } = Resource("fixtures/library.json");

    /// <summary>Rows of query data and of JSON.</summary>
    public static int Rows(Size size) => size switch { Size.Small => 10, Size.Medium => 100, _ => 1000 };

    /// <summary>Fields in a generated form.</summary>
    public static int FormFields(Size size) => size switch { Size.Small => 1, Size.Medium => 20, _ => 200 };

    /// <summary>A model reply: the worked example of the streaming fixtures, or a dashboard whose root comes first.</summary>
    public static string Reply(Size size) => size == Size.Small ? WorkedExample : Dashboard(Sections(size));

    /// <summary>A program with state and buttons: the several-actions fixture, or the same dashboard.</summary>
    public static string Interactive(Size size) => size == Size.Small ? Resource("fixtures/several-actions.oui") : Dashboard(Sections(size));

    /// <summary>A state variable <see cref="Interactive"/> declares.</summary>
    public static string StateKey(Size size) => size == Size.Small ? "$n" : "$team";

    public static string MutationProgram { get; } = Resource("fixtures/mutation-run.oui");

    /// <summary>The tools a ticket program calls: the query behind its dashboard and the mutation of the fixture.</summary>
    public static OpenUITools Tools(int rows) => new OpenUITools()
        .Add("listTickets", OpenUIToolEffect.Read, (_, _) => ValueTask.FromResult(Tickets(rows)))
        .Add("createTicket", (_, _) => ValueTask.FromResult<OuiValue>(OuiObject.Create([KeyValuePair.Create("id", (OuiValue)1)])));

    /// <summary>What <c>listTickets</c> returns: <c>{ rows: [...] }</c>, rows of id, title, status, team, age and tags.</summary>
    public static OuiValue Tickets(int rows)
    {
        string[] statuses = ["open", "closed", "blocked"];
        string[] teams = ["alpha", "beta", "gamma", "delta"];
        string[] words = ["login", "export", "billing", "search", "upload", "report", "alerts", "sync"];
        var random = new Random(20251031);
        var items = ImmutableArray.CreateBuilder<OuiValue>(rows);
        for (var i = 0; i < rows; i++)
        {
            var title = $"{words[random.Next(words.Length)]} fails for {words[random.Next(words.Length)]} users #{i}";
            items.Add(OuiObject.Create(
            [
                KeyValuePair.Create("id", (OuiValue)i),
                KeyValuePair.Create("title", (OuiValue)title),
                KeyValuePair.Create("status", (OuiValue)statuses[random.Next(statuses.Length)]),
                KeyValuePair.Create("team", (OuiValue)teams[random.Next(teams.Length)]),
                KeyValuePair.Create("age", (OuiValue)random.Next(1, 90)),
                KeyValuePair.Create("tags", (OuiValue)OuiArray.Create(words[random.Next(words.Length)], words[random.Next(words.Length)])),
            ]));
        }

        return OuiObject.Create([KeyValuePair.Create("rows", (OuiValue)OuiArray.Create(items.MoveToImmutable()))]);
    }

    /// <summary>Mounts <paramref name="program"/> in a session and waits for its queries.</summary>
    public static OpenUISession Mount(string program, int rows, IOpenUIHostEventHandler? events = null)
    {
        var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = Tools(rows), Events = events });
        session.Load(program);
        session.WhenIdleAsync().GetAwaiter().GetResult();
        Check.That(session.Current.Errors.Count == 0, "the mounted program reported errors");
        return session;
    }

    public static string[] Chunk(string text, int chunkChars)
    {
        var chunks = new string[(text.Length + chunkChars - 1) / chunkChars];
        for (var i = 0; i < chunks.Length; i++)
        {
            chunks[i] = text.Substring(i * chunkChars, Math.Min(chunkChars, text.Length - (i * chunkChars)));
        }

        return chunks;
    }

    /// <summary>A form of <paramref name="fields"/> inputs, each with <paramref name="rules"/>, and a send button.</summary>
    public static string Form(int fields, string rules) => Form(fields, _ => rules);

    public static string Form(int fields, Func<int, string> rules)
    {
        var text = new StringBuilder("root = Form(\"signup\", [");
        for (var i = 1; i <= fields; i++)
        {
            text.Append($"field{i}, ");
        }

        text.Append("send])\n");
        for (var i = 1; i <= fields; i++)
        {
            text.Append($"field{i} = Input(\"field{i}\", \"Field {i}\", null, {rules(i)})\n");
        }

        text.Append("send = Button(\"Send\", Action([@ToAssistant(\"Send\")]))\n");
        return text.ToString();
    }

    /// <summary>The last button in the tree, which in both interactive programs has the most steps.</summary>
    public static OuiComponent LastButton(OuiValue tree)
    {
        OuiComponent? found = null;
        void Visit(OuiValue value)
        {
            if (value.TryGetArray(out var items))
            {
                foreach (var item in items)
                {
                    Visit(item);
                }
            }
            else if (value.TryGetComponent(out var component))
            {
                if (component.Type == "Button")
                {
                    found = component;
                }

                foreach (var prop in component.Props)
                {
                    Visit(prop.Value);
                }
            }
        }

        Visit(tree);
        return found ?? throw new InvalidOperationException("The tree has no button.");
    }

    private static int Sections(Size size) => size == Size.Medium ? 20 : 100;

    private static string WorkedExample { get; } = string.Concat(JsonSerializer.Deserialize<string[]>(Resource("fixtures/worked-example.json"))!);

    // Four statements per section plus eight shared ones. The root comes first, so a streamed
    // program has unresolved references until the end, as a model's reply does.
    private static string Dashboard(int sections)
    {
        var text = new StringBuilder();
        text.Append("$team = \"alpha\"\n$clicks = 0\n");
        text.Append("root = Stack([Text(\"Tickets for \" + $team, \"heading\"), Metric(\"Open\", @Count(openRows)), Metric(\"Average age\", averageAge), board");
        for (var i = 1; i <= sections; i++)
        {
            text.Append($", team{i}");
        }

        text.Append("])\n");
        text.Append("tickets = Query(\"listTickets\", {}, {rows: []})\n");
        text.Append("openRows = @Filter(tickets.rows, \"status\", \"==\", \"open\")\n");
        text.Append("byAge = @Sort(tickets.rows, \"age\", \"desc\")\n");
        text.Append("averageAge = @Round(@Avg(tickets.rows.age), 1)\n");
        text.Append("board = Table([Col(\"Title\", byAge.title), Col(\"Status\", byAge.status), Col(\"Age\", byAge.age)])\n");
        for (var i = 1; i <= sections; i++)
        {
            text.Append($"team{i} = Card(\"Team {i}\", [count{i}, tag{i}, reply{i}])\n");
            text.Append($"count{i} = Metric(\"Open\", @Count(openRows) + {i})\n");
            text.Append($"tag{i} = Tag($team + \" {i}\", \"green\")\n");
            text.Append($"reply{i} = Button(\"Reply\", Action([@Set($clicks, $clicks + 1), @ToAssistant(\"Look at team {i}\")]))\n");
        }

        return text.ToString();
    }

    // The fixture library as a host would load it. Its Upper function is the suite's own and is never called here.
    private static OpenUILibrary CreateLibrary() => OpenUILibrary.FromSpec(
        LibrarySpec.Parse(Resource("fixtures/library.json")),
        implement => implement
            .Traits("Form", new ComponentTraits(FormNameProp: "name"))
            .Traits("Input", new ComponentTraits(FieldNameProp: "name", FieldComponentType: "input", FieldRulesProp: "rules")));

    private static string Resource(string name)
    {
        using var stream = typeof(Corpus).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The resource {name} is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
