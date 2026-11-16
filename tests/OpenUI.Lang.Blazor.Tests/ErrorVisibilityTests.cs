using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace OpenUI.Lang.Blazor.Tests;

/// <summary>When <see cref="OpenUIView"/> draws errors for a program that still renders.</summary>
public sealed class ErrorVisibilityTests
{
    // The loop name is not quoted, so the @Each is an error and draws nothing, while the rest of the page renders.
    private const string BrokenEach = """
        root = Stack([Text("Shown"), Stack(@Each(["a", "b"], n, Text("x")))])

        """;

    [Fact]
    public async Task By_default_a_non_fatal_error_is_not_drawn()
    {
        await using var context = TestLibrary.NewContext();

        var cut = context.Render<OpenUIProgram>(p => p.Add(x => x.Program, BrokenEach));

        Assert.NotEmpty(cut.Instance.Session.Current.Errors);
        Assert.False(cut.Instance.Session.Current.IsFatal);
        Assert.Equal("Shown", cut.Find("p.text").TextContent);
        Assert.Empty(cut.FindAll(".oui-errors"));
    }

    [Fact]
    public async Task A_view_that_shows_all_errors_draws_a_broken_Each_s_error_under_the_tree()
    {
        await using var context = TestLibrary.NewContext();

        var cut = context.Render<OpenUIProgram>(p => p.Add(x => x.Program, BrokenEach).Add(x => x.ShowAllErrors, true));

        Assert.Equal("Shown", cut.Find("p.text").TextContent);
        var drawn = cut.Find(".oui-errors").TextContent;
        Assert.All(cut.Instance.Session.Current.Errors, error => Assert.Contains(error.Message, drawn));
    }

    [Fact]
    public async Task The_app_wide_option_shows_them_and_a_view_can_turn_it_off()
    {
        await using var context = TestLibrary.NewContext(services => services
            .AddOpenUIComponents(TestLibrary.Definition)
            .AddSingleton(new OpenUIViewOptions { ShowAllErrors = true }));

        var shown = context.Render<OpenUIProgram>(p => p.Add(x => x.Program, BrokenEach));
        var hidden = context.Render<OpenUIProgram>(p => p.Add(x => x.Program, BrokenEach).Add(x => x.ShowAllErrors, false));

        Assert.NotEmpty(shown.FindAll(".oui-errors"));
        Assert.Empty(hidden.FindAll(".oui-errors"));
    }
}
