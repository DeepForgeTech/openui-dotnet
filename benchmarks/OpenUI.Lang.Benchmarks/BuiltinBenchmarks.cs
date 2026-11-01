using BenchmarkDotNet.Attributes;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Benchmarks;

/// <summary>
/// Built-in functions that walk the rows of a query. Each case evaluates a program of one such
/// statement over <see cref="Corpus.Tickets"/>, so Small also shows what a pass costs before any data.
/// </summary>
[MemoryDiagnoser]
public class BuiltinBenchmarks
{
    private const string Query = "tickets = Query(\"listTickets\", {}, {rows: []})\n";

    private EvaluationInputs _inputs = null!;
    private OpenUIDocument _sum = null!;
    private OpenUIDocument _filter = null!;
    private OpenUIDocument _sortByNumber = null!;
    private OpenUIDocument _sortByText = null!;
    private OpenUIDocument _pluck = null!;
    private OpenUIDocument _each = null!;

    [ParamsAllValues]
    public Size Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _inputs = new EvaluationInputs(QueryData: new Dictionary<string, OuiValue> { ["tickets"] = Corpus.Tickets(Corpus.Rows(Size)) });
        var parser = new OpenUIParser(Corpus.Library);
        _sum = Parse(parser, nameof(Sum), "root = Stack([Metric(\"Total age\", @Sum(tickets.rows.age))])\n");
        _filter = Parse(parser, nameof(Filter), "root = Stack([Table([Col(\"Open\", @Filter(tickets.rows, \"status\", \"==\", \"open\").title)])])\n");
        _sortByNumber = Parse(parser, nameof(SortByNumber), "root = Stack([Table([Col(\"Age\", @Sort(tickets.rows, \"age\", \"desc\").age)])])\n");
        _sortByText = Parse(parser, nameof(SortByText), "root = Stack([Table([Col(\"Title\", @Sort(tickets.rows, \"title\").title)])])\n");
        _pluck = Parse(parser, nameof(Pluck), "root = Stack([Table([Col(\"Title\", tickets.rows.title)])])\n");
        _each = Parse(parser, nameof(Each), "root = Stack(@Each(tickets.rows, \"t\", Tag(t.title, \"gray\")))\n");
    }

    [Benchmark]
    public RenderResult Sum() => Evaluate(_sum);

    [Benchmark]
    public RenderResult Filter() => Evaluate(_filter);

    /// <summary>Numbers compare as numbers.</summary>
    [Benchmark]
    public RenderResult SortByNumber() => Evaluate(_sortByNumber);

    /// <summary>Strings go through the collator, as <c>localeCompare</c> does.</summary>
    [Benchmark]
    public RenderResult SortByText() => Evaluate(_sortByText);

    /// <summary>A member read on an array: one field from every element.</summary>
    [Benchmark]
    public RenderResult Pluck() => Evaluate(_pluck);

    [Benchmark]
    public RenderResult Each() => Evaluate(_each);

    private RenderResult Evaluate(OpenUIDocument document) => OpenUIEvaluator.Evaluate(document, _inputs);

    private OpenUIDocument Parse(OpenUIParser parser, string name, string statement)
    {
        var document = parser.Parse(Query + statement);
        var render = OpenUIEvaluator.Evaluate(document, _inputs);
        Check.That(render.Root is not null && render.Errors.Count == 0, $"{name} does not evaluate cleanly");
        return document;
    }
}
