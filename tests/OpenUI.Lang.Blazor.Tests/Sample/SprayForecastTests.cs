using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using OpenUI.Lang.Samples.BlazorApp.Spray;

namespace OpenUI.Lang.Blazor.Tests.Sample;

/// <summary>The planner's forecast tiles, drawn from whatever a program gives them.</summary>
public sealed class SprayForecastTests
{
    private const string Day = "{date: \"2026-11-04\", highF: 60, lowF: 40, rainChance: 10, windMph: 5, sprayable: true}";

    [Fact]
    public async Task A_date_that_a_program_repeats_draws_a_tile_each_and_a_press_does_not_end_the_circuit()
    {
        await using var context = TestLibrary.NewContext(services => services
            .AddMudServices()
            .AddOpenUIComponents<OpenUI.Lang.Blazor.Mud.MudComponents>()
            .AddOpenUIComponents<SprayComponents>());
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Render<MudPopoverProvider>();
        var cut = context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, $"day = {Day}\n$picked = \"\"\nroot = SprayForecast([day, day], $picked)\n"));
        Assert.Equal(2, cut.FindAll(".spray-day").Count);

        await cut.InvokeAsync(() => cut.FindAll(".spray-day")[0].Click());

        cut.WaitForAssertion(() => Assert.All(cut.FindAll(".spray-day"), tile => Assert.Equal("true", tile.GetAttribute("aria-pressed"))));
    }
}
