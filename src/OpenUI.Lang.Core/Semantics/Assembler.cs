using System.Collections.Immutable;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;
using OpenUI.Lang.Syntax;

namespace OpenUI.Lang.Semantics;

/// <summary>A statement as the reference graph sees it.</summary>
internal readonly record struct GraphNode(string Name, StatementKind Kind, ImmutableArray<string> References);

/// <summary>
/// Reachability over the static reference graph (language.md, section 2.3), shared by orphans,
/// unresolved names and the edit merge's clean-up (section 7). Roots are the entry plus every state,
/// query and mutation statement; with no entry, every statement is a root.
/// </summary>
internal static class ProgramGraph
{
    public static (HashSet<string> Reached, List<string> Unresolved) Walk(IReadOnlyList<GraphNode> statements, string? entry)
    {
        var byName = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        foreach (var statement in statements)
        {
            byName[statement.Name] = statement;
        }

        var roots = new List<string>();
        if (entry is not null)
        {
            roots.Add(entry);
        }

        roots.AddRange(statements.Where(s => entry is null || s.Kind != StatementKind.Value).Select(s => s.Name));

        var reached = new HashSet<string>(StringComparer.Ordinal);
        var unresolved = new List<string>();
        var unresolvedSet = new HashSet<string>(StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<(string Name, int Next)>();
        foreach (var root in roots)
        {
            if (!byName.ContainsKey(root))
            {
                if (unresolvedSet.Add(root))
                {
                    unresolved.Add(root);
                }

                continue;
            }

            if (!reached.Add(root))
            {
                continue;
            }

            // Iterative DFS in source order; a reference to a statement still on the stack closes a cycle.
            stack.Push((root, 0));
            onStack.Add(root);
            while (stack.Count > 0)
            {
                var (name, next) = stack.Pop();
                var references = byName[name].References;
                if (next >= references.Length)
                {
                    onStack.Remove(name);
                    continue;
                }

                stack.Push((name, next + 1));
                var target = references[next];
                if (!byName.ContainsKey(target) || onStack.Contains(target))
                {
                    if (unresolvedSet.Add(target))
                    {
                        unresolved.Add(target);
                    }
                }
                else if (reached.Add(target))
                {
                    stack.Push((target, 0));
                    onStack.Add(target);
                }
            }
        }

        return (reached, unresolved);
    }
}

/// <summary>The entry of a document and a lookup of its statements by name.</summary>
internal sealed class DocumentIndex(Statement? entry, EntryKind entryKind, IReadOnlyDictionary<string, Statement> byName)
{
    public Statement? Entry { get; } = entry;

    public EntryKind EntryKind { get; } = entryKind;

    public IReadOnlyDictionary<string, Statement> ByName { get; } = byName;
}

/// <summary>What the reference graph says about a document (language.md, section 2.3).</summary>
internal sealed class DocumentReferences(ImmutableArray<string> unresolved, ImmutableArray<string> orphans)
{
    public ImmutableArray<string> Unresolved { get; } = unresolved;

    public ImmutableArray<string> Orphans { get; } = orphans;
}

/// <summary>Every diagnostic of a document and the lists a host sees.</summary>
internal sealed class DocumentDiagnostics(ImmutableArray<Diagnostic> all, bool isFinal)
{
    public ImmutableArray<Diagnostic> All { get; } = all;

    public OpenUIErrorList Errors { get; } = DiagnosticBag.Visible(all, isFinal);

    public OpenUIErrorList Listed { get; } = OpenUIErrorList.Create(all.Select(d => d.Error));
}

/// <summary>Derives what a document holds from its statements (language.md, sections 2.2 to 2.4 and 4).</summary>
internal static class Assembler
{
    public static DocumentIndex Index(OpenUILibrary library, ImmutableArray<Statement> statements)
    {
        var (entry, entryKind) = FindEntry(library, statements);
        var byName = new Dictionary<string, Statement>(statements.Length, StringComparer.Ordinal);
        foreach (var statement in statements)
        {
            byName[statement.Name] = statement;
        }

        return new DocumentIndex(entry, entryKind, byName);
    }

    public static DocumentReferences References(ImmutableArray<Statement> statements, Statement? entry)
    {
        var nodes = statements.Select(s => new GraphNode(s.Name, s.Kind, s.References)).ToList();
        var (reached, unresolved) = ProgramGraph.Walk(nodes, entry?.Name);
        var orphans = entry is null
            ? []
            : statements.Where(s => s.Kind == StatementKind.Value && !reached.Contains(s.Name)).Select(s => s.Name).ToImmutableArray();
        return new DocumentReferences([.. unresolved], orphans);
    }

    // parseDiagnostics are the input and statement limits that were hit, which come before the statements' own diagnostics.
    public static DocumentDiagnostics Diagnostics(
        OpenUILibrary library,
        bool isFinal,
        ImmutableArray<Statement> statements,
        DocumentIndex index,
        bool hasText,
        ImmutableArray<Diagnostic> parseDiagnostics,
        int maxErrors)
    {
        var errors = new DiagnosticBag(maxErrors);
        errors.AddRange(parseDiagnostics);
        foreach (var statement in statements)
        {
            errors.AddRange(statement.Bound.Diagnostics);
        }

        // Outside the bag: a cut list must still say that nothing renders.
        var diagnostics = new List<Diagnostic>(errors.Items);
        if (statements.IsEmpty && hasText)
        {
            diagnostics.Add(Fatal(OpenUIErrorCode.ParseFailed, "The response has text but no statements."));
        }
        else if (index.EntryKind == EntryKind.None)
        {
            diagnostics.Add(Fatal(OpenUIErrorCode.NoRoot, library.RootComponent is null
                ? "There is no root statement."
                : $"There is no root statement and no statement calls {library.RootComponent}."));
        }
        else if (index.EntryKind == EntryKind.Fallback)
        {
            diagnostics.Add(new Diagnostic(
                new OpenUIError
                {
                    Source = OpenUIErrorSource.Parser,
                    Code = OpenUIErrorCode.NoRoot,
                    Message = $"There is no root statement; rendering \"{index.Entry!.Name}\", the first call to {library.RootComponent}.",
                    IsWarning = true,
                },
                DiagnosticTiming.StreamEnd));
        }

        return new DocumentDiagnostics([.. diagnostics], isFinal);
    }

    private static (Statement? Entry, EntryKind Kind) FindEntry(OpenUILibrary library, ImmutableArray<Statement> statements)
    {
        if (statements.FirstOrDefault(s => s.Name == "root") is { } root)
        {
            return (root, EntryKind.RootStatement);
        }

        var fallback = library.RootComponent is { } rootComponent
            ? statements.FirstOrDefault(s => s.Kind == StatementKind.Value
                && s.Syntax.Value is CallSyntax { Head: CallHeadKind.Component } call && call.Name == rootComponent)
            : null;
        return fallback is null ? (null, EntryKind.None) : (fallback, EntryKind.Fallback);
    }

    private static Diagnostic Fatal(OpenUIErrorCode code, string message) =>
        new(new OpenUIError { Source = OpenUIErrorSource.Parser, Code = code, Message = message }, DiagnosticTiming.StreamEnd);
}
