using System.Text.Json.Nodes;

namespace OpenUI.Lang.Core.Tests.Conformance;

/// <summary>The Level 2 comparison rules, proved on hand-written JSON with no adapter involved.</summary>
public sealed class InteractionComparerTests
{
    private static IReadOnlyList<Difference> Compare(string expected, string actual) =>
        InteractionComparer.Compare((JsonObject)JsonNode.Parse(expected)!, (JsonObject)JsonNode.Parse(actual)!);

    [Fact]
    public void Events_compare_in_order()
    {
        var a = """{ "type": "continue_conversation", "humanFriendlyMessage": "A" }""";
        var b = """{ "type": "continue_conversation", "humanFriendlyMessage": "B" }""";

        Assert.Empty(Compare($$"""{ "events": [{{a}}, {{b}}] }""", $$"""{ "events": [{{a}}, {{b}}] }"""));
        Assert.NotEmpty(Compare($$"""{ "events": [{{a}}, {{b}}] }""", $$"""{ "events": [{{b}}, {{a}}] }"""));
    }

    [Fact]
    public void A_different_number_of_events_differs()
    {
        var event1 = """{ "type": "continue_conversation" }""";

        var fewer = Compare($$"""{ "events": [{{event1}}, {{event1}}] }""", $$"""{ "events": [{{event1}}] }""");
        var more = Compare($$"""{ "events": [{{event1}}] }""", $$"""{ "events": [{{event1}}, {{event1}}] }""");

        Assert.Contains(fewer, d => d.Path == "events" && d.Problem.Contains("count"));
        Assert.Contains(more, d => d.Path == "events" && d.Problem.Contains("count"));
    }

    [Fact]
    public void An_event_is_compared_on_the_keys_it_lists_only()
    {
        var differences = Compare(
            """{ "events": [{ "type": "continue_conversation" }] }""",
            """{ "events": [{ "type": "continue_conversation", "humanFriendlyMessage": "Save", "params": { "context": 1 }, "formState": {} }] }""");

        Assert.Empty(differences);
    }

    [Fact]
    public void A_listed_event_key_must_match()
    {
        var differences = Compare(
            """{ "events": [{ "type": "continue_conversation", "humanFriendlyMessage": "Save" }] }""",
            """{ "events": [{ "type": "continue_conversation", "humanFriendlyMessage": "Send" }] }""");

        var difference = Assert.Single(differences);
        Assert.Equal("events[0].humanFriendlyMessage", difference.Path);
    }

    [Fact]
    public void A_listed_key_the_event_lacks_differs()
    {
        var differences = Compare(
            """{ "events": [{ "type": "continue_conversation", "formName": "contact" }] }""",
            """{ "events": [{ "type": "continue_conversation" }] }""");

        Assert.NotEmpty(differences);
    }

    [Theory]
    [InlineData("""{ "type": "continue_conversation" }""")]
    [InlineData("""{ "type": "continue_conversation", "formName": null }""")]
    public void A_null_formName_means_the_key_is_absent_or_null(string actualEvent)
    {
        var differences = Compare(
            """{ "events": [{ "type": "continue_conversation", "formName": null }] }""",
            $$"""{ "events": [{{actualEvent}}] }""");

        Assert.Empty(differences);
    }

    [Fact]
    public void A_null_formName_does_not_match_a_form()
    {
        var differences = Compare(
            """{ "events": [{ "formName": null }] }""",
            """{ "events": [{ "formName": "contact" }] }""");

        Assert.NotEmpty(differences);
    }

    [Fact]
    public void Nested_event_values_compare_exactly()
    {
        var expected = """{ "events": [{ "params": { "context": { "orderId": 42 } } }] }""";

        Assert.Empty(Compare(expected, """{ "events": [{ "params": { "context": { "orderId": 42 } } }] }"""));

        var extra = Compare(expected, """{ "events": [{ "params": { "context": { "orderId": 42, "extra": true } } }] }""");
        var difference = Assert.Single(extra);
        Assert.Equal("events[0].params.context.extra", difference.Path);
    }

    [Fact]
    public void State_compares_key_by_key_and_ignores_keys_not_listed()
    {
        var differences = Compare(
            """{ "state": { "$a": 1 } }""",
            """{ "state": { "$a": 1, "$b": 2 } }""");

        Assert.Empty(differences);
    }

    [Fact]
    public void A_state_key_with_the_wrong_value_is_reported_by_name()
    {
        var differences = Compare("""{ "state": { "$a": 1, "$b": 2 } }""", """{ "state": { "$a": 1, "$b": 3 } }""");

        var difference = Assert.Single(differences);
        Assert.Equal("state.$b", difference.Path);
        Assert.Equal("2", difference.Expected);
        Assert.Equal("3", difference.Actual);
    }

    [Fact]
    public void A_state_key_that_is_absent_reads_as_null()
    {
        Assert.Empty(Compare("""{ "state": { "$a": null } }""", """{ "state": { } }"""));
        Assert.NotEmpty(Compare("""{ "state": { "$a": 1 } }""", """{ "state": { } }"""));
    }

    [Fact]
    public void Tool_calls_compare_in_order()
    {
        var a = """{ "tool": "a", "args": {} }""";
        var b = """{ "tool": "b", "args": {} }""";

        Assert.Empty(Compare($$"""{ "toolCalls": [{{a}}, {{b}}] }""", $$"""{ "toolCalls": [{{a}}, {{b}}] }"""));
        Assert.NotEmpty(Compare($$"""{ "toolCalls": [{{a}}, {{b}}] }""", $$"""{ "toolCalls": [{{b}}, {{a}}] }"""));
    }

    [Fact]
    public void Tool_call_arguments_compare_exactly()
    {
        var differences = Compare(
            """{ "toolCalls": [{ "tool": "createTicket", "args": { "title": "Bug" } }] }""",
            """{ "toolCalls": [{ "tool": "createTicket", "args": { "title": "Crash" } }] }""");

        var difference = Assert.Single(differences);
        Assert.Equal("toolCalls[0].args.title", difference.Path);
    }

    [Fact]
    public void Consecutive_identical_tool_calls_count_once()
    {
        var a = """{ "tool": "a", "args": {} }""";
        var b = """{ "tool": "b", "args": {} }""";

        Assert.Empty(Compare($$"""{ "toolCalls": [{{a}}, {{b}}] }""", $$"""{ "toolCalls": [{{a}}, {{a}}, {{a}}, {{b}}] }"""));
    }

    [Fact]
    public void Identical_tool_calls_that_are_not_consecutive_count_separately()
    {
        var a = """{ "tool": "a", "args": {} }""";
        var b = """{ "tool": "b", "args": {} }""";

        var differences = Compare($$"""{ "toolCalls": [{{a}}, {{b}}] }""", $$"""{ "toolCalls": [{{a}}, {{b}}, {{a}}] }""");

        Assert.NotEmpty(differences);
    }

    [Fact]
    public void An_empty_expected_tool_call_list_rejects_any_call()
    {
        Assert.Empty(Compare("""{ "toolCalls": [] }""", """{ }"""));
        Assert.NotEmpty(Compare("""{ "toolCalls": [] }""", """{ "toolCalls": [{ "tool": "a", "args": {} }] }"""));
    }

    [Fact]
    public void Tool_calls_are_not_checked_when_expected_omits_them()
    {
        Assert.Empty(Compare("""{ "events": [] }""", """{ "events": [], "toolCalls": [{ "tool": "a", "args": {} }] }"""));
    }
}
