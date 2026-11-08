using OpenUI.Lang.Blazor;
using OpenUI.Lang.Library;

namespace OpenUI.Lang.Samples.BlazorApp.Spray;

/// <summary>
/// The sample's own components, which only make sense for spraying. It is a second source on top of the Mud package: registering it
/// is all it takes for a program to use them alongside Mud's, for the prompt to teach them, and for a Card to say it can hold them.
/// </summary>
public sealed class SprayComponents() : LibraryDefinition("spray", null,
[
    new("Spray planning",
    [
        new("SprayOutlook", "The week at a glance: the next day fit to spray, how many days are, and how many jobs are on the books.", typeof(SprayOutlookView),
        [
            new("days", new ArrayShape(Day), "The forecast's days") { Required = true },
            new("jobCount", TypeShape.Number, "Jobs scheduled"),
        ])
        {
            Slots = ["Card.children"],
        },
        new("SprayForecast", "One tile per forecast day with its temperatures, wind and rain, and whether it can be sprayed. Pressing a tile picks that day.", typeof(SprayForecastView),
        [
            new("days", new ArrayShape(Day), "The forecast's days") { Required = true },
            new("selected", TypeShape.String, "The picked day, as yyyy-MM-dd") { Access = PropAccess.ReadWrite },
        ])
        {
            Slots = ["Card.children"],
        },
    ], []),
])
{
    private static TypeShape Day => new ObjectShape(
    [
        new PropSpec("date", TypeShape.String, true),
        new PropSpec("highF", TypeShape.Number, true),
        new PropSpec("lowF", TypeShape.Number, true),
        new PropSpec("rainChance", TypeShape.Number, true),
        new PropSpec("windMph", TypeShape.Number, true),
        new PropSpec("sprayable", TypeShape.Boolean, true),
    ]);
}
