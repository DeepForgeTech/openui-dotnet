using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace OpenUI.Lang.Blazor.Tests.Mud;

/// <summary>
/// The package's writable views hold what the user wrote until the program answers. Two writes to a bound variable on one
/// turn of the renderer both land: the second builds on the first, not on the props drawn before the program answered.
/// </summary>
public sealed class MudWritableHoldTests
{
    private static BunitContext NewContext()
    {
        var context = TestLibrary.NewContext(services => services.AddMudServices().AddOpenUIComponents(TestLibrary.Mud));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Render<MudPopoverProvider>();
        return context;
    }

    [Fact]
    public async Task Two_ticks_before_the_program_answers_keep_both_in_a_bound_check_box_group()
    {
        await using var context = NewContext();
        var cut = context.Render<OpenUIProgram>(p => p.Add(x => x.Program,
            "$checks = {}\nroot = CheckBoxGroup(\"checks\", [CheckBoxItem(\"Wind\", \"\", \"wind\"), CheckBoxItem(\"Rain\", \"\", \"rain\")], null, $checks)\n"));
        var boxes = cut.FindComponents<MudCheckBox<bool>>();

        await cut.InvokeAsync(async () =>
        {
            await boxes[0].Instance.ValueChanged.InvokeAsync(true);
            await boxes[1].Instance.ValueChanged.InvokeAsync(true);
        });

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Instance.Session.State.Get("$checks").TryGetObject(out var flags));
            Assert.True(flags["wind"].TryGetBoolean(out var wind) && wind);
            Assert.True(flags["rain"].TryGetBoolean(out var rain) && rain);
        });
    }
}
