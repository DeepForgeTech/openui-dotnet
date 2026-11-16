using Bunit;
using OpenUI.Lang.Blazor.Tests.TestComponents;
using OpenUI.Lang.Library;

namespace OpenUI.Lang.Blazor.Tests;

public sealed class RunOnceTests
{
    // A press that runs when it should not waits on the gate for good, so each wait has a limit and the test fails by name.
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_press_while_one_is_still_running_does_nothing_and_after_it_finishes_one_runs_again()
    {
        var source = new LibraryDefinition("once", "Once", [new("All", [new("Once", "Runs once at a time.", typeof(RunOnceView), [])], [])]);
        await using var context = TestLibrary.NewContext(source);
        var cut = context.Render<OpenUIProgram>(parameters => parameters.Add(component => component.Program, "root = Once()\n"));
        var view = cut.FindComponent<RunOnceView>().Instance;

        var running = cut.InvokeAsync(view.Press);
        await cut.InvokeAsync(view.Press).WaitAsync(Budget);

        Assert.Equal(1, view.Hits);
        Assert.True(view.Running);

        await cut.InvokeAsync(view.Open);
        await running.WaitAsync(Budget);

        Assert.False(view.Running);
        var again = cut.InvokeAsync(view.Press);
        Assert.Equal(2, view.Hits);
        await cut.InvokeAsync(view.Open);
        await again.WaitAsync(Budget);
    }
}
