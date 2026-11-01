using System.Text;
using System.Text.Json;
using OpenUI.Lang.Core.Tests.Conformance;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Streaming;

/// <summary>
/// Language.md section 4, "streaming equals parsing", read from every document of a stream rather than
/// the last: where the chunk boundaries fall must not change a document, its errors or its revisions.
/// The deltas describe a chunk, so they are checked by what they add up to and, for a few programs, by
/// a recorded trace.
/// </summary>
public sealed class ChunkBoundaryTests
{
    private static readonly int[] ChunkSizes = [1, 2, 3, 5, 8, 13, 40];

    private static OpenUILibrary Library { get; } = ConformanceAdapter.CreateLibrary(FixtureCatalog.LoadLibrary());

    private static readonly OpenUILimits TightLimits = new() { MaxStatements = 3, MaxStatementChars = 30, MaxErrors = 2 };

    // Programs that reach the corners of section 4: redefinitions, truncated and ternary tails, a late
    // fence, a regenerated response, limits, and statements that fail to bind.
    private static readonly (string Name, string Text, OpenUILimits? Limits)[] Programs =
    [
        ("shared", "root = Stack([a, b, c])\na = Text(\"one\")\nb = Text(\"two\")\nc = Text(\"three\")\nlone = Text(\"x\")\n", null),
        ("redefined", "root = Stack([a])\na = Text(\"first\")\nb = 1\na = Text(\"second\")\nc = 2\na = Text(\"thi", null),
        ("redefined-whole", "root = Stack([a])\na = Text(\"first\")\nb = 1\na = Text(\"second\")\n", null),
        ("ternary", "$n = 1\nroot = Stack([Text($n > 0\n  ? \"pos\"\n  : \"neg\")])\nz = 1\n", null),
        ("comments", "# note\nroot = Stack([Text(\"a ) ] } // kept\"), Text('it\\'s')]) // dropped\nz = 2\n", null),
        ("late-fence", "a = Text(\"prose\")\nb = 2\n\n```openui\nroot = Stack([Text(\"f\"), c])\nc = Text(\"g\")\n```\nafter\n", null),
        ("regenerated", "]]>openui:content\nroot = Stack([a])\na = Text(\"first\")\nb = 1\n]]>openui:content\nroot = Stack([Text(\"second\")])\nx = 3\n]]>openui:end\n", null),
        ("errors", "root = Stack([Nope(1), Text(1, 2, 3), missing, other])\nother = Text()\nbad = Text(\"a\" +\n", null),
        ("cycle", "root = Stack([a])\na = Stack([b])\nb = Stack([a])\n", null),
        ("junk", "hello world\nroot = Stack([a])\n== x\na = Text(\"ok\")\n$s = \"unterminated", null),
        ("no-root", "a = Text(\"x\")\nb = Stack([a])\n", null),
        ("tight", "root = Stack([a, b, c, d])\na = Text(\"1\")\nb = Text(\"2\")\nc = Text(\"3\")\nd = Text(\"a statement longer than thirty characters\")\ne = Nope(1)\nf = Nope(2)\ng = Nope(3)\n", TightLimits),
        ("input-limit", "root = Stack([a])\na = Text(\"abcdefghij\")\nb = Text(\"never read\")\n", new OpenUILimits { MaxInputChars = 40 }),
    ];

    public static TheoryData<string> ProgramNames() => [.. ProgramSet().Select(p => p.Name)];

    // The hand-written programs, then every fixture program.
    private static IEnumerable<(string Name, string Text, OpenUILimits? Limits)> ProgramSet() =>
        Programs.Concat(FixtureTestDiscoverer.CasesOf(FixtureSuite.CharByChar)
            .Select(c => ("fixture " + c.Id, FixtureRunner.ProgramTextOf(c), (OpenUILimits?)null)));

    // Runes, not UTF-16 units: a chunk boundary inside a surrogate pair is not valid text.
    private static List<string> Split(string text, int size)
    {
        var runes = text.EnumerateRunes().Select(r => r.ToString()).ToList();
        return [.. runes.Chunk(size).Select(chunk => string.Concat(chunk))];
    }

    private static OpenUIStreamParser Start(OpenUILimits? limits) =>
        new OpenUIParser(Library, new OpenUIParserOptions { Limits = limits ?? OpenUILimits.Default }).CreateStream();

    private static List<OpenUIDocument> Stream(IEnumerable<string> chunks, OpenUILimits? limits)
    {
        var stream = Start(limits);
        var documents = chunks.Select(chunk => stream.Append(chunk)).ToList();
        documents.Add(stream.Complete());
        return documents;
    }

    [Theory]
    [MemberData(nameof(ProgramNames))]
    public void Every_document_of_a_stream_equals_the_document_of_the_text_read_so_far(string name)
    {
        var (_, text, limits) = ProgramSet().First(p => p.Name == name);
        foreach (var size in ChunkSizes)
        {
            var chunks = Split(text, size);
            var stream = Start(limits);
            var read = new StringBuilder();
            for (var i = 0; i < chunks.Count; i++)
            {
                read.Append(chunks[i]);
                var whole = Start(limits).Append(read.ToString());
                Assert.Equal(Describe(whole), Describe(stream.Append(chunks[i])));
            }

            var wholeFinal = Start(limits);
            wholeFinal.Append(text);
            Assert.Equal(Describe(wholeFinal.Complete()), Describe(stream.Complete()));
        }
    }

    [Theory]
    [MemberData(nameof(ProgramNames))]
    public void A_document_kept_while_the_stream_went_on_still_reads_as_it_did(string name)
    {
        var (_, text, limits) = ProgramSet().First(p => p.Name == name);
        var chunks = Split(text, 5);
        var stream = Start(limits);
        var kept = chunks.Select(chunk => stream.Append(chunk)).ToList();
        stream.Complete();

        // Nothing was read from the documents until the stream ended.
        var read = new StringBuilder();
        for (var i = 0; i < chunks.Count; i++)
        {
            read.Append(chunks[i]);
            Assert.Equal(Describe(Start(limits).Append(read.ToString())), Describe(kept[i]));
        }
    }

    [Fact]
    public void A_document_read_from_several_threads_at_once_gives_the_same_answers()
    {
        var (_, text, limits) = Programs.First(p => p.Name == "errors");
        var expected = Describe(Start(limits).Append(text));

        for (var attempt = 0; attempt < 50; attempt++)
        {
            var document = Start(limits).Append(text);
            var described = new string[8];
            Parallel.For(0, described.Length, new ParallelOptions { MaxDegreeOfParallelism = described.Length }, i => described[i] = Describe(document));
            Assert.All(described, line => Assert.Equal(expected, line));
        }
    }

    [Theory]
    [MemberData(nameof(ProgramNames))]
    public void Revisions_count_up_by_one_and_removed_names_are_those_the_last_document_lost(string name)
    {
        var (_, text, limits) = ProgramSet().First(p => p.Name == name);
        foreach (var size in ChunkSizes)
        {
            var documents = Stream(Split(text, size), limits);
            for (var i = 1; i < documents.Count; i++)
            {
                Assert.Equal(documents[i - 1].Revision + 1, documents[i].Revision);
                var lost = documents[i - 1].Statements.Select(s => s.Name).Where(n => !documents[i].TryGetStatement(n, out _));
                Assert.Equal(lost, documents[i].Delta.Removed);
            }
        }
    }

    [Theory]
    [MemberData(nameof(ProgramNames))]
    public void The_statements_completed_over_a_stream_do_not_depend_on_the_chunks(string name)
    {
        var (_, text, limits) = ProgramSet().First(p => p.Name == name);
        var expected = Completed(Stream([text], limits));
        foreach (var size in ChunkSizes)
        {
            Assert.Equal(expected, Completed(Stream(Split(text, size), limits)));
        }
    }

    // Names in the order they completed, from the last reset on.
    private static List<string> Completed(IEnumerable<OpenUIDocument> documents)
    {
        var names = new List<string>();
        foreach (var document in documents)
        {
            if (document.Delta.WasReset)
            {
                names.Clear();
            }

            names.AddRange(document.Delta.Completed);
        }

        return names;
    }

    [Fact]
    public void A_recorded_trace_of_seven_character_chunks_is_reproduced()
    {
        var trace = new StringBuilder();
        foreach (var (name, text, limits) in Programs)
        {
            trace.Append("== ").Append(name).Append('\n');
            foreach (var document in Stream(Split(text, 7), limits))
            {
                trace.Append(Trace(document)).Append('\n');
            }
        }

        // OPENUI_WRITE_TRACE=<path> records the trace there instead of checking it.
        if (Environment.GetEnvironmentVariable("OPENUI_WRITE_TRACE") is { Length: > 0 } target)
        {
            File.WriteAllText(target, trace.ToString());
            return;
        }

        Assert.Equal(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Streaming", "ChunkTrace.txt")), trace.ToString());
    }

    // What one chunk did to the stream: the revision, the statements and what the delta says.
    private static string Trace(OpenUIDocument document)
    {
        var delta = document.Delta;
        var statements = document.Statements.Select(s => s.Name + (s.IsPending ? "*" : ""));
        return $"r{document.Revision}{(document.IsFinal ? " final" : "")}{(document.Incomplete ? " incomplete" : "")} " +
            $"[{string.Join(' ', statements)}] errors={document.Errors.Count}{(delta.WasReset ? " reset" : "")} " +
            $"completed=[{string.Join(' ', delta.Completed)}] pending=[{string.Join(' ', delta.PendingChanged)}] removed=[{string.Join(' ', delta.Removed)}]";
    }

    // Everything a host can read from a document, on one line. The revision and the delta belong to a stream, not to the text, so they are left out.
    private static string Describe(OpenUIDocument document)
    {
        var line = new StringBuilder();
        line.Append($"final={document.IsFinal} incomplete={document.Incomplete}");
        line.Append($" entry={document.Entry?.Name}/{document.EntryKind}");
        line.Append(" statements=[").AppendJoin(", ", document.Statements.Select(s =>
            $"{s.Name}:{s.Kind}{(s.IsPending ? ":pending" : "")}{(s.NeededImplicitClose ? ":closed" : "")}@{s.Span.Offset}+{s.Span.Length}->{string.Join('|', s.References)}")).Append(']');
        line.Append(" unresolved=[").AppendJoin(", ", document.Unresolved).Append(']');
        line.Append(" orphans=[").AppendJoin(", ", document.Orphans).Append(']');
        line.Append(" errors=[").AppendJoin("; ", document.Errors.Select(Describe)).Append(']');
        line.Append(" diagnostics=[").AppendJoin("; ", document.AllDiagnostics.Select(d => $"{Describe(d.Error)} ({d.Timing})")).Append(']');
        line.Append(" listed=[").AppendJoin("; ", document.Diagnostics.Select(Describe)).Append(']');
        line.Append(" render=").Append(Render(document));
        return line.ToString();
    }

    private static string Describe(OpenUIError error) =>
        $"{error.Code.Value}@{error.StatementId}/{error.Component}{error.Path}{(error.IsWarning ? " warning" : "")}: {error.Message}";

    private static string Render(OpenUIDocument document) =>
        OpenUIEvaluator.Evaluate(document).Root is { } root ? JsonSerializer.Serialize<OuiValue>(root) : "none";
}
