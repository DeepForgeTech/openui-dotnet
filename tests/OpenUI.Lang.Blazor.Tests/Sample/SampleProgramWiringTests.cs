using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Samples.BlazorApp;
using OpenUI.Lang.Samples.BlazorApp.Playback;
using OpenUI.Lang.Samples.BlazorApp.Spray;
using OpenUI.Lang.Samples.BlazorApp.Weather;

namespace OpenUI.Lang.Blazor.Tests.Sample;

/// <summary>The sample's own programs against Core's wiring check, which the chat and the builder run.</summary>
public sealed class SampleProgramWiringTests
{
    private static readonly OpenUIComponents Components = new([new OpenUI.Lang.Blazor.Mud.MudComponents(), new SprayComponents(), new PlaybackComponents()]);

    private static OpenUIErrorList Check(string program) => OpenUIWiringCheck.Check(Components.Library, WeatherTools.Descriptors, program);

    [Fact]
    public void Every_canned_program_in_the_sample_passes()
    {
        var programs = ProgramFiles.ReadFolder("gallery")
            .Where(program => program.Name != "spray.oui")
            .Select(program => (program.Name, program.Text))
            .Concat(new[] { "forecast.oui", "plan-job.oui", "spray-planner.oui", "spray-window.oui" }.Select(name => (name, ProgramFiles.Read(name))));

        Assert.All(programs, program => Assert.True(Check(program.Item2).Count == 0,
            $"{program.Item1}: {string.Join("; ", Check(program.Item2).Select(error => $"{error.Code.Value} {error.Message}"))}"));
    }

    [Fact]
    public void The_gallery_s_spray_showcase_is_flagged_because_picking_a_day_there_changes_nothing_else()
    {
        var spray = ProgramFiles.ReadFolder("gallery").Single(program => program.Name == "spray.oui");

        var error = Assert.Single(Check(spray.Text));
        Assert.Equal(OpenUIErrorCode.StateUnread, error.Code);
        Assert.StartsWith("$day is set in forecast", error.Message);
    }
}
