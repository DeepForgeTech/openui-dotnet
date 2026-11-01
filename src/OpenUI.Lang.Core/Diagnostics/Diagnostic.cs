namespace OpenUI.Lang.Diagnostics;

/// <summary>When an error may be shown (language.md, section 8.2: <c>no-root</c> and enum checks wait for the stream to end).</summary>
internal enum DiagnosticTiming
{
    Immediate,
    StreamEnd,
}

/// <summary>An error plus the moment it becomes visible.</summary>
internal readonly record struct Diagnostic(OpenUIError Error, DiagnosticTiming Timing)
{
    /// <summary>Extension codes stay out of the error list, except limit hits, which are removals (language.md, section 8.1).</summary>
    public bool IsListed => Error.Code.IsSpecDefined || Error.Code == OpenUIErrorCode.LimitExceeded;
}

/// <summary>Collects diagnostics, reporting each error once, and stops at the limit with one marker (language.md, section 8.1).</summary>
internal sealed class DiagnosticBag
{
    private readonly HashSet<(string Code, string? StatementId, string? Component, string? Path, string Message)> _seen = [];
    private readonly List<Diagnostic> _items = [];
    private readonly int _maxErrors;

    public DiagnosticBag(int maxErrors) => _maxErrors = maxErrors;

    public IReadOnlyList<Diagnostic> Items => _items;

    /// <summary>The error added last, listed or not: a repeat or an overflow is still what just happened.</summary>
    public OpenUIError? LastAdded { get; private set; }

    public void Add(OpenUIError error, DiagnosticTiming timing = DiagnosticTiming.Immediate)
    {
        LastAdded = error;
        if (_items.Count > _maxErrors || !_seen.Add((error.Code.Value, error.StatementId, error.Component, error.Path, error.Message)))
        {
            return;
        }

        _items.Add(_items.Count == _maxErrors ? new Diagnostic(CutOff(_maxErrors), DiagnosticTiming.Immediate) : new Diagnostic(error, timing));
    }

    private static OpenUIError CutOff(int maxErrors) => new()
    {
        Source = OpenUIErrorSource.Parser,
        Code = OpenUIErrorCode.LimitExceeded,
        Message = $"More than {maxErrors} errors; the rest were dropped.",
    };

    public void AddRange(IEnumerable<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            Add(diagnostic.Error, diagnostic.Timing);
        }
    }

    /// <summary>The visible errors: stream-end ones only once the stream is final.</summary>
    public static OpenUIErrorList Visible(IEnumerable<Diagnostic> diagnostics, bool isFinal) =>
        OpenUIErrorList.Create(diagnostics.Where(d => d.IsListed && (isFinal || d.Timing == DiagnosticTiming.Immediate)).Select(d => d.Error));
}
