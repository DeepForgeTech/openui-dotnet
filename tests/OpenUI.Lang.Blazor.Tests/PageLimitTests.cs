using Bunit;

namespace OpenUI.Lang.Blazor.Tests;

/// <summary>What a page allows of a program when nobody configured its session.</summary>
public sealed class PageLimitTests
{
    private static string Repeated(int chars, int copies) =>
        $"a = \"{new string('x', chars)}\"\nroot = Stack([{string.Join(", ", Enumerable.Repeat("Text(a)", copies))}])\n";

    [Fact]
    public async Task A_program_that_repeats_a_long_string_past_what_a_page_holds_draws_none_of_it()
    {
        await using var context = TestLibrary.NewContext();

        var cut = context.Render<OpenUIProgram>(parameters => parameters.Add(component => component.Program, Repeated(32 * 1024, 16)));

        Assert.Contains(cut.Instance.Session.Current.Errors, error => error.Message.Contains($"{OpenUIProgram.DefaultMaxTextChars} characters"));
        Assert.DoesNotContain(new string('x', 100), cut.Markup);
    }

    [Fact]
    public async Task One_string_of_the_size_a_program_really_uses_draws_many_times()
    {
        await using var context = TestLibrary.NewContext();

        var cut = context.Render<OpenUIProgram>(parameters => parameters.Add(component => component.Program, Repeated(32 * 1024, 7)));

        Assert.Empty(cut.Instance.Session.Current.Errors);
        Assert.Contains(new string('x', 32 * 1024), cut.Markup);
    }
}
