namespace OpenUI.Lang;

/// <summary>
/// Budgets that keep hostile or broken input from exhausting the host. Going over one drops the
/// smallest unit and reports <c>x-limit-exceeded</c>; it never throws. The numbers are this
/// implementation's choice: language.md sets none. It requires that generated content never crash
/// the host (section 10) and that every removal is reported (section 8.1). A citation below is for
/// what happens at that limit; where the language says nothing, the member says so.
/// </summary>
public sealed record OpenUILimits
{
    /// <summary>The defaults (language.md, section 8.1).</summary>
    public static OpenUILimits Default { get; } = new();

    /// <summary>Characters of input read per stream; later chunks are ignored and the document is incomplete (language.md, section 8.1).</summary>
    public int MaxInputChars { get; init; } = 4 * 1024 * 1024;

    /// <summary>Characters in one statement; a longer statement is dropped (language.md, section 8.1).</summary>
    public int MaxStatementChars { get; init; } = 256 * 1024;

    /// <summary>Statements per program; later ones are dropped (language.md, section 8.1).</summary>
    public int MaxStatements { get; init; } = 10_000;

    /// <summary>Height of one statement's tree, where every operator, member read, index and bracket adds a level and parentheses add none; a taller statement is dropped (language.md, section 8.1).</summary>
    public int MaxSyntaxDepth { get; init; } = 128;

    /// <summary>Nesting of evaluation, counting expressions and statement references; deeper values become null (language.md, section 8.1).</summary>
    public int MaxEvalDepth { get; init; } = 256;

    /// <summary>Values (array items, object entries, components) one evaluated value may expand to; a larger value is dropped (language.md, section 8.1). A host that draws what it evaluates should lower this to what it can draw.</summary>
    public int MaxValueNodes { get; init; } = 100_000;

    /// <summary>Characters in one evaluated string (language.md, section 8.1).</summary>
    public int MaxStringLength { get; init; } = 1024 * 1024;

    /// <summary>Characters of text one pass may create, and one evaluated value may expand to (language.md, section 8.1).</summary>
    public int MaxTextChars { get; init; } = 16 * 1024 * 1024;

    /// <summary>Evaluation steps per pass: each expression, <c>@Each</c> iteration and <c>@Sort</c> comparison costs one, and walking a value costs its size (language.md, section 8.1).</summary>
    public int Fuel { get; init; } = 2_000_000;

    /// <summary>Errors kept per list; language.md sets no number.</summary>
    public int MaxErrors { get; init; } = 200;

    /// <summary>Nesting of arrays, objects, components and actions in one evaluated value; a deeper value is dropped (language.md, section 8.1).</summary>
    public int MaxValueDepth { get; init; } = 64;

    /// <summary>Steps in one action; a longer action is dropped (language.md, section 8.1).</summary>
    public int MaxActionSteps { get; init; } = 1000;

    /// <summary>Mutations one run of an action may call; the action stops at the next; language.md sets no number.</summary>
    public int MaxMutationsPerAction { get; init; } = 16;

    /// <summary>Tool calls running at once in a session; the rest wait their turn; language.md sets no number.</summary>
    public int MaxConcurrentToolCalls { get; init; } = 8;

    /// <summary>Fetches started per query per second; more wait and coalesce into one with the latest arguments; language.md sets no number.</summary>
    public int MaxFetchesPerSecond { get; init; } = 4;

    /// <summary>Fetches started by all of a session's queries per second; more wait their turn; language.md sets no number.</summary>
    public int MaxSessionFetchesPerSecond { get; init; } = 32;

    /// <summary>Queries in a session that refresh on a timer; the rest fetch once and report the limit; language.md sets no number.</summary>
    public int MaxRefreshingQueries { get; init; } = 64;
}

/// <summary>Options for <see cref="OpenUIParser"/> and <see cref="OpenUIValidator"/> (language.md, section 8.1).</summary>
public sealed record OpenUIParserOptions
{
    /// <summary>The defaults (language.md, section 8.1).</summary>
    public static OpenUIParserOptions Default { get; } = new();

    /// <summary>The budgets applied while parsing (language.md, section 8.1).</summary>
    public OpenUILimits Limits { get; init; } = OpenUILimits.Default;
}
