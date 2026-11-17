using System.Collections.Immutable;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Semantics;

/// <summary>
/// What one session's last render pass recorded for each statement it evaluated, so the next pass can take a
/// statement's result again when nothing it read has changed: a stream redraws after every chunk, and a state write
/// changes a few statements of many. One session owns it and passes it to its own render passes only.
/// </summary>
internal sealed class EvaluationReuse
{
    private IReadOnlyDictionary<string, StatementRecord> _last = Empty;
    private bool _lastWasFinal;

    private static IReadOnlyDictionary<string, StatementRecord> Empty { get; } = new Dictionary<string, StatementRecord>(StringComparer.Ordinal);

    /// <summary>
    /// The records a pass over a document may reuse. An error waits for stream end or not depending on whether the
    /// document is final, so a pass over a final document reuses nothing from one over a streaming document, and back.
    /// </summary>
    public IReadOnlyDictionary<string, StatementRecord> For(bool isFinal)
    {
        Pass++;
        return isFinal == _lastWasFinal ? _last : Empty;
    }

    /// <summary>Numbers the passes, so a record can keep what the pass that reads it found out without a table per pass.</summary>
    public int Pass { get; private set; }

    /// <summary>Keeps what a finished pass recorded, for the next one.</summary>
    public void Keep(IReadOnlyDictionary<string, StatementRecord> records, bool isFinal)
    {
        _last = records;
        _lastWasFinal = isFinal;
    }
}

/// <summary>
/// One statement's evaluation: its result, and everything it did in order, so taking the result again does the same.
/// Fuel, text and depth are its own; what a statement it references costs is in that statement's record.
/// </summary>
internal sealed class StatementRecord(BoundStatement bound, Result result, ImmutableArray<RecordedStep> steps, long fuel, long text, int depth)
{
    public BoundStatement Bound { get; } = bound;

    public Result Result { get; } = result;

    public ImmutableArray<RecordedStep> Steps { get; } = steps;

    /// <summary>The steps it spent, besides those of the statements it references and of reading their names.</summary>
    public long Fuel { get; } = fuel;

    /// <summary>The characters it created, besides those of the statements it references.</summary>
    public long Text { get; } = text;

    /// <summary>How much deeper than its own start it nested, the statements it evaluated on the way included.</summary>
    public int Depth { get; } = depth;

    // What the pass numbered CheckedIn found: whether this record can stand for its statement. The session reads its records
    // under its lock, one pass at a time.
    internal int CheckedIn { get; set; }

    internal bool Reusable { get; set; }

    // What reusing it may cost, as the pass numbered BoundedIn worked it out.
    internal int BoundedIn { get; set; }

    internal ReuseBounds Bounds { get; set; }
}

/// <summary>The most reusing a statement may spend: fuel is Fuel plus FuelPerFrame for each frame open inside it.</summary>
internal readonly record struct ReuseBounds(long Fuel, long FuelPerFrame, long Text, long Depth);

internal enum RecordedStepKind : byte
{
    /// <summary>A reference to <see cref="RecordedStep.Name"/>, which <see cref="RecordedStep.Found"/> says existed.</summary>
    Reference,

    /// <summary>A read of the state variable <see cref="RecordedStep.Name"/>, which gave <see cref="RecordedStep.Value"/>.</summary>
    State,

    /// <summary>A read of the query data of <see cref="RecordedStep.Name"/>: <see cref="RecordedStep.Value"/> if <see cref="RecordedStep.Found"/>.</summary>
    Query,

    /// <summary>An error added to the pass's list.</summary>
    Report,
}

/// <summary>
/// One thing a statement did that its result depends on or that the pass sees. <see cref="Cost"/> is what reading a
/// referenced name costs per open frame, and <see cref="Depth"/> how deep below the statement's start the reference was.
/// </summary>
internal readonly record struct RecordedStep(
    RecordedStepKind Kind,
    string Name,
    bool Found = false,
    OuiValue Value = default,
    long Cost = 0,
    int Depth = 0,
    OpenUIError? Error = null,
    DiagnosticTiming Timing = DiagnosticTiming.Immediate)
{
    /// <summary>
    /// True when two values are the same value in every way an evaluation can tell: the same instance, or equal strings,
    /// or numbers with the same bits, so 0 and -0 differ. Values built again are not the same, which only costs a reuse.
    /// </summary>
    public static bool Same(OuiValue a, OuiValue b) =>
        ReferenceEquals(a.Payload, b.Payload)
            ? BitConverter.DoubleToInt64Bits(a.RawNumber) == BitConverter.DoubleToInt64Bits(b.RawNumber)
            : a.Payload is string left && b.Payload is string right && string.Equals(left, right, StringComparison.Ordinal);
}
