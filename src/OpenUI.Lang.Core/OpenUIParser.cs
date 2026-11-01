using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;
using OpenUI.Lang.Protocol;
using OpenUI.Lang.Semantics;
using OpenUI.Lang.Syntax;

namespace OpenUI.Lang;

/// <summary>
/// Parses OpenUI Lang against a library (language.md, sections 1, 2 and 4). Stateless after
/// construction, thread-safe and reusable. A batch parse is a stream of one chunk, so batch and
/// streaming share one implementation (section 4, "streaming equals parsing").
/// </summary>
public sealed class OpenUIParser
{
    private readonly OpenUILibrary _library;
    private readonly OpenUIParserOptions _options;

    /// <summary>Creates a parser for <paramref name="library"/> (language.md, sections 1, 2 and 4).</summary>
    public OpenUIParser(OpenUILibrary library, OpenUIParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;
        _options = options ?? OpenUIParserOptions.Default;
    }

    /// <summary>Parses a complete response: <c>CreateStream().Append(text)</c> then <c>Complete()</c> (language.md, sections 1, 2 and 4).</summary>
    public OpenUIDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var stream = CreateStream();
        stream.Append(text);
        return stream.Complete();
    }

    /// <summary>Starts a stream (section 4).</summary>
    public OpenUIStreamParser CreateStream() => new(_library, _options.Limits);

    /// <summary>
    /// Yields one document per chunk and a final one when <paramref name="chunks"/> ends. Cancellation
    /// throws <see cref="OperationCanceledException"/> and yields no final document (language.md, sections 1, 2 and 4).
    /// </summary>
    public async IAsyncEnumerable<OpenUIDocument> ParseStreamAsync(
        IAsyncEnumerable<string> chunks,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        var stream = CreateStream();
        await foreach (var chunk in chunks.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return stream.Append(chunk);
        }

        cancellationToken.ThrowIfCancellationRequested();
        yield return stream.Complete();
    }
}

/// <summary>
/// One streamed response (language.md, section 4). Each <see cref="Append"/> scans only the new
/// characters: completed statements are parsed once, and the pending tail is parsed again after
/// implicit closing. Single producer; not thread-safe. The documents it returns are immutable.
/// </summary>
public sealed class OpenUIStreamParser
{
    // A long pending statement is parsed again only after it grows this much (or completes), which
    // keeps a giant statement arriving one character at a time from costing quadratic time.
    private const int PendingReparseStep = 1024;

    private readonly OpenUILibrary _library;
    private readonly OpenUILimits _limits;
    private readonly OpenUIMessageStreamReader _envelope = new();
    private readonly CharBuffer _prepared = new();
    private readonly List<Segment> _segments = [];
    private readonly List<string> _completedSinceLast = [];

    // The completed statements after last-wins, in order of their last definition, as a document lists them.
    private readonly List<Statement> _completed = [];
    private readonly Dictionary<string, Statement> _completedByName = new(StringComparer.Ordinal);
    private DiagnosticBag _statementLimits;
    private SourcePreparer _preparer;
    private Segmenter _segmenter;
    private Statement? _pending;
    private Segment? _pendingSegment;
    private Diagnostic? _inputLimit;
    private ImmutableArray<Diagnostic>? _limitDiagnostics;
    private int _inputChars;
    private int _completedCount;
    private bool _resetSinceLast;
    private long _revision;

    internal OpenUIStreamParser(OpenUILibrary library, OpenUILimits limits)
    {
        _library = library;
        _limits = limits;
        _statementLimits = new DiagnosticBag(limits.MaxErrors);
        _preparer = new SourcePreparer(_prepared);
        _segmenter = new Segmenter(limits.MaxStatementChars);
        Current = Publish(isFinal: false, incomplete: false);
    }

    /// <summary>The latest document (language.md, section 4).</summary>
    public OpenUIDocument Current { get; private set; }

    /// <summary>True after <see cref="Complete"/> (language.md, section 4).</summary>
    public bool IsCompleted { get; private set; }

    /// <summary>Reads one chunk and returns the document for everything read so far (language.md, section 4).</summary>
    public OpenUIDocument Append(ReadOnlySpan<char> chunk)
    {
        if (IsCompleted)
        {
            throw new InvalidOperationException("The stream has ended; chunks after the end belong to a new stream (language.md, section 4, rule 7).");
        }

        try
        {
            Feed(_envelope.Push(LimitInput(chunk)));
            return Current = Publish(isFinal: false, incomplete: _segmenter.EndsInsideStringOrBracket);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Current = Failed(ex);
        }
    }

    /// <summary>Ends the stream: the pending tail is implicitly closed and completes (section 4, rule 7).</summary>
    public OpenUIDocument Complete()
    {
        if (IsCompleted)
        {
            return Current;
        }

        IsCompleted = true;
        try
        {
            Feed(_envelope.Complete());
            _preparer.Complete();
            Scan();
            var incomplete = _segmenter.EndsInsideStringOrBracket || _inputLimit is not null;
            if (_segmenter.Tail(_prepared.Length) is { } tail)
            {
                AddCompleted(tail);
            }

            _pending = null;
            _pendingSegment = null;
            return Current = Publish(isFinal: true, incomplete);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Current = Failed(ex);
        }
    }

    private OpenUIDocument Failed(Exception ex)
    {
        // A bug in the parser must not reach the host (section 8.2, parse-exception).
        var error = new OpenUIError { Source = OpenUIErrorSource.Parser, Code = OpenUIErrorCode.ParseException, Message = ex.Message };
        return new OpenUIDocument(
            _library, ++_revision, IsCompleted, new Diagnostic(error, DiagnosticTiming.Immediate));
    }

    private ReadOnlySpan<char> LimitInput(ReadOnlySpan<char> chunk)
    {
        var room = _limits.MaxInputChars - _inputChars;
        if (chunk.Length <= room)
        {
            _inputChars += chunk.Length;
            return chunk;
        }

        _inputChars = _limits.MaxInputChars;
        if (_inputLimit is null)
        {
            _inputLimit = Limit(null, $"The input is longer than {_limits.MaxInputChars} characters; the rest was ignored.");
            _limitDiagnostics = null;
        }

        return chunk[..Math.Max(room, 0)];
    }

    private void Feed(ContentUpdate update)
    {
        if (update.IsReset)
        {
            _preparer = new SourcePreparer(_prepared);
            _prepared.Clear();
            Restart();
        }

        foreach (var c in update.Text)
        {
            if (_preparer.Push(c))
            {
                Restart();
            }
        }

        Scan();
    }

    // The prepared text no longer starts with what was parsed: parse again from the start (section 4, rule 4).
    private void Restart()
    {
        _segmenter = new Segmenter(_limits.MaxStatementChars);
        _completed.Clear();
        _completedByName.Clear();
        _completedCount = 0;
        _statementLimits = new DiagnosticBag(_limits.MaxErrors);
        _limitDiagnostics = null;
        _completedSinceLast.Clear();
        _pending = null;
        _pendingSegment = null;
        _resetSinceLast = true;
    }

    private void Scan()
    {
        _segments.Clear();
        _segmenter.Scan(_prepared, _segments);
        foreach (var segment in _segments)
        {
            AddCompleted(segment);
        }

        UpdatePending();
    }

    private void AddCompleted(Segment segment)
    {
        if (Parse(segment, isPending: false) is not { } bound)
        {
            return;
        }

        var statement = new Statement(bound, isPending: false, neededImplicitClose: false);
        if (_completedByName.TryGetValue(bound.Name, out var replaced))
        {
            _completed.Remove(replaced);
        }

        _completed.Add(statement);
        _completedByName[bound.Name] = statement;
        _completedCount++;
        _completedSinceLast.Add(bound.Name);
    }

    private void UpdatePending()
    {
        if (_segmenter.Tail(_prepared.Length) is not { } tail)
        {
            _pending = null;
            _pendingSegment = null;
            return;
        }

        if (_pendingSegment is { } last && last.Start == tail.Start
            && (last.End == tail.End || (tail.Length > PendingReparseStep && tail.End - last.End < PendingReparseStep)))
        {
            return;
        }

        _pendingSegment = tail;
        _pending = Parse(tail, isPending: true) is { } bound ? new Statement(bound, isPending: true, tail.NeedsClose) : null;
    }

    private BoundStatement? Parse(Segment segment, bool isPending)
    {
        string? problem = null;
        if (segment.TooLong)
        {
            problem = $"A statement is longer than {_limits.MaxStatementChars} characters and was dropped.";
        }
        else if (!isPending && _completedCount >= _limits.MaxStatements)
        {
            problem = $"The program has more than {_limits.MaxStatements} statements; the rest were dropped.";
        }

        ParseOutcome outcome = default;
        if (problem is null)
        {
            outcome = Parser.ParseStatement(_prepared.Substring(segment.Start, segment.Length), segment.Start, segment.NeedsClose, _limits.MaxSyntaxDepth);
            if (outcome.TooDeep)
            {
                problem = $"A statement nests deeper than {_limits.MaxSyntaxDepth} levels and was dropped.";
            }
        }

        if (problem is not null)
        {
            ReportDropped(isPending, problem);
            return null;
        }

        try
        {
            return outcome.Statement is { } syntax ? Binder.Bind(syntax, _library, isPending, new DiagnosticBag(_limits.MaxErrors)) : null;
        }
        catch (InsufficientExecutionStackException)
        {
            // Nesting within the limit can still build a tree too tall for this thread's stack. That
            // costs this statement and no other.
            ReportDropped(isPending, "A statement nests too deeply to read and was dropped.");
            return null;
        }
    }

    private void ReportDropped(bool isPending, string problem)
    {
        if (!isPending)
        {
            _statementLimits.AddRange([Limit(null, problem)]);
            _limitDiagnostics = null;
        }
    }

    private OpenUIDocument Publish(bool isFinal, bool incomplete)
    {
        var previous = Current;
        var statements = Statements();
        var pendingChanged = _pending is not null && (previous?.Statements.LastOrDefault() is not { IsPending: true } old || !ReferenceEquals(old.Bound, _pending.Bound))
            ? ImmutableArray.Create(_pending.Name)
            : [];
        var delta = new DocumentDelta(_resetSinceLast, [.. _completedSinceLast], pendingChanged, Removed(previous, statements));
        var document = new OpenUIDocument(
            _library, ++_revision, isFinal, incomplete, statements, _prepared.Length > 0, LimitDiagnostics(), _limits.MaxErrors, delta);
        _completedSinceLast.Clear();
        _resetSinceLast = false;
        return document;
    }

    // The later statement wins, but a pending statement that needed implicit closing never replaces a
    // completed one (sections 2.4 and 4, rule 3).
    private ImmutableArray<Statement> Statements()
    {
        var keepPending = _pending is not null && !(_pending.NeededImplicitClose && _completedByName.ContainsKey(_pending.Name));
        var replaced = keepPending && _completedByName.TryGetValue(_pending!.Name, out var same) ? same : null;
        var statements = new Statement[_completed.Count - (replaced is null ? 0 : 1) + (keepPending ? 1 : 0)];
        if (replaced is null)
        {
            _completed.CopyTo(statements);
        }
        else
        {
            var at = _completed.IndexOf(replaced);
            _completed.CopyTo(0, statements, 0, at);
            _completed.CopyTo(at + 1, statements, at, _completed.Count - at - 1);
        }

        if (keepPending)
        {
            statements[^1] = _pending!;
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(statements);
    }

    // The names the previous document had and this one lacks. A completed statement never leaves, so
    // unless the stream started over only the pending statement the previous document ended with can.
    private ImmutableArray<string> Removed(OpenUIDocument? previous, ImmutableArray<Statement> statements)
    {
        if (previous is null || previous.Statements.IsEmpty)
        {
            return [];
        }

        if (_resetSinceLast)
        {
            var names = statements.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
            return [.. previous.Statements.Select(s => s.Name).Where(name => !names.Contains(name))];
        }

        var last = previous.Statements[^1];
        return last.IsPending && !_completedByName.ContainsKey(last.Name) && _pending?.Name != last.Name ? [last.Name] : [];
    }

    // What the document says about the limits this stream hit, which only changes when one is hit.
    private ImmutableArray<Diagnostic> LimitDiagnostics()
    {
        if (_limitDiagnostics is { } cached)
        {
            return cached;
        }

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        if (_inputLimit is { } input)
        {
            diagnostics.Add(input);
        }

        diagnostics.AddRange(_statementLimits.Items);
        _limitDiagnostics = diagnostics.ToImmutable();
        return _limitDiagnostics.Value;
    }

    private static Diagnostic Limit(string? statement, string message) => new(
        new OpenUIError { Source = OpenUIErrorSource.Parser, Code = OpenUIErrorCode.LimitExceeded, Message = message, StatementId = statement },
        DiagnosticTiming.Immediate);
}
