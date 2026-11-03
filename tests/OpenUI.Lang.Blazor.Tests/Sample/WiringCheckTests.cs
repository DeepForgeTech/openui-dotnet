using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Samples.BlazorApp;
using OpenUI.Lang.Samples.BlazorApp.Checks;
using OpenUI.Lang.Samples.BlazorApp.Playback;
using OpenUI.Lang.Samples.BlazorApp.Spray;
using OpenUI.Lang.Samples.BlazorApp.Weather;

namespace OpenUI.Lang.Blazor.Tests.Sample;

/// <summary>The sample's checks on how a program's state is wired to its queries and controls.</summary>
public sealed class WiringCheckTests
{
    private static readonly OpenUIComponents Components = new([new OpenUI.Lang.Blazor.Mud.MudComponents(), new SprayComponents(), new PlaybackComponents()]);

    private static OpenUIErrorList Check(string program) => WiringCheck.Check(Components.Library, WeatherTools.Descriptors, program);

    private static IEnumerable<string> Codes(string program) => Check(program).Select(error => error.Code.Value);

    private const string Toggles = """
        north = Button("North", @Set($location, "north-field"), $location == "north-field" ? "primary" : "secondary")
        river = Button("River", @Set($location, "river-bend"), $location == "river-bend" ? "primary" : "secondary")
        days = TextContent("" + @Count(forecast.days))
        """;

    [Fact]
    public void A_required_query_arg_whose_variable_starts_as_null_is_flagged()
    {
        var errors = Check($"""
            root = Card([Buttons([north, river]), days])
            {Toggles}
            forecast = Query("getForecast", {"{"}location: $location, days: 7{"}"}, {"{"}days: []{"}"})
            """);

        var error = Assert.Single(errors);
        Assert.Equal(WiringCheck.NullQueryArg, error.Code);
        Assert.Equal("forecast", error.StatementId);
        Assert.Equal("getForecast", error.ToolName);
        Assert.Contains("$location starts as null", error.Message);
    }

    [Fact]
    public void A_declared_null_is_flagged_too_and_a_real_default_is_not()
    {
        const string Body = """
            root = Card([Buttons([north, river]), days])
            forecast = Query("getForecast", {location: $location, days: 7}, {days: []})
            """;

        Assert.Equal(["x-query-null-arg"], Codes($"$location = null\n{Body}\n{Toggles}"));
        Assert.Empty(Codes($"$location = \"north-field\"\n{Body}\n{Toggles}"));
    }

    [Fact]
    public void An_optional_arg_may_start_as_null()
    {
        Assert.Empty(Codes("""
            root = Card([Buttons([all, scheduled]), count])
            all = Button("All", @Set($status, null))
            scheduled = Button("Scheduled", @Set($status, "scheduled"))
            jobs = Query("listSprayJobs", {status: $status}, {jobs: []})
            count = TextContent("" + @Count(jobs.jobs))
            """));
    }

    [Fact]
    public void A_variable_reaching_the_args_only_through_a_statement_is_flagged_with_the_statement()
    {
        var errors = Check($"""
            $location = "north-field"
            root = Card([Buttons([north, river]), days])
            {Toggles}
            activeLoc = picked
            picked = $location
            forecast = Query("getForecast", {"{"}location: activeLoc, days: 7{"}"}, {"{"}days: []{"}"})
            """);

        var error = Assert.Single(errors);
        Assert.Equal(WiringCheck.IndirectQueryState, error.Code);
        Assert.Contains("read $location only through activeLoc", error.Message);
    }

    [Fact]
    public void A_variable_written_in_the_args_may_also_reach_them_through_a_statement()
    {
        Assert.Empty(Codes($"""
            $location = "north-field"
            root = Card([Buttons([north, river]), days])
            {Toggles}
            label = $location + " forecast"
            forecast = Query("getForecast", {"{"}location: $location, days: 7, note: label{"}"}, {"{"}days: []{"}"})
            """));
    }

    [Fact]
    public void A_required_query_arg_whose_variable_starts_empty_is_flagged()
    {
        var error = Assert.Single(Check($"""
            $location = ""
            root = Card([Buttons([north, river]), days])
            {Toggles}
            forecast = Query("getForecast", {"{"}location: $location, days: 7{"}"}, {"{"}days: []{"}"})
            """));

        Assert.Equal(WiringCheck.NullQueryArg, error.Code);
        Assert.Contains("$location starts as empty", error.Message);
    }

    [Fact]
    public void A_query_arg_read_from_another_query_s_result_is_flagged()
    {
        var error = Assert.Single(Check("""
            root = Card([days])
            locs = Query("listLocations", {}, {locations: []})
            forecast = Query("getForecast", {location: locs.locations[0].id, days: 7}, {days: []})
            days = TextContent("" + @Count(forecast.days))
            """));

        Assert.Equal(WiringCheck.QueryReadsQuery, error.Code);
        Assert.Equal("forecast", error.StatementId);
        Assert.Equal("/args/location", error.Path);
        Assert.StartsWith("forecast's location reads locs, another Query's result", error.Message);
    }

    [Fact]
    public void A_query_result_reached_through_a_value_statement_names_the_statement()
    {
        var error = Assert.Single(Check("""
            root = Card([days])
            locs = Query("listLocations", {}, {locations: []})
            eastId = @First(@Filter(locs.locations, "name", "contains", "East")).id
            forecast = Query("getForecast", {location: eastId, days: 7}, {days: []})
            days = TextContent("" + @Count(forecast.days))
            """));

        Assert.Equal(WiringCheck.QueryReadsQuery, error.Code);
        Assert.Contains("reads locs through eastId", error.Message);
    }

    [Fact]
    public void A_mutation_s_args_may_read_a_query_s_result()
    {
        Assert.Empty(Codes("""
            $location = "north-field"
            root = Card([Buttons([Button("Book", @Run(book))]), days])
            forecast = Query("getForecast", {location: $location, days: 7}, {days: []})
            book = Mutation("scheduleSprayJob", {location: $location, product: "Fungicide", date: forecast.days[0].date})
            days = TextContent("" + @Count(forecast.days))
            """));
    }

    [Fact]
    public void A_variable_a_control_writes_and_nothing_reads_is_flagged()
    {
        var errors = Check("""
            $hour = 0
            $speed = "1"
            root = Card([player, speedPick, readout])
            player = Player($hour, [0, 6, 12])
            speedPick = Select("speed", [SelectItem("1", "1s"), SelectItem("2", "2s")], null, null, $speed)
            readout = TextContent("Hour " + $hour)
            """);

        var error = Assert.Single(errors);
        Assert.Equal(WiringCheck.UnreadState, error.Code);
        Assert.Equal("speedPick", error.StatementId);
        Assert.StartsWith("$speed is set in speedPick but nothing reads it", error.Message);
    }

    [Fact]
    public void A_binding_inside_a_form_is_the_form_s_field_and_is_not_flagged()
    {
        Assert.Empty(Codes("""
            root = Card([form])
            form = Form("job", buttons, [field])
            field = FormControl("Product", Input("product", "Product", "text", null, $product))
            buttons = Buttons([Button("Send", @ToAssistant("Plan it"))])
            """));
    }

    [Fact]
    public void A_variable_a_button_sets_and_a_bound_control_shows_is_used()
    {
        Assert.Empty(Codes("""
            $open = false
            root = Card([Buttons([Button("Open", @Set($open, true))]), dialog])
            dialog = Modal("Confirm", $open, [TextContent("Sure?")])
            """));
    }

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
        Assert.Equal(WiringCheck.UnreadState, error.Code);
        Assert.StartsWith("$day is set in forecast", error.Message);
    }
}
