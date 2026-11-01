using BenchmarkDotNet.Attributes;
using OpenUI.Lang.Runtime;

namespace OpenUI.Lang.Benchmarks;

/// <summary>A click that runs a mutation: the tool call, the status changes and the message to the assistant (the mutation-run fixture).</summary>
[MemoryDiagnoser]
public class MutationBenchmarks
{
    private OpenUISession _session = null!;
    private ActionBenchmarks.IgnoreEvents _events = null!;
    private Values.OuiAction _action = null!;
    private ActionOrigin _origin;

    [GlobalSetup]
    public void Setup()
    {
        _events = new ActionBenchmarks.IgnoreEvents();
        _session = Corpus.Mount(Corpus.MutationProgram, rows: 0, _events);
        var button = Corpus.LastButton(_session.Current.Root!);
        Check.That(button["action"].Value.TryGetAction(out var action), "the button has no action");
        (_action, _origin) = (action!, new ActionOrigin(button, null));
        for (var i = 0; i < 3; i++)
        {
            var outcome = ClickCreate().AsTask().GetAwaiter().GetResult();
            Check.That(outcome is { Kind: ActionOutcomeKind.Completed, StepsRun: 2 }, "the mutation click does not complete");
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _session.DisposeAsync().AsTask().GetAwaiter().GetResult();

    [Benchmark]
    public ValueTask<ActionOutcome> ClickCreate() => _session.RunActionAsync(_action, _origin);
}
