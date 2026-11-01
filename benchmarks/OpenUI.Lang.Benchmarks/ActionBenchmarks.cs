using BenchmarkDotNet.Attributes;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Benchmarks;

/// <summary>Clicking a button: <see cref="OpenUISession.RunActionAsync"/> on the action of a rendered button.</summary>
[MemoryDiagnoser]
public class ActionBenchmarks
{
    private OpenUISession _session = null!;
    private OpenUISession _formSession = null!;
    private OuiAction _action = null!;
    private OuiAction _formAction = null!;
    private ActionOrigin _origin;
    private ActionOrigin _formOrigin;

    [ParamsAllValues]
    public Size Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _session = Corpus.Mount(Corpus.Interactive(Size), Corpus.Rows(Size), new IgnoreEvents());
        (_action, _origin) = Button(_session, formName: null);

        var fields = Corpus.FormFields(Size);
        _formSession = Corpus.Mount(Corpus.Form(fields, "{required: true, email: true, maxLength: 80}"), rows: 0, new IgnoreEvents());
        for (var i = 1; i <= fields; i++)
        {
            _formSession.Forms.Set("signup", $"field{i}", $"person{i}@example.com", "input");
        }

        (_formAction, _formOrigin) = Button(_formSession, formName: "signup");
        Check.That(Click().AsTask().GetAwaiter().GetResult().Kind == ActionOutcomeKind.Completed, "the click does not complete");
        Check.That(ClickInForm().AsTask().GetAwaiter().GetResult().Kind == ActionOutcomeKind.Completed, "the form click does not complete");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _formSession.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Set a state variable and send the assistant a message (the several-actions fixture, or a dashboard button).</summary>
    [Benchmark]
    public ValueTask<ActionOutcome> Click() => _session.RunActionAsync(_action, _origin);

    /// <summary>A button inside a form: every field is checked against its rules before the action runs.</summary>
    [Benchmark]
    public ValueTask<ActionOutcome> ClickInForm() => _formSession.RunActionAsync(_formAction, _formOrigin);

    private static (OuiAction Action, ActionOrigin Origin) Button(OpenUISession session, string? formName)
    {
        var button = Corpus.LastButton(session.Current.Root!);
        Check.That(button["action"].Value.TryGetAction(out var action), "the button has no action");
        return (action!, new ActionOrigin(button, formName));
    }

    internal sealed class IgnoreEvents : IOpenUIHostEventHandler
    {
        public ValueTask OnEventAsync(OpenUIHostEvent e, CancellationToken ct) => ValueTask.CompletedTask;
    }
}
