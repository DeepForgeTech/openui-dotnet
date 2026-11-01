using BenchmarkDotNet.Attributes;

namespace OpenUI.Lang.Benchmarks;

/// <summary>
/// Parsing a whole reply in one call, <see cref="OpenUIParser.Parse"/>, and reading what a host reads
/// first from the result. A document works out its errors and references when they are first read, so
/// reading them keeps this comparable with builds that worked them out during the parse.
/// </summary>
[MemoryDiagnoser]
public class ParseBenchmarks
{
    private OpenUIParser _parser = null!;
    private string _text = null!;

    [ParamsAllValues]
    public Size Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _text = Corpus.Reply(Size);
        _parser = new OpenUIParser(Corpus.Library);
        var document = _parser.Parse(_text);
        Check.That(document.IsFinal && document.Entry is not null && document.Errors.Count == 0 && document.Unresolved.IsEmpty, "the program does not parse cleanly");
    }

    [Benchmark]
    public OpenUIDocument Parse()
    {
        var document = _parser.Parse(_text);
        _ = document.Errors;
        _ = document.Unresolved;
        return document;
    }
}
