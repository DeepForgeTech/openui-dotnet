using BenchmarkDotNet.Attributes;
using OpenUI.Lang.Runtime;

namespace OpenUI.Lang.Benchmarks;

/// <summary>
/// One reply fed in chunks of <see cref="Corpus.ChunkChars"/> characters, as a model sends it. The
/// time per chunk should stay flat as the program grows; a cost that grows with the program is quadratic.
/// A document works out its errors and references when they are first read, so what a case reads is
/// part of what it measures.
/// </summary>
[MemoryDiagnoser]
public class StreamingBenchmarks
{
    private OpenUIParser _parser = null!;
    private OpenUITools _tools = null!;
    private string[] _chunks = null!;

    [ParamsAllValues]
    public Size Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _chunks = Corpus.Chunk(Corpus.Reply(Size), Corpus.ChunkChars);
        _parser = new OpenUIParser(Corpus.Library);
        _tools = Corpus.Tools(100);
        Check.That(ParserStream().Errors.Count == 0, "the streamed program has errors");
        Check.That(ParserStreamReadEveryChunk().Errors.Count == 0, "the streamed program has errors when read per chunk");
        var snapshot = SessionStream();
        Check.That(snapshot.Root is not null, "the streamed program renders nothing");
        Check.That(snapshot.Errors.Count == 0, "the streamed program has runtime errors");
        Check.That(SessionStreamRenderedEveryChunk().Errors.Count == 0, "the streamed program has runtime errors when drawn per chunk");
    }

    /// <summary>The parser alone, for a host that only reads the errors and references of the final document.</summary>
    [Benchmark]
    public OpenUIDocument ParserStream()
    {
        var stream = _parser.CreateStream();
        foreach (var chunk in _chunks)
        {
            stream.Append(chunk);
        }

        return Read(stream.Complete());
    }

    /// <summary>The parser alone, for a host that reads the errors and references of every document.</summary>
    [Benchmark]
    public OpenUIDocument ParserStreamReadEveryChunk()
    {
        var stream = _parser.CreateStream();
        foreach (var chunk in _chunks)
        {
            Read(stream.Append(chunk));
        }

        return Read(stream.Complete());
    }

    /// <summary>A session that is read once, when the stream has ended.</summary>
    [Benchmark]
    public RenderSnapshot SessionStream()
    {
        var session = OpenUISession.Create(Corpus.Library, new OpenUISessionOptions { Tools = _tools });
        session.BeginStream();
        foreach (var chunk in _chunks)
        {
            session.Append(chunk);
        }

        session.EndStream();
        var snapshot = session.Current;
        session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return snapshot;
    }

    /// <summary>A session read after every chunk, as a page that draws each change does.</summary>
    [Benchmark]
    public RenderSnapshot SessionStreamRenderedEveryChunk()
    {
        var session = OpenUISession.Create(Corpus.Library, new OpenUISessionOptions { Tools = _tools });
        session.BeginStream();
        foreach (var chunk in _chunks)
        {
            session.Append(chunk);
            _ = session.Current;
        }

        session.EndStream();
        var snapshot = session.Current;
        session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return snapshot;
    }

    private static OpenUIDocument Read(OpenUIDocument document)
    {
        _ = document.Errors;
        _ = document.Unresolved;
        return document;
    }
}
