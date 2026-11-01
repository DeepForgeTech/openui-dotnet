namespace OpenUI.Lang.Samples.BlazorApp.Weather;

/// <summary>One finished tool call.</summary>
/// <param name="At">When it finished.</param>
/// <param name="Tool">The tool name.</param>
/// <param name="Request">The method and path it sent.</param>
/// <param name="Arguments">The arguments as JSON.</param>
/// <param name="Status">The HTTP status, or null when no response came.</param>
/// <param name="Error">Why it failed, or null when it succeeded.</param>
public sealed record ToolCall(DateTimeOffset At, string Tool, string Request, string Arguments, int? Status, string? Error);

/// <summary>What the programs on this page sent to the API, newest first. One per circuit.</summary>
public sealed class ToolCallLog
{
    private const int MaxEntries = 40;

    private readonly Lock _gate = new();
    private readonly List<ToolCall> _calls = [];

    /// <summary>Raised after a call is added, on the thread that made the call.</summary>
    public event Action? Changed;

    /// <summary>The calls so far, newest first.</summary>
    public IReadOnlyList<ToolCall> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    /// <summary>Records a finished call.</summary>
    public void Add(ToolCall call)
    {
        lock (_gate)
        {
            _calls.Insert(0, call);
            if (_calls.Count > MaxEntries)
            {
                _calls.RemoveAt(MaxEntries);
            }
        }

        Changed?.Invoke();
    }
}
