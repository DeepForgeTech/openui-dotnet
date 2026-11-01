using OpenUI.Lang.Core.Tests.Conformance;
using OpenUI.Lang.Library;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Runtime;

public class FormValidationTests
{
    private const string Page = """
        $sent = false
        root = Stack([signup, outside])
        signup = Form("signup", [email, code, send], [])
        email = Input("email", "Email", null, {required: true, email: true})
        code = Input("code", "Code", null, {maxLength: 3, pattern: "^[0-9]+$"})
        send = Button("Send", @Set($sent, true))
        outside = Button("Other", @Set($sent, true))
        """;

    private static OpenUILibrary Library { get; } = ConformanceAdapter.CreateLibrary(FixtureCatalog.LoadLibrary());

    private static async Task<(ActionOutcome Outcome, bool Sent)> ClickSendAsync(OpenUISession session)
    {
        var send = Find(session.Current.Root!, "send");
        Assert.True(send["action"].Value.TryGetAction(out var action));
        var outcome = await session.RunActionAsync(action, new ActionOrigin(send, "signup"));
        session.State.Get("$sent").TryGetBoolean(out var sent);
        return (outcome, sent);
    }

    private static OuiComponent Find(OuiComponent node, string statement)
    {
        if (node.StatementId == statement)
        {
            return node;
        }

        foreach (var prop in node.Props)
        {
            if (prop.Value.TryGetComponent(out var child) && TryFind(child, statement) is { } found)
            {
                return found;
            }

            if (prop.Value.TryGetArray(out var items))
            {
                foreach (var item in items)
                {
                    if (item.TryGetComponent(out var element) && TryFind(element, statement) is { } nested)
                    {
                        return nested;
                    }
                }
            }
        }

        throw new InvalidOperationException($"No {statement}.");
    }

    private static OuiComponent? TryFind(OuiComponent node, string statement)
    {
        try
        {
            return Find(node, statement);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    [Fact]
    public async Task An_action_inside_a_form_does_not_run_while_a_field_fails()
    {
        await using var session = OpenUISession.Create(Library);
        session.Load(Page);
        session.Forms.Set("signup", "email", "nope", "input");

        var (outcome, sent) = await ClickSendAsync(session);

        Assert.False(sent);
        Assert.Equal(ActionOutcomeKind.Invalid, outcome.Kind);
        Assert.Equal(0, outcome.StepsRun);
        Assert.Equal("email", Assert.Single(outcome.Validation!.Errors).Field);
    }

    [Fact]
    public async Task An_action_inside_a_form_runs_once_every_field_passes()
    {
        await using var session = OpenUISession.Create(Library);
        session.Load(Page);
        session.Forms.Set("signup", "email", "a@b.co", "input");
        session.Forms.Set("signup", "code", "123", "input");

        var (outcome, sent) = await ClickSendAsync(session);

        Assert.True(sent);
        Assert.Equal(ActionOutcomeKind.Completed, outcome.Kind);
    }

    [Fact]
    public async Task A_host_can_ask_for_each_fields_first_failure()
    {
        await using var session = OpenUISession.Create(Library);
        session.Load(Page);
        session.Forms.Set("signup", "code", "12345", "input");

        var validation = session.ValidateForm("signup");

        Assert.False(validation.IsValid);
        Assert.Equal(new[] { "email", "code" }, validation.Errors.Select(e => e.Field));
        Assert.Equal("This field is required", validation.Errors[0].Message);
        Assert.Equal("Must be no more than 3 characters", validation.Errors[1].Message);
    }

    [Fact]
    public async Task A_field_bound_to_state_is_checked_against_the_state_value()
    {
        const string Bound = """
            $mail = "nope"
            root = Form("f", [Input("email", "Email", $mail, {email: true})], [])
            """;
        await using var session = OpenUISession.Create(Library);
        session.Load(Bound);
        Assert.False(session.ValidateForm("f").IsValid);

        session.State.Set("$mail", "a@b.co");

        Assert.True(session.ValidateForm("f").IsValid);
    }

    [Fact]
    public async Task An_action_outside_a_form_is_not_checked_and_another_form_does_not_count()
    {
        await using var session = OpenUISession.Create(Library);
        session.Load(Page);

        var other = Find(session.Current.Root!, "outside");
        Assert.True(other["action"].Value.TryGetAction(out var action));
        var outcome = await session.RunActionAsync(action, new ActionOrigin(other, null));

        Assert.Equal(ActionOutcomeKind.Completed, outcome.Kind);
        Assert.True(session.ValidateForm("another").IsValid);
    }

    [Fact]
    public async Task Fields_that_share_a_pattern_are_each_checked_against_their_own_value()
    {
        await using var session = OpenUISession.Create(Library);
        var fields = string.Join(", ", Enumerable.Range(1, 6).Select(i => $"Input(\"f{i}\", \"x\", null, {{pattern: \"^[A-Z]{{2}}-[0-9]{{2}}$\"}})"));
        session.Load("root = Form(\"f\", [" + fields + "], [])\n");
        string[] values = ["AB-12", "nope", "CD-34", "", "EF-5", "GH-67"];
        for (var i = 0; i < values.Length; i++)
        {
            session.Forms.Set("f", $"f{i + 1}", values[i], "input");
        }

        var first = session.ValidateForm("f");
        var second = session.ValidateForm("f");

        Assert.Equal(["f2", "f5"], first.Errors.Select(e => e.Field).ToArray());
        Assert.All(first.Errors, e => Assert.Equal("Invalid format", e.Message));
        Assert.Equal(first.Errors.Select(e => (e.Field, e.Message)), second.Errors.Select(e => (e.Field, e.Message)));
    }

    [Fact]
    public async Task A_program_of_many_fields_stops_at_the_budget_and_does_not_pass()
    {
        var limits = OpenUILimits.Default with { Fuel = 20_000 };
        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Limits = limits });
        var fields = string.Join(", ", Enumerable.Range(0, 500).Select(i => $"Input(\"f{i}\", \"x\", $v, {{pattern: \"^{i}$\"}})"));
        session.Load("$v = \"x\"\nroot = Form(\"f\", [" + fields + "], [])\n");
        Assert.NotNull(session.Current.Root);

        var validation = session.ValidateForm("f");

        Assert.False(validation.IsValid);
        Assert.Contains("x-limit-exceeded", session.Current.Errors.Select(e => e.Code.Value));
    }
}
