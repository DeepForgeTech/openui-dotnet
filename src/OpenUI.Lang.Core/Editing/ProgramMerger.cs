using System.Collections.Immutable;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Semantics;
using OpenUI.Lang.Syntax;

namespace OpenUI.Lang.Editing;

/// <summary>The outcome of merging a patch into a program (language.md, section 7).</summary>
/// <param name="Text">The merged program, one statement per line.</param>
/// <param name="Statements">The statement names of <paramref name="Text"/>, in order.</param>
/// <param name="Deleted">Names the patch deleted with a top-level <c>name = null</c>.</param>
/// <param name="Collected">Names removed because <c>root</c> no longer reaches them.</param>
/// <param name="Errors">What a limit dropped from either text (section 8.1).</param>
public sealed record MergeResult(string Text, ImmutableArray<string> Statements, ImmutableArray<string> Deleted, ImmutableArray<string> Collected, OpenUIErrorList Errors);

/// <summary>Merges an edit-mode patch into the current program by statement name (language.md, section 7). Needs no library.</summary>
public static class ProgramMerger
{
    /// <summary>
    /// Merges <paramref name="patch"/> into <paramref name="current"/> (language.md, section 7): a patch
    /// statement replaces the one of the same name in place or is appended; a top-level <c>name = null</c>
    /// deletes; then, when a <c>root</c> statement exists, statements it cannot reach are removed (state
    /// declarations, queries and mutations stay). Both texts are read as source text (sections 1.2 and
    /// 1.4), so a reply of prose plus a fenced patch merges only the fenced part. A statement over a
    /// limit is dropped and reported; merging never throws for any text (section 8.1).
    /// </summary>
    /// <param name="current">The current program.</param>
    /// <param name="patch">The patch, after it has finished streaming.</param>
    /// <param name="limits">Budgets; null for <see cref="OpenUILimits.Default"/>.</param>
    public static MergeResult Merge(string current, string patch, OpenUILimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(patch);
        limits ??= OpenUILimits.Default;

        var errors = new DiagnosticBag(limits.MaxErrors);
        var merged = new List<MergeStatement?>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        void Put(MergeStatement statement)
        {
            if (index.TryGetValue(statement.Syntax.Name, out var at))
            {
                merged[at] = statement;
            }
            else
            {
                index[statement.Syntax.Name] = merged.Count;
                merged.Add(statement);
            }
        }

        foreach (var statement in Statements(current, limits, errors))
        {
            Put(statement);
        }

        var deleted = new List<string>();
        foreach (var statement in Statements(patch, limits, errors))
        {
            if (statement.Syntax.Value is LiteralSyntax { Value.IsNull: true })
            {
                deleted.Add(statement.Syntax.Name);
                if (index.Remove(statement.Syntax.Name, out var at))
                {
                    merged[at] = null;
                }

                continue;
            }

            Put(statement);
        }

        var kept = merged.OfType<MergeStatement>().ToList();
        var collected = new List<string>();
        if (kept.Any(s => s.Syntax.Name == "root"))
        {
            var nodes = kept.Select(s => new GraphNode(s.Syntax.Name, s.Facts.Kind, s.Facts.References)).ToList();
            var (reached, _) = ProgramGraph.Walk(nodes, "root");
            collected.AddRange(kept.Where(s => !reached.Contains(s.Syntax.Name)).Select(s => s.Syntax.Name));
            kept.RemoveAll(s => !reached.Contains(s.Syntax.Name));
        }

        return new MergeResult(
            string.Join('\n', kept.Select(s => s.Text)),
            [.. kept.Select(s => s.Syntax.Name)],
            [.. deleted.Distinct(StringComparer.Ordinal)],
            [.. collected],
            OpenUIErrorList.Create(errors.Items.Select(d => d.Error)));
    }

    // Statements as written, after fences, comments and trimming (sections 1.2 and 1.4); each name
    // keeps its last definition. Text that is not a statement is dropped.
    private static List<MergeStatement> Statements(string text, OpenUILimits limits, DiagnosticBag errors)
    {
        var prepared = new CharBuffer();
        var preparer = new SourcePreparer(prepared);
        var segmenter = new Segmenter(limits.MaxStatementChars);
        var segments = new List<(Segment Segment, string Closers)>();
        if (text.Length > limits.MaxInputChars)
        {
            errors.Add(Limit($"The text is longer than {limits.MaxInputChars} characters; the rest was ignored."));
        }

        foreach (var c in text.AsSpan(0, Math.Min(text.Length, limits.MaxInputChars)))
        {
            preparer.Push(c);
        }

        preparer.Complete();
        var completed = new List<Segment>();
        segmenter.Scan(prepared, completed);
        segments.AddRange(completed.Select(segment => (segment, "")));
        if (segmenter.Tail(prepared.Length) is { } tail)
        {
            segments.Add((tail, tail.NeedsClose ? segmenter.Closers : ""));
        }

        var statements = new List<MergeStatement>();
        foreach (var (segment, closers) in segments)
        {
            if (segment.TooLong)
            {
                errors.Add(Limit($"A statement is longer than {limits.MaxStatementChars} characters and was dropped."));
            }
            else if (Read(prepared.Substring(segment.Start, segment.Length), closers, segment, limits, errors) is { } statement)
            {
                statements.Add(statement);
            }
        }

        return statements;
    }

    // A statement cut off where its text ended is written closed, or the merged program would read
    // everything after it as part of it.
    private static MergeStatement? Read(string source, string closers, Segment segment, OpenUILimits limits, DiagnosticBag errors)
    {
        try
        {
            var outcome = Parser.ParseStatement(source, segment.Start, segment.NeedsClose, limits.MaxSyntaxDepth);
            if (!outcome.TooDeep)
            {
                return outcome.Statement is { } syntax ? new MergeStatement(syntax, StatementFacts.Of(syntax), DropPartialEscape(source, closers).Trim()) : null;
            }
        }
        catch (InsufficientExecutionStackException)
        {
            // A tree within the limit can still be too tall for this thread's stack.
        }

        errors.Add(Limit($"A statement nests deeper than {limits.MaxSyntaxDepth} levels and was dropped."));
        return null;
    }

    // A \u escape cut before its fourth digit is dropped when reading (section 4, rule 2), so a closing
    // quote written after it must not make it an invalid escape.
    private static string DropPartialEscape(string source, string closers)
    {
        if (closers.Length > 0 && closers[0] == '"')
        {
            var digits = 0;
            while (digits < 3 && digits < source.Length && char.IsAsciiHexDigit(source[^(digits + 1)]))
            {
                digits++;
            }

            var u = source.Length - digits - 1;
            if (u >= 1 && source[u] == 'u' && source[u - 1] == '\\' && !IsEscaped(source, u - 1))
            {
                source = source[..(u - 1)];
            }
        }

        return source + closers;
    }

    private static bool IsEscaped(string source, int index)
    {
        var count = 0;
        while (index - count - 1 >= 0 && source[index - count - 1] == '\\')
        {
            count++;
        }

        return count % 2 == 1;
    }

    private static OpenUIError Limit(string message) =>
        new() { Source = OpenUIErrorSource.Parser, Code = OpenUIErrorCode.LimitExceeded, Message = message };

    private sealed record MergeStatement(StatementSyntax Syntax, StatementFacts Facts, string Text);
}
