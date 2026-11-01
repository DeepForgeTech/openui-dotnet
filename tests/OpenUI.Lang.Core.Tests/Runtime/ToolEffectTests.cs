using OpenUI.Lang.Library;
using OpenUI.Lang.Core.Tests.Conformance;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Runtime;

public class ToolEffectTests
{
    private static OpenUILibrary Library { get; } = ConformanceAdapter.CreateLibrary(FixtureCatalog.LoadLibrary());

    private static OpenUITools Counting(Action onCall, OpenUIToolEffect? effect)
    {
        ValueTask<OuiValue> Handler(OuiValue args, CancellationToken ct)
        {
            onCall();
            return ValueTask.FromResult(OuiValue.Null);
        }

        return effect is { } declared ? new OpenUITools().Add("t", declared, Handler) : new OpenUITools().Add("t", Handler);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(OpenUIToolEffect.Mutating)]
    public async Task A_query_refuses_a_tool_not_declared_read(OpenUIToolEffect? effect)
    {
        var calls = 0;
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = Counting(() => calls++, effect) });

        session.Load("q = Query(\"t\", {id: 1})\nroot = Stack([])\n");
        await session.WhenIdleAsync();

        Assert.Equal(0, calls);
        var error = Assert.Single(session.GetQueryStatus("q").Errors);
        Assert.Equal("tool-error", error.Code.Value);
        Assert.Equal("t", error.ToolName);
    }

    [Fact]
    public async Task A_query_runs_a_tool_declared_read()
    {
        var calls = 0;
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = Counting(() => calls++, OpenUIToolEffect.Read) });

        session.Load("q = Query(\"t\", {id: 1})\nroot = Stack([])\n");
        await session.WhenIdleAsync();

        Assert.Equal(1, calls);
        Assert.Empty(session.GetQueryStatus("q").Errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(OpenUIToolEffect.Mutating)]
    [InlineData(OpenUIToolEffect.Read)]
    public async Task A_mutation_runs_a_tool_whatever_it_declares(OpenUIToolEffect? effect)
    {
        var calls = 0;
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = Counting(() => calls++, effect) });
        session.Load("m = Mutation(\"t\", {})\nroot = Stack([go])\ngo = Button(\"Go\", @Run(m))\n");
        Assert.True(session.Current.Root!["children"].Value.TryGetArray(out var children));
        Assert.True(children[0].TryGetComponent(out var go));
        Assert.True(go["action"].Value.TryGetAction(out var action));

        var outcome = await session.RunActionAsync(action, new ActionOrigin(go, null));

        Assert.Equal(ActionOutcomeKind.Completed, outcome.Kind);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task A_provider_that_declares_nothing_counts_as_mutating()
    {
        var tools = new BareProvider();
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools });

        session.Load("q = Query(\"t\", {})\nroot = Stack([])\n");
        await session.WhenIdleAsync();

        Assert.Equal(0, tools.Calls);
        Assert.Single(session.GetQueryStatus("q").Errors);
    }

    private sealed class BareProvider : IOpenUIToolProvider
    {
        public int Calls { get; private set; }

        public IReadOnlyCollection<string> ToolNames => ["t"];

        public bool Contains(string name) => name == "t";

        public ValueTask<OuiValue> InvokeAsync(string name, OuiValue args, CancellationToken ct)
        {
            Calls++;
            return ValueTask.FromResult(OuiValue.Null);
        }
    }
}
