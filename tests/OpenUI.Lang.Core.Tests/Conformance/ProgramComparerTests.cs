using System.Text.Json.Nodes;

namespace OpenUI.Lang.Core.Tests.Conformance;

/// <summary>The Level 1 comparison rules, proved on hand-written JSON with no adapter involved.</summary>
public sealed class ProgramComparerTests
{
    private const string Text = """{ "type": "Text", "props": { "text": "a" } }""";

    // A result that passes on every always-compared key.
    private static JsonObject Result() => (JsonObject)JsonNode.Parse(
        $$"""{ "root": {{Text}}, "errors": [], "unresolved": [], "orphans": [], "incomplete": false }""")!;

    // Each side is the baseline result with the given members added or replaced.
    private static IReadOnlyList<Difference> Compare(string expectedMembers, string actualMembers) =>
        ProgramComparer.Compare(With(Result(), expectedMembers), With(Result(), actualMembers));

    private static JsonObject With(JsonObject result, string members)
    {
        foreach (var (key, value) in (JsonObject)JsonNode.Parse("{" + members + "}")!)
        {
            result[key] = value?.DeepClone();
        }

        return result;
    }

    [Fact]
    public void Identical_results_have_no_differences() =>
        Assert.Empty(ProgramComparer.Compare(Result(), Result()));

    [Fact]
    public void A_different_entry_or_statement_list_is_a_difference()
    {
        Assert.Contains(Compare(""" "entry": "root" """, """ "entry": "page" """), d => d.Path == "entry");
        Assert.Contains(Compare(""" "statements": ["root", "a"] """, """ "statements": ["root"] """), d => d.Path.StartsWith("statements", StringComparison.Ordinal));
    }

    [Fact]
    public void The_canonical_result_holds_the_statement_names_and_the_entry()
    {
        var result = ConformanceAdapter.RunSource("root = Stack([a])\na = Text(\"x\")\n", FixtureCatalog.LoadLibrary());

        Assert.Equal("root", result["entry"]!.GetValue<string>());
        Assert.Equal(["root", "a"], result["statements"]!.AsArray().Select(name => name!.GetValue<string>()));
    }

    [Fact]
    public void Errors_compare_in_any_order()
    {
        var differences = Compare(
            """ "errors": [{ "code": "a", "statementId": "x" }, { "code": "b", "statementId": "y" }] """,
            """ "errors": [{ "code": "b", "statementId": "y" }, { "code": "a", "statementId": "x" }] """);

        Assert.Empty(differences);
    }

    [Fact]
    public void Errors_are_a_multiset_so_duplicates_count()
    {
        var one = """ "errors": [{ "code": "a", "statementId": "x" }] """;
        var two = """ "errors": [{ "code": "a", "statementId": "x" }, { "code": "a", "statementId": "x" }] """;

        Assert.NotEmpty(Compare(one, two));
        Assert.NotEmpty(Compare(two, one));
        Assert.Empty(Compare(two, two));
    }

    [Fact]
    public void Errors_with_equal_counts_but_different_members_differ()
    {
        var differences = Compare(
            """ "errors": [{ "code": "a", "statementId": "x" }, { "code": "a", "statementId": "x" }] """,
            """ "errors": [{ "code": "a", "statementId": "x" }, { "code": "b", "statementId": "x" }] """);

        Assert.Equal(2, differences.Count);
    }

    [Fact]
    public void Unresolved_and_orphans_are_sets_so_duplicates_and_order_do_not_count()
    {
        var differences = Compare(
            """ "unresolved": ["a", "b"], "orphans": ["x", "y"] """,
            """ "unresolved": ["b", "a", "b", "a"], "orphans": ["y", "x", "x"] """);

        Assert.Empty(differences);
    }

    [Fact]
    public void The_same_duplicates_that_a_set_ignores_fail_as_a_multiset()
    {
        var asSet = Compare(""" "unresolved": ["a"] """, """ "unresolved": ["a", "a"] """);
        var asMultiset = Compare(
            """ "errors": [{ "code": "a" }] """,
            """ "errors": [{ "code": "a" }, { "code": "a" }] """);

        Assert.Empty(asSet);
        Assert.NotEmpty(asMultiset);
    }

    [Fact]
    public void Set_members_must_still_match()
    {
        var differences = Compare(""" "orphans": ["a"] """, """ "orphans": ["b"] """);

        Assert.Equal(2, differences.Count);
        Assert.Contains(differences, d => d.Problem.Contains("missing"));
        Assert.Contains(differences, d => d.Problem.Contains("unexpected"));
    }

    [Fact]
    public void An_error_without_statementId_matches_any_statement()
    {
        var differences = Compare(
            """ "errors": [{ "code": "no-root" }] """,
            """ "errors": [{ "code": "no-root", "statementId": "whatever" }] """);

        Assert.Empty(differences);
    }

    [Fact]
    public void An_error_with_statementId_matches_only_that_statement()
    {
        var differences = Compare(
            """ "errors": [{ "code": "a", "statementId": "x" }] """,
            """ "errors": [{ "code": "a", "statementId": "y" }] """);

        Assert.NotEmpty(differences);
    }

    [Fact]
    public void A_wildcard_error_still_needs_an_actual_entry_of_its_own()
    {
        var differences = Compare(
            """ "errors": [{ "code": "a" }, { "code": "a" }] """,
            """ "errors": [{ "code": "a", "statementId": "x" }] """);

        Assert.NotEmpty(differences);
    }

    [Fact]
    public void A_wildcard_does_not_use_up_the_actual_entry_a_specific_one_needs()
    {
        // The wildcard is listed first and could take either actual entry; only one choice lets
        // the specific entry match, so the comparison must look past the first fit.
        var differences = Compare(
            """ "errors": [{ "code": "a" }, { "code": "a", "statementId": "x" }] """,
            """ "errors": [{ "code": "a", "statementId": "x" }, { "code": "a", "statementId": "y" }] """);

        Assert.Empty(differences);
    }

    [Fact]
    public void Error_objects_compare_only_the_listed_keys()
    {
        var differences = Compare(
            """ "errorObjects": [{ "code": "missing-required", "path": "/value" }] """,
            """ "errorObjects": [{ "source": "parser", "code": "missing-required", "statementId": "chart", "component": "Metric", "path": "/value", "message": "text", "hint": "more text" }] """);

        Assert.Empty(differences);
    }

    [Fact]
    public void A_listed_error_object_key_must_match()
    {
        var differences = Compare(
            """ "errorObjects": [{ "code": "missing-required", "path": "/value" }] """,
            """ "errorObjects": [{ "code": "missing-required", "path": "/other" }] """);

        var difference = Assert.Single(differences);
        Assert.Equal("errorObjects[0]", difference.Path);
    }

    [Fact]
    public void Error_objects_are_ignored_when_expected_omits_them()
    {
        Assert.Empty(Compare("", """ "errorObjects": [{ "code": "x" }] """));
    }

    [Fact]
    public void Root_compares_exactly()
    {
        var differences = Compare(
            """ "root": { "type": "Text", "props": { "text": "a", "variant": "h1" } } """,
            """ "root": { "type": "Text", "props": { "text": "a" } } """);

        var difference = Assert.Single(differences);
        Assert.Equal("root.props.variant", difference.Path);
        Assert.Equal("missing in actual", difference.Problem);
    }

    [Fact]
    public void Root_rejects_extra_props_in_actual()
    {
        var differences = Compare(
            """ "root": { "type": "Text", "props": { "text": "a" } } """,
            """ "root": { "type": "Text", "props": { "text": "a", "variant": "h1" } } """);

        var difference = Assert.Single(differences);
        Assert.Equal("unexpected in actual", difference.Problem);
    }

    [Fact]
    public void Root_ignores_key_order_but_not_array_order()
    {
        Assert.Empty(Compare(
            """ "root": { "type": "Text", "props": { "a": 1, "b": 2 } } """,
            """ "root": { "props": { "b": 2, "a": 1 }, "type": "Text" } """));

        Assert.NotEmpty(Compare(
            """ "root": { "children": [1, 2] } """,
            """ "root": { "children": [2, 1] } """));
    }

    [Fact]
    public void Root_distinguishes_a_null_prop_from_an_absent_one()
    {
        Assert.NotEmpty(Compare(
            """ "root": { "props": { "a": null } } """,
            """ "root": { "props": { } } """));
    }

    [Fact]
    public void Numbers_compare_by_value()
    {
        Assert.Empty(Compare(""" "root": { "n": 1 } """, """ "root": { "n": 1.0 } """));
        Assert.NotEmpty(Compare(""" "root": { "n": 1 } """, """ "root": { "n": "1" } """));
    }

    [Fact]
    public void Incomplete_compares_exactly()
    {
        Assert.NotEmpty(Compare(""" "incomplete": true """, """ "incomplete": false """));
    }

    [Fact]
    public void State_compares_exactly_when_expected_has_it()
    {
        Assert.Empty(Compare(""" "state": { "$a": 1 } """, """ "state": { "$a": 1 } """));
        Assert.NotEmpty(Compare(""" "state": { "$a": 1 } """, """ "state": { "$a": 2 } """));
        Assert.NotEmpty(Compare(""" "state": { "$a": 1 } """, """ "state": { "$a": 1, "$b": null } """));
    }

    [Fact]
    public void State_and_statements_are_ignored_when_expected_omits_them()
    {
        Assert.Empty(Compare("", """ "state": { "$a": 1 }, "statements": ["root"] """));
    }

    [Fact]
    public void Statements_compare_exactly_and_in_order()
    {
        Assert.Empty(Compare(""" "statements": ["root", "a"] """, """ "statements": ["root", "a"] """));
        Assert.NotEmpty(Compare(""" "statements": ["root", "a"] """, """ "statements": ["a", "root"] """));
    }

    [Fact]
    public void Root_only_comparison_ignores_every_other_key()
    {
        var expected = With(Result(), """ "errors": [{ "code": "a" }] """);
        var actual = With(Result(), """ "orphans": ["x"] """);

        Assert.Empty(ProgramComparer.CompareRoot(expected, actual));
    }

    [Fact]
    public void Root_only_comparison_reports_a_changed_or_missing_root()
    {
        var changed = With(Result(), """ "root": { "type": "Text", "props": { "text": "b" } } """);
        var missing = Result();
        missing.Remove("root");

        Assert.Equal("root.props.text", Assert.Single(ProgramComparer.CompareRoot(Result(), changed)).Path);
        Assert.Equal("missing in actual", Assert.Single(ProgramComparer.CompareRoot(Result(), missing)).Problem);
    }

    [Fact]
    public void A_key_the_actual_result_lacks_is_reported()
    {
        var actual = Result();
        actual.Remove("orphans");

        var difference = Assert.Single(ProgramComparer.Compare(Result(), actual));
        Assert.Equal("orphans", difference.Path);
        Assert.Equal("missing in actual", difference.Problem);
    }

    [Fact]
    public void A_failure_shows_the_path_and_both_sides()
    {
        var differences = Compare(
            """ "root": { "type": "Stack", "props": { "children": [{ "props": { "text": "a" } }, { "props": { "text": "b" } }] } } """,
            """ "root": { "type": "Stack", "props": { "children": [{ "props": { "text": "a" } }, { "props": { "text": "c" } }] } } """);

        var message = Difference.Format("errors/001-example", differences);

        Assert.Contains("errors/001-example: 1 difference(s)", message);
        Assert.Contains("root.props.children[1].props.text: differs", message);
        Assert.Contains("expected: \"b\"", message);
        Assert.Contains("actual:   \"c\"", message);
    }

    [Fact]
    public void A_missing_error_is_shown_with_the_errors_that_were_produced()
    {
        var differences = Compare(
            """ "errors": [{ "code": "a", "statementId": "x" }] """,
            """ "errors": [{ "code": "b", "statementId": "y" }] """);

        var message = Difference.Format("case", differences);

        Assert.Contains("""expected: {"code":"a","statementId":"x"}""", message);
        Assert.Contains("""actual:   [{"code":"b","statementId":"y"}]""", message);
    }
}
