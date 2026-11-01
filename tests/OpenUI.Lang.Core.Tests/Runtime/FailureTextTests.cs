using System.Diagnostics;
using OpenUI.Lang.Core.Tests.Conformance;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Runtime;

public class FailureTextTests
{
    private const string Secret = "Login failed; Server=prod-sql-01;Password=S3cret";

    private static OpenUILibrary Library { get; } = ConformanceAdapter.CreateLibrary(FixtureCatalog.LoadLibrary());

    private static OpenUITools Throwing(Exception exception) =>
        new OpenUITools().Add("t", OpenUIToolEffect.Read, (_, _) => throw exception);

    [Fact]
    public async Task A_query_failure_keeps_the_exception_text_from_the_model()
    {
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = Throwing(new InvalidOperationException(Secret)) });
        session.Load("q = Query(\"t\", {})\nroot = Stack([])\n");
        await session.WhenIdleAsync();

        Assert.Equal("tool-error", Assert.Single(session.GetQueryStatus("q").Errors).Code.Value);
        Assert.DoesNotContain("S3cret", session.Current.Errors.ToModelFeedbackJson());
    }

    [Fact]
    public async Task A_mutation_failure_keeps_the_exception_text_from_the_model_and_the_page()
    {
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = Throwing(new InvalidOperationException(Secret)) });
        session.Load("m = Mutation(\"t\", {})\nroot = Stack([Text(\"err: \" + m.error), go])\ngo = Button(\"Go\", @Run(m))\n");
        Assert.True(session.Current.Root!["children"].Value.TryGetArray(out var children));
        Assert.True(children[1].TryGetComponent(out var go));
        Assert.True(go["action"].Value.TryGetAction(out var action));

        var outcome = await session.RunActionAsync(action, new ActionOrigin(go, null));

        Assert.Equal(ActionOutcomeKind.Stopped, outcome.Kind);
        Assert.DoesNotContain("S3cret", outcome.Error!.Message);
        Assert.DoesNotContain("S3cret", session.Current.Errors.ToModelFeedbackJson());
        Assert.DoesNotContain("S3cret", session.GetMutationStatus("m").Error);
        Assert.True(session.Current.Root!["children"].Value.TryGetArray(out var after));
        Assert.True(after[0].TryGetComponent(out var text));
        Assert.DoesNotContain("S3cret", text["text"].Value.ToString());
    }

    [Fact]
    public async Task A_tool_that_means_its_message_for_the_model_throws_the_dedicated_exception()
    {
        var tools = Throwing(new OpenUIToolException("No such location.", OpenUIErrorCode.ToolError));
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools });
        session.Load("q = Query(\"t\", {})\nroot = Stack([])\n");
        await session.WhenIdleAsync();

        Assert.Equal("No such location.", Assert.Single(session.GetQueryStatus("q").Errors).Message);
    }

    [Fact]
    public async Task A_host_reads_the_raw_exception_from_the_trace()
    {
        using var writer = new StringWriter();
        using var listener = new TextWriterTraceListener(writer);
        Trace.Listeners.Add(listener);
        try
        {
            await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = Throwing(new InvalidOperationException(Secret)) });
            session.Load("q = Query(\"t\", {})\nroot = Stack([])\n");
            await session.WhenIdleAsync();
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }

        listener.Flush();
        Assert.Contains("S3cret", writer.ToString());
    }

    [Fact]
    public void A_function_failure_keeps_the_exception_text_from_the_model()
    {
        var library = OpenUILibrary.FromSpec(
            LibrarySpec.FromJson(System.Text.Json.JsonSerializer.SerializeToElement(FixtureCatalog.LoadLibrary())),
            implement => implement.Function("Upper", (in FunctionArguments _) => throw new InvalidOperationException(Secret)));

        var render = OpenUIEvaluator.Evaluate(new OpenUIParser(library).Parse("root = Stack([Text(@Upper(\"a\"))])\n"));

        Assert.Contains("runtime-error", render.Errors.Select(e => e.Code.Value));
        Assert.DoesNotContain("S3cret", render.Errors.ToModelFeedbackJson());
    }

    [Fact]
    public async Task A_render_failure_keeps_the_exception_text_from_the_model()
    {
        await using var session = OpenUISession.Create(Library);
        session.Load("root = Stack([])\n");

        session.ReportRenderError(session.Current.Root!, new InvalidOperationException(Secret));

        Assert.Equal("render-error", Assert.Single(session.Current.Errors).Code.Value);
        Assert.DoesNotContain("S3cret", session.Current.Errors.ToModelFeedbackJson());
    }
}
