using OpenUI.Lang.Blazor;
using OpenUI.Lang.Library;

namespace OpenUI.Lang.Samples.BlazorApp.Playback;

/// <summary>
/// Components that change state on a timer, which the language itself can't do: a Query's refresh
/// interval only fetches again. Registered on top of the Mud package, like the spray components.
/// </summary>
public sealed class PlaybackComponents() : LibraryDefinition("playback", null,
[
    new("Playback",
    [
        new("Player",
            "A play and pause button that steps a $variable through a list of values on a timer, to animate through hours, days or frames. " +
            "While playing it writes the next value every `seconds` and starts again after the last when `loop` is true. " +
            "Bind `value` to the same $variable a Slider or Buttons use, and they follow it.",
            typeof(PlayerView),
        [
            new("value", TypeShape.Any, "The $variable it steps") { Access = PropAccess.ReadWrite, Required = true },
            new("values", new ArrayShape(TypeShape.Any), "The values in order") { Required = true },
            // Any, so a speed picked in a Select, which writes text, still counts; the player reads it as a number.
            new("seconds", TypeShape.Any, "Seconds per step, which may be a $variable a Select or Slider sets; 1 if left out"),
            new("loop", TypeShape.Boolean, "Start again after the last value; true if left out"),
            new("label", TypeShape.String, "Text beside the button"),
        ])
        {
            Slots = ["Card.children"],
        },
    ], []),
]);
