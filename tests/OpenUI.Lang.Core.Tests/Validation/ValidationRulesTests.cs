using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Validation;

public class ValidationRulesTests
{
    private static OuiObject Rules(params (string Name, OuiValue Value)[] entries) =>
        OuiObject.Create(entries.Select(entry => KeyValuePair.Create(entry.Name, entry.Value)));

    private static OuiValue ObjectOf(params (string Key, OuiValue Value)[] entries) => Rules(entries);

    [Fact]
    public void The_first_failing_rule_wins()
    {
        var rules = Rules(("required", true), ("email", true), ("minLength", 20.0));

        Assert.Equal("Please enter a valid email", ValidationRules.Check(rules, "nope"));
        Assert.Equal("This field is required", ValidationRules.Check(rules, ""));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Every_rule_but_required_skips_empty_values(string? empty)
    {
        var rules = Rules(("email", true), ("url", true), ("numeric", true), ("minLength", 3.0), ("maxLength", 1.0), ("min", 5.0), ("max", 1.0), ("pattern", "^x$"));

        Assert.Null(ValidationRules.Check(rules, empty));
    }

    [Fact]
    public void Empty_arrays_and_objects_are_empty()
    {
        var rules = Rules(("minLength", 3.0), ("pattern", "^x$"));

        Assert.Null(ValidationRules.Check(rules, OuiArray.Empty));
        Assert.Null(ValidationRules.Check(rules, OuiObject.Empty));
        Assert.Equal("This field is required", ValidationRules.Check(Rules(("required", true)), OuiObject.Empty));
    }

    [Fact]
    public void A_rule_set_to_false_or_null_is_off()
    {
        Assert.Null(ValidationRules.Check(Rules(("required", false), ("email", OuiValue.Null)), ""));
    }

    [Fact]
    public void Unknown_rules_are_ignored() =>
        Assert.Null(ValidationRules.Check(Rules(("nonsense", true)), "x"));

    [Fact]
    public void An_object_is_not_unwrapped_to_its_value_field()
    {
        // The reference treats { value: "" } form state as empty. Section 5.4 does not, so neither do we.
        var wrapped = ObjectOf(("value", ""));

        Assert.Equal("Please enter a valid email", ValidationRules.Check(Rules(("email", true)), wrapped));
    }

    [Fact]
    public void A_checkbox_group_with_nothing_ticked_fails_required()
    {
        var none = ObjectOf(("a", false), ("b", false));
        var one = ObjectOf(("a", false), ("b", true));
        var rules = Rules(("required", true));

        Assert.Equal("At least one option is required", ValidationRules.Check(rules, none));
        Assert.Null(ValidationRules.Check(rules, one));
    }

    [Theory]
    [InlineData("a@b.co", true)]
    [InlineData("a@b.c.d", true)]
    [InlineData("a b@c.d", false)]
    [InlineData("@b.c", false)]
    [InlineData("a@.c", false)]
    [InlineData("a@b.", false)]
    [InlineData("a@b", false)]
    [InlineData("a@@b.c", false)]
    [InlineData("a@b.c\n", false)]
    [InlineData("a@b.c\x00a0", false)]
    [InlineData("a@b.c\x0085", true)]
    public void Email_follows_the_regular_expression_in_section_5_4(string email, bool valid) =>
        Assert.Equal(valid, ValidationRules.Check(Rules(("email", true)), email) is null);

    [Fact]
    public void Numeric_accepts_a_prefix_number_like_parseFloat()
    {
        var rules = Rules(("numeric", true));

        Assert.Null(ValidationRules.Check(rules, "12abc"));
        Assert.Equal("Must be a number", ValidationRules.Check(rules, "abc"));
        Assert.Equal("Must be a number", ValidationRules.Check(rules, "  "));
        Assert.Null(ValidationRules.Check(rules, 3.0));
    }

    [Theory]
    [InlineData("min", 5.0, 4.0, "Must be at least 5")]
    [InlineData("min", 5.0, 5.0, null)]
    [InlineData("max", 5.0, 5.0, null)]
    [InlineData("max", 5.0, 6.0, "Must be no more than 5")]
    public void Min_and_max_compare_numbers(string rule, double limit, double value, string? message) =>
        Assert.Equal(message, ValidationRules.Check(Rules((rule, limit)), value));

    [Fact]
    public void Min_and_max_read_other_values_with_to_number()
    {
        // Comparisons use toNumber, where a string that is not a number is 0.
        Assert.Null(ValidationRules.Check(Rules(("min", 5.0)), "12"));
        Assert.Equal("Must be at least 5", ValidationRules.Check(Rules(("min", 5.0)), "12abc"));
        Assert.Equal("Must be at least 5", ValidationRules.Check(Rules(("min", 5.0)), "abc"));
        Assert.Equal("Must be at least 1", ValidationRules.Check(Rules(("min", "1")), false));
    }

    [Theory]
    [InlineData("minLength", 3.0, "ab", "Must be at least 3 characters")]
    [InlineData("minLength", 3.0, "abc", null)]
    [InlineData("maxLength", 3.0, "abc", null)]
    [InlineData("maxLength", 3.0, "abcd", "Must be no more than 3 characters")]
    public void Lengths_count_a_strings_characters(string rule, double limit, string value, string? message) =>
        Assert.Equal(message, ValidationRules.Check(Rules((rule, limit)), value));

    [Fact]
    public void A_length_rule_reads_only_strings() =>
        Assert.Null(ValidationRules.Check(Rules(("minLength", 3.0)), OuiArray.Create(1.0)));

    [Theory]
    [InlineData("(")]
    [InlineData("(?=a)a")]
    [InlineData("(a)\\1")]
    public void A_pattern_that_gives_no_answer_fails_the_rule(string pattern) =>
        Assert.Equal("Invalid format", ValidationRules.Check(Rules(("pattern", pattern)), "a"));

    [Fact]
    public void A_pass_compiles_a_pattern_once_and_gives_the_next_pass_its_own()
    {
        var first = new EcmaRegexPass();

        Assert.NotNull(first.Get("^[a-z]+$"));
        Assert.Same(first.Get("^[a-z]+$"), first.Get("^[a-z]+$"));
        Assert.NotSame(first.Get("^[a-z]+$"), first.Get("^[a-z]*$"));
        Assert.NotSame(first.Get("^[a-z]+$"), new EcmaRegexPass().Get("^[a-z]+$"));
        Assert.Null(first.Get("("));
        Assert.Null(first.Get("("));
    }

    [Fact]
    public void A_pass_lets_go_of_patterns_past_what_it_keeps()
    {
        var pass = new EcmaRegexPass();
        var patterns = Enumerable.Range(0, EcmaRegexPass.MaxKept * 3).Select(i => $"^q{i}$").ToArray();

        var kept = patterns.Count(p => ReferenceEquals(pass.Get(p), pass.Get(p)));

        Assert.Equal(EcmaRegexPass.MaxKept, kept);
        Assert.Same(pass.Get(patterns[0]), pass.Get(patterns[0]));
        Assert.Equal(true, pass.IsMatch(patterns[^1], $"q{patterns.Length - 1}"));
        Assert.Equal(false, pass.IsMatch(patterns[^1], "q0"));
    }

    [Fact]
    public void A_pattern_shared_through_a_pass_gives_each_value_its_own_answer()
    {
        var rules = Rules(("pattern", "^[A-Z]{2}-[0-9]{2}$"));
        var pass = new EcmaRegexPass();

        foreach (var (value, message) in new (string, string?)[] { ("AB-12", null), ("ab-12", "Invalid format"), ("CD-34", null), ("CD-345", "Invalid format"), ("AB-12", null) })
        {
            Assert.Equal(message, ValidationRules.Check(rules, value, pass));
            Assert.Equal(message, ValidationRules.Check(rules, value));
        }
    }

    [Fact]
    public void A_pattern_that_is_not_a_string_fails_the_rule() =>
        Assert.Equal("Invalid format", ValidationRules.Check(Rules(("pattern", 5.0)), "a"));

    [Fact]
    public void A_pattern_reads_only_strings() =>
        Assert.Null(ValidationRules.Check(Rules(("pattern", "^x$")), 5.0));
}
