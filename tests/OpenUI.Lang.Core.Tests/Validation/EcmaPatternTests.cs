using OpenUI.Lang.Runtime;

namespace OpenUI.Lang.Core.Tests.Validation;

/// <summary>
/// Section 5.4: "<c>pattern</c> is a regular expression with no flags. It uses only features common to
/// ECMAScript and ICU: character classes, quantifiers, anchors, groups, and alternation." Each case
/// follows from that sentence and from the ECMAScript meaning of the syntax.
/// </summary>
public class EcmaPatternTests
{
    [Theory]
    // Character classes.
    [InlineData("^[abc]$", "b", true)]
    [InlineData("^[abc]$", "d", false)]
    [InlineData("^[^abc]$", "d", true)]
    [InlineData("^[a-c]$", "b", true)]
    [InlineData("^[a\\-c]$", "-", true)]
    [InlineData("^[\\]]$", "]", true)]
    [InlineData("^[[]$", "[", true)]
    [InlineData("^\\d$", "7", true)]
    [InlineData("^\\d$", "\u0663", false)]
    [InlineData("^\\D$", "\u0663", true)]
    [InlineData("^\\w$", "_", true)]
    [InlineData("^\\w$", "\u00E9", false)]
    [InlineData("^\\W$", "\u00E9", true)]
    [InlineData("^\\s$", "\u00A0", true)]
    [InlineData("^\\s$", "\uFEFF", true)]
    [InlineData("^\\s$", "\u0085", false)]
    [InlineData("^\\S$", "\u0085", true)]
    [InlineData("^\\S$", "\u00A0", false)]
    [InlineData("^[\\d]$", "\u0663", false)]
    [InlineData("^[\\w-]$", "-", true)]
    [InlineData("^[\\s@]$", "\u2003", true)]
    [InlineData("^[\\D]$", "5", false)]
    [InlineData("^[\\D]$", "a", true)]
    [InlineData("^[\\W]$", "a", false)]
    [InlineData("^[\\W]$", "^", true)]
    [InlineData("^[\\S]$", " ", false)]
    [InlineData("^[\\S]$", "a", true)]
    [InlineData("^.$", "a", true)]
    [InlineData("^.$", "\n", false)]
    [InlineData("^.$", "\u2028", false)]
    [InlineData("^.$", "\u0085", true)]
    // Quantifiers.
    [InlineData("^a*$", "", true)]
    [InlineData("^a+$", "", false)]
    [InlineData("^a?$", "aa", false)]
    [InlineData("^a{2}$", "aa", true)]
    [InlineData("^a{2,}$", "a", false)]
    [InlineData("^a{1,2}$", "aaa", false)]
    [InlineData("^a+?$", "aa", true)]
    // Anchors: with no flags $ matches only at the very end, unlike .NET's default.
    [InlineData("^a", "ab", true)]
    [InlineData("^a", "ba", false)]
    [InlineData("a$", "ba", true)]
    [InlineData("a$", "a\n", false)]
    [InlineData("^a$", "\na", false)]
    // Groups and alternation.
    [InlineData("^(ab)+$", "abab", true)]
    [InlineData("^(?:ab)+$", "abab", true)]
    [InlineData("^(?:ab)+$", "aba", false)]
    [InlineData("^(a|b)c$", "bc", true)]
    [InlineData("^a|b$", "ax", true)]
    [InlineData("^(a|b)$", "ax", false)]
    // Escapes that stand for themselves, or for one character.
    [InlineData("^\\.$", ".", true)]
    [InlineData("^\\.$", "a", false)]
    [InlineData("^\\/$", "/", true)]
    [InlineData("^\\t$", "\t", true)]
    [InlineData("^\\x41$", "A", true)]
    [InlineData("^\\u0041$", "A", true)]
    // No flags: matching is case sensitive, and a match can be anywhere.
    [InlineData("a", "A", false)]
    [InlineData("b", "abc", true)]
    [InlineData("", "abc", true)]
    public void The_subset_matches_as_the_spec_says(string pattern, string input, bool expected) =>
        Assert.Equal(expected, EcmaRegex.IsMatch(pattern, input));

    [Theory]
    // Outside the subset the sentence names.
    [InlineData("(a)\\1")]
    [InlineData("(?<n>a)\\k<n>")]
    [InlineData("(?<n>a)")]
    [InlineData("(?=a)")]
    [InlineData("(?!a)")]
    [InlineData("(?<=a)b")]
    [InlineData("(?<!a)b")]
    [InlineData("(?i)a")]
    [InlineData("\\bword\\b")]
    [InlineData("\\Bx")]
    [InlineData("\\p{L}")]
    [InlineData("\\cA")]
    [InlineData("[]")]
    [InlineData("[^]")]
    // Not a regular expression at all.
    [InlineData("[")]
    [InlineData("(")]
    [InlineData(")")]
    [InlineData("*")]
    [InlineData("a{2,1}")]
    [InlineData("\\")]
    [InlineData("\\x4")]
    [InlineData("\\u00G1")]
    public void Anything_else_gives_no_answer(string pattern) => Assert.Null(EcmaRegex.IsMatch(pattern, "a"));

    [Fact]
    public void A_pattern_past_the_length_limit_gives_no_answer()
    {
        Assert.NotNull(EcmaRegex.IsMatch(new string('a', EcmaRegex.MaxPatternLength), "a"));
        Assert.Null(EcmaRegex.IsMatch(new string('a', EcmaRegex.MaxPatternLength + 1), "a"));
    }

    [Fact]
    public void A_catastrophic_pattern_runs_in_linear_time()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(EcmaRegex.IsMatch("^(a+)+$", new string('a', 50_000) + "!"));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Took {clock.Elapsed}.");
    }
}
