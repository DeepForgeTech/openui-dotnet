using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;
using OpenUI.Lang.Semantics;
using OpenUI.Lang.Syntax;

namespace OpenUI.Lang;

/// <summary>How the entry was chosen (language.md, section 2.2).</summary>
public enum EntryKind
{
    /// <summary>No entry: nothing renders, and at stream end a fatal <c>no-root</c> is reported (language.md, section 2.2).</summary>
    None,

    /// <summary>The statement named <c>root</c> (language.md, section 2.2).</summary>
    RootStatement,

    /// <summary>The first value statement whose value calls the library's root component; renders with a non-fatal <c>no-root</c> (language.md, section 2.2).</summary>
    Fallback,
}

/// <summary>What changed since the previous document of the same stream (language.md, section 4).</summary>
/// <param name="WasReset">True when the cache was discarded and the program parsed again from the start (section 4, rule 4).</param>
/// <param name="Completed">Statements that completed in this revision.</param>
/// <param name="PendingChanged">The pending statement, when its text changed.</param>
/// <param name="Removed">Statement names the previous document had and this one lacks.</param>
public sealed record DocumentDelta(bool WasReset, ImmutableArray<string> Completed, ImmutableArray<string> PendingChanged, ImmutableArray<string> Removed)
{
    /// <summary>No change (language.md, section 4).</summary>
    public static DocumentDelta None { get; } = new(false, [], [], []);
}

/// <summary>One statement of a document (language.md, section 2).</summary>
public sealed class Statement
{
    internal Statement(BoundStatement bound, bool isPending, bool neededImplicitClose)
    {
        Bound = bound;
        IsPending = isPending;
        NeededImplicitClose = neededImplicitClose;
    }

    /// <summary>The statement name (language.md, section 2).</summary>
    public string Name => Bound.Name;

    /// <summary>The statement kind (section 2.1).</summary>
    public StatementKind Kind => Bound.Kind;

    /// <summary>True for the pending tail of a stream, parsed after implicit closing (section 4, rule 2).</summary>
    public bool IsPending { get; }

    /// <summary>True when the pending tail needed implicit closing; such a statement never replaces a completed one (section 4, rule 3).</summary>
    public bool NeededImplicitClose { get; }

    /// <summary>The syntax (language.md, section 2).</summary>
    public StatementSyntax Syntax => Bound.Syntax;

    /// <summary>The statements this one references, in source order, without <c>@Each</c> loop variables (sections 2.3 and 3.5).</summary>
    public ImmutableArray<string> References => Bound.Facts.References;

    /// <summary>Where the statement sits in the prepared text (language.md, section 2).</summary>
    public SourceSpan Span => Bound.Syntax.Span;

    internal BoundStatement Bound { get; }
}

/// <summary>
/// An immutable parse result: the statements after last-wins, the entry, references, and the errors
/// found while parsing and binding (language.md, sections 2, 4 and 8). Thread-safe.
/// </summary>
public sealed class OpenUIDocument
{
    // What the properties below derive from the statements is computed on first use, each part apart:
    // a stream that is not read after every chunk does not pay for it after every chunk, and a render
    // never needs the reference graph.
    private readonly ImmutableArray<Diagnostic> _parseDiagnostics;
    private readonly bool _hasText;
    private readonly int _maxErrors;
    private DocumentIndex? _index;
    private DocumentReferences? _references;
    private DocumentDiagnostics? _diagnostics;

    internal OpenUIDocument(
        OpenUILibrary library,
        long revision,
        bool isFinal,
        bool incomplete,
        ImmutableArray<Statement> statements,
        bool hasText,
        ImmutableArray<Diagnostic> parseDiagnostics,
        int maxErrors,
        DocumentDelta delta)
    {
        Library = library;
        Revision = revision;
        IsFinal = isFinal;
        Incomplete = incomplete;
        Statements = statements;
        Delta = delta;
        _hasText = hasText;
        _parseDiagnostics = parseDiagnostics;
        _maxErrors = maxErrors;
    }

    /// <summary>A document that holds only <paramref name="error"/>, for a parser that failed.</summary>
    internal OpenUIDocument(OpenUILibrary library, long revision, bool isFinal, Diagnostic error)
    {
        Library = library;
        Revision = revision;
        IsFinal = isFinal;
        Statements = [];
        Delta = DocumentDelta.None;
        _index = new DocumentIndex(null, EntryKind.None, new Dictionary<string, Statement>());
        _references = new DocumentReferences([], []);
        _diagnostics = new DocumentDiagnostics([error], isFinal);
    }

    /// <summary>Counts documents of one stream, starting at 1 (language.md, sections 2, 4 and 8).</summary>
    public long Revision { get; }

    /// <summary>True once the stream has ended (section 4, rule 7).</summary>
    public bool IsFinal { get; }

    /// <summary>True when the input ended inside an unclosed string or bracket (Appendix B) (language.md, sections 2, 4 and 8).</summary>
    public bool Incomplete { get; }

    /// <summary>The statements after last-wins, in source order (section 2.4).</summary>
    public ImmutableArray<Statement> Statements { get; }

    /// <summary>The entry statement, or null (section 2.2).</summary>
    public Statement? Entry => Index.Entry;

    /// <summary>How <see cref="Entry"/> was chosen (language.md, sections 2, 4 and 8).</summary>
    public EntryKind EntryKind => Index.EntryKind;

    /// <summary>Referenced names with no statement, and references that close a cycle (section 2.3).</summary>
    public ImmutableArray<string> Unresolved => References.Unresolved;

    /// <summary>Value statements the entry cannot reach (section 2.3).</summary>
    public ImmutableArray<string> Orphans => References.Orphans;

    /// <summary>
    /// The parse and bind errors visible now: errors that wait for stream end (enum checks,
    /// <c>no-root</c>) appear only once <see cref="IsFinal"/> (section 8.2). Extension codes other
    /// than <c>x-limit-exceeded</c> are left out.
    /// </summary>
    public OpenUIErrorList Errors => Derived.Errors;

    /// <summary>Every parse and bind diagnostic, whatever its timing, extension codes included (language.md, sections 2, 4 and 8).</summary>
    public OpenUIErrorList Diagnostics => Derived.Listed;

    /// <summary>What changed since the previous document of the stream (language.md, sections 2, 4 and 8).</summary>
    public DocumentDelta Delta { get; }

    internal OpenUILibrary Library { get; }

    internal ImmutableArray<Diagnostic> AllDiagnostics => Derived.All;

    /// <summary>Looks up a statement by name (language.md, sections 2, 4 and 8).</summary>
    public bool TryGetStatement(string name, [NotNullWhen(true)] out Statement? statement) => Index.ByName.TryGetValue(name, out statement);

    // Two threads that arrive together both derive a part, which depends on nothing but this
    // document; the first to finish is kept.
    private DocumentIndex Index =>
        Volatile.Read(ref _index) ?? (Interlocked.CompareExchange(ref _index, Assembler.Index(Library, Statements), null) ?? _index!);

    private DocumentReferences References =>
        Volatile.Read(ref _references) ?? (Interlocked.CompareExchange(ref _references, Assembler.References(Statements, Entry), null) ?? _references!);

    private DocumentDiagnostics Derived =>
        Volatile.Read(ref _diagnostics) ?? (Interlocked.CompareExchange(
            ref _diagnostics,
            Assembler.Diagnostics(Library, IsFinal, Statements, Index, _hasText, _parseDiagnostics, _maxErrors),
            null) ?? _diagnostics!);
}
