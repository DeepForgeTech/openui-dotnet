using BenchmarkDotNet.Attributes;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Benchmarks;

/// <summary>Evaluating a parsed program, and drawing it again after a state variable changes.</summary>
[MemoryDiagnoser]
public class EvaluationBenchmarks
{
    private OpenUIDocument _document = null!;
    private EvaluationInputs _inputs = null!;
    private OpenUISession _session = null!;
    private string _stateKey = null!;
    private int _tick;

    [ParamsAllValues]
    public Size Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var rows = Corpus.Rows(Size);
        _document = new OpenUIParser(Corpus.Library).Parse(Corpus.Reply(Size));
        _inputs = new EvaluationInputs(QueryData: new Dictionary<string, OuiValue> { ["tickets"] = Corpus.Tickets(rows) });
        var render = OpenUIEvaluator.Evaluate(_document, _inputs);
        Check.That(render.Root is not null && render.Errors.Count == 0, "the program does not evaluate cleanly");

        _stateKey = Corpus.StateKey(Size);
        _session = Corpus.Mount(Corpus.Interactive(Size), rows);
        Check.That(ReEvaluateAfterStateChange().Root is not null, "the mounted program renders nothing");
    }

    [GlobalCleanup]
    public void Cleanup() => _session.DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>One whole pass over the document with the query's rows supplied and state at its defaults.</summary>
    [Benchmark]
    public RenderResult Evaluate() => OpenUIEvaluator.Evaluate(_document, _inputs);

    /// <summary>A write to a state variable, then the read that evaluates the session again.</summary>
    [Benchmark]
    public RenderSnapshot ReEvaluateAfterStateChange()
    {
        _session.State.Set(_stateKey, ++_tick);
        return _session.Current;
    }
}
