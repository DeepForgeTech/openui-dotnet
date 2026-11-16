using System.Text.Json;
using OpenUI.Lang.Core.Tests.Conformance;
using OpenUI.Lang.Diagnostics;
using OpenUI.Lang.Library;
using OpenUI.Lang.Prompting;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Core.Tests.Validation;

/// <summary>The opt-in checks on how a program's state is wired to its queries and controls.</summary>
public sealed class WiringCheckTests
{
    private static OpenUILibrary Library { get; } = ConformanceAdapter.CreateLibrary(FixtureCatalog.LoadLibrary());

    private static readonly ToolDescriptor[] Tools =
    [
        new("getForecast", InputSchema: Schema("""{"type":"object","properties":{"location":{"type":"string"},"days":{"type":"number"}},"required":["location"]}""")),
        new("listSprayJobs", InputSchema: Schema("""{"type":"object","properties":{"status":{"type":"string"}}}""")),
        new("listLocations"),
        "scheduleSprayJob",
    ];

    private const string Toggles = """
        north = Button("North", @Set($location, "north-field"), $location == "north-field" ? "primary" : "secondary")
        river = Button("River", @Set($location, "river-bend"), $location == "river-bend" ? "primary" : "secondary")
        days = Text("" + @Count(forecast.days))
        """;

    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static OpenUIErrorList Check(string program) => OpenUIWiringCheck.Check(Library, Tools, program);

    private static IEnumerable<string> Codes(string program) => Check(program).Select(error => error.Code.Value);

    [Fact]
    public void A_required_query_arg_whose_variable_starts_as_null_is_flagged()
    {
        var errors = Check($$"""
            root = Card("Forecast", [north, river, days])
            {{Toggles}}
            forecast = Query("getForecast", {location: $location, days: 7}, {days: []})
            """);

        var error = Assert.Single(errors);
        Assert.Equal(OpenUIErrorCode.QueryNullArg, error.Code);
        Assert.Equal("x-query-null-arg", error.Code.Value);
        Assert.Equal(OpenUIErrorSource.Query, error.Source);
        Assert.Equal("forecast", error.StatementId);
        Assert.Equal("getForecast", error.ToolName);
        Assert.Equal("/args/location", error.Path);
        Assert.Contains("$location starts as null", error.Message);
    }

    [Fact]
    public void A_declared_null_is_flagged_too_and_a_real_default_is_not()
    {
        const string Body = """
            root = Card("Forecast", [north, river, days])
            forecast = Query("getForecast", {location: $location, days: 7}, {days: []})
            """;

        Assert.Equal(["x-query-null-arg"], Codes($"$location = null\n{Body}\n{Toggles}"));
        Assert.Empty(Codes($"$location = \"north-field\"\n{Body}\n{Toggles}"));
    }

    [Fact]
    public void A_required_query_arg_whose_variable_starts_empty_is_flagged()
    {
        var error = Assert.Single(Check($$"""
            $location = ""
            root = Card("Forecast", [north, river, days])
            {{Toggles}}
            forecast = Query("getForecast", {location: $location, days: 7}, {days: []})
            """));

        Assert.Equal(OpenUIErrorCode.QueryNullArg, error.Code);
        Assert.Contains("$location starts as empty", error.Message);
    }

    [Fact]
    public void An_optional_arg_may_start_as_null()
    {
        Assert.Empty(Codes("""
            root = Card("Jobs", [all, scheduled, count])
            all = Button("All", @Set($status, null))
            scheduled = Button("Scheduled", @Set($status, "scheduled"))
            jobs = Query("listSprayJobs", {status: $status}, {jobs: []})
            count = Text("" + @Count(jobs.jobs))
            """));
    }

    [Fact]
    public void A_variable_reaching_the_args_only_through_a_statement_is_flagged_with_the_statement()
    {
        var error = Assert.Single(Check($$"""
            $location = "north-field"
            root = Card("Forecast", [north, river, days])
            {{Toggles}}
            activeLoc = picked
            picked = $location
            forecast = Query("getForecast", {location: activeLoc, days: 7}, {days: []})
            """));

        Assert.Equal(OpenUIErrorCode.QueryIndirectState, error.Code);
        Assert.Equal("/args", error.Path);
        Assert.Contains("read $location only through activeLoc", error.Message);
    }

    [Fact]
    public void Args_given_as_a_statement_reference_are_checked_for_indirect_state()
    {
        var error = Assert.Single(Check($$"""
            $location = "north-field"
            root = Card("Forecast", [north, river, days])
            {{Toggles}}
            params = {location: $location, days: 7}
            forecast = Query("getForecast", params, {days: []})
            """));

        Assert.Equal(OpenUIErrorCode.QueryIndirectState, error.Code);
        Assert.Contains("read $location only through params", error.Message);
    }

    [Fact]
    public void A_variable_written_in_the_args_may_also_reach_them_through_a_statement()
    {
        Assert.Empty(Codes($$"""
            $location = "north-field"
            root = Card("Forecast", [north, river, days])
            {{Toggles}}
            label = $location + " forecast"
            forecast = Query("getForecast", {location: $location, days: 7, note: label}, {days: []})
            """));
    }

    [Fact]
    public void A_query_arg_read_from_another_query_s_result_is_flagged()
    {
        var error = Assert.Single(Check("""
            root = Card("Forecast", [days])
            locs = Query("listLocations", {}, {locations: []})
            forecast = Query("getForecast", {location: locs.locations[0].id, days: 7}, {days: []})
            days = Text("" + @Count(forecast.days))
            """));

        Assert.Equal(OpenUIErrorCode.QueryReadsQuery, error.Code);
        Assert.Equal("forecast", error.StatementId);
        Assert.Equal("/args/location", error.Path);
        Assert.StartsWith("forecast's location reads locs, another Query's result", error.Message);
    }

    [Fact]
    public void A_query_result_reached_through_a_value_statement_names_the_statement()
    {
        var error = Assert.Single(Check("""
            root = Card("Forecast", [days])
            locs = Query("listLocations", {}, {locations: []})
            eastId = @First(@Filter(locs.locations, "name", "contains", "East")).id
            forecast = Query("getForecast", {location: eastId, days: 7}, {days: []})
            days = Text("" + @Count(forecast.days))
            """));

        Assert.Equal(OpenUIErrorCode.QueryReadsQuery, error.Code);
        Assert.Contains("reads locs through eastId", error.Message);
    }

    [Fact]
    public void A_mutation_s_args_may_read_a_query_s_result()
    {
        Assert.Empty(Codes("""
            $location = "north-field"
            root = Card("Forecast", [Button("Book", @Run(book)), days])
            forecast = Query("getForecast", {location: $location, days: 7}, {days: []})
            book = Mutation("scheduleSprayJob", {location: $location, date: forecast.days[0].date})
            days = Text("" + @Count(forecast.days))
            """));
    }

    [Fact]
    public void A_variable_a_control_writes_and_nothing_reads_is_flagged()
    {
        var error = Assert.Single(Check("""
            $hour = "0"
            $speed = "1"
            root = Card("Player", [hourPick, speedPick, readout])
            hourPick = Select("hour", [SelectItem("0", "0h"), SelectItem("6", "6h")], $hour)
            speedPick = Select("speed", [SelectItem("1", "1s"), SelectItem("2", "2s")], $speed)
            readout = Text("Hour " + $hour)
            """));

        Assert.Equal(OpenUIErrorCode.StateUnread, error.Code);
        Assert.Equal(OpenUIErrorSource.Runtime, error.Source);
        Assert.Equal("speedPick", error.StatementId);
        Assert.StartsWith("$speed is set in speedPick but nothing reads it", error.Message);
    }

    [Fact]
    public void A_binding_inside_a_form_is_the_form_s_field_and_is_not_flagged()
    {
        Assert.Empty(Codes("""
            root = Card("Job", [form])
            form = Form("job", [field, send])
            field = Input("product", "Product", $product)
            send = Button("Send", @ToAssistant("Plan it"))
            """));
    }

    [Fact]
    public void A_variable_a_button_sets_and_a_bound_control_shows_is_used()
    {
        Assert.Empty(Codes("""
            $pick = "a"
            root = Card("Pick", [Button("Pick B", @Set($pick, "b")), choice])
            choice = Select("pick", [SelectItem("a", "A"), SelectItem("b", "B")], $pick)
            """));
    }

    // Outside a form, an event carries the page's $variables to the host and the model (section 6.3).
    [Fact]
    public void A_variable_an_input_writes_is_read_when_a_button_sends_the_page_to_the_assistant()
    {
        Assert.Empty(Codes("""
            root = Card("Feedback", [field, send])
            field = Input("fb", "Your feedback", $feedback)
            send = Button("Send", @ToAssistant("Here is my feedback"))
            """));
    }

    [Fact]
    public void A_set_before_ToAssistant_in_the_same_action_is_sent_with_it()
    {
        Assert.Empty(Codes("""
            root = Card("Pick", [a])
            a = Button("A", Action([@Set($choice, "a"), @ToAssistant("Picked")]))
            """));
    }

    [Fact]
    public void A_button_with_no_action_sends_the_page_to_the_assistant()
    {
        Assert.Empty(Codes("""
            root = Card("Feedback", [field, Button("Send")])
            field = Input("fb", "Your feedback", $feedback)
            """));
    }

    [Fact]
    public void An_event_inside_a_form_sends_only_the_form_so_a_page_variable_is_still_unread()
    {
        var error = Assert.Single(Check("""
            root = Card("Job", [speedPick, form])
            speedPick = Select("speed", [SelectItem("1", "1s"), SelectItem("2", "2s")], $speed)
            form = Form("job", [Input("product", "Product", $product), Button("Send", @ToAssistant("Plan it"))])
            """));

        Assert.Equal(OpenUIErrorCode.StateUnread, error.Code);
        Assert.Equal("speedPick", error.StatementId);
    }

    // A chain as long as the statement limit allows must not exhaust the stack (section 10: never crash the host).
    // It runs on a thread with a 1 MB stack so the result doesn't depend on the test runner's.
    [Fact]
    public void A_chain_of_almost_the_statement_limit_is_checked_without_throwing()
    {
        var program = new System.Text.StringBuilder("""
            root = Card("x", [Text(q.n), Input("a", "b", $x)])
            q = Query("getForecast", {location: s0}, {n: 1})

            """);
        const int Length = 9990;
        for (var i = 0; i < Length - 1; i++)
        {
            program.Append($"s{i} = s{i + 1}\n");
        }

        program.Append($"s{Length - 1} = $x\n");
        OpenUIErrorList? errors = null;
        Exception? thrown = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    errors = Check(program.ToString());
                }
                catch (Exception exception)
                {
                    thrown = exception;
                }
            },
            1024 * 1024);
        thread.Start();
        thread.Join();

        Assert.Null(thrown);
        var error = Assert.Single(errors!);
        Assert.Equal(OpenUIErrorCode.QueryIndirectState, error.Code);
        Assert.Contains("read $x only through s0", error.Message);
    }

    [Fact]
    public void Many_queries_over_one_long_chain_are_checked_quickly_and_the_findings_are_capped()
    {
        const int Length = 4900;
        var program = new System.Text.StringBuilder("root = Card(\"x\", [Text(s0), Input(\"a\", \"b\", $x)])\n");
        for (var i = 0; i < Length - 1; i++)
        {
            program.Append($"s{i} = s{i + 1}\n");
        }

        program.Append($"s{Length - 1} = $x\n");
        for (var i = 0; i < Length; i++)
        {
            program.Append($"q{i} = Query(\"getForecast\", {{location: s0, b: s1, c: s2}}, {{n: 1}})\n");
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var errors = Check(program.ToString());
        watch.Stop();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"The check took {watch.Elapsed}.");
        Assert.Equal(OpenUILimits.Default.MaxErrors + 1, errors.Count);
        Assert.All(errors.Take(OpenUILimits.Default.MaxErrors), error => Assert.Equal(OpenUIErrorCode.QueryIndirectState, error.Code));
        Assert.Equal(OpenUIErrorCode.LimitExceeded, errors[^1].Code);
    }

    [Fact]
    public void A_program_with_no_statements_has_no_findings()
    {
        Assert.Same(OpenUIErrorList.Empty, Check(""));
    }

    // The check only reports a chained query; it must not change what the runtime does with one (section 6.1).
    [Fact]
    public async Task A_query_reading_another_query_s_result_still_runs_once()
    {
        var forecastCalls = new List<OuiValue>();
        var tools = new OpenUITools()
            .Add("listLocations", OpenUIToolEffect.Read, (_, _) => ValueTask.FromResult(OuiValue.FromJson(Schema("""{"locations":[{"id":"east"}]}"""))))
            .Add("getForecast", OpenUIToolEffect.Read, (args, _) =>
            {
                lock (forecastCalls)
                {
                    forecastCalls.Add(args);
                }

                return ValueTask.FromResult(OuiValue.FromJson(Schema("""{"days":[]}""")));
            });
        const string Program = """
            root = Card("Forecast", [Text("" + @Count(locs.locations))])
            locs = Query("listLocations", {}, {locations: []})
            forecast = Query("getForecast", {location: @First(locs.locations).id}, {days: []})
            """;

        await using var session = OpenUISession.Create(Library, new OpenUISessionOptions { Tools = tools });
        session.Load(Program);
        await session.WhenIdleAsync();

        Assert.Equal(OpenUIErrorCode.QueryReadsQuery, Assert.Single(Check(Program)).Code);
        Assert.Equal("1", TextOf(session)?["text"].Value.ToString());
        var call = Assert.Single(forecastCalls);
        Assert.True(call.TryGetObject(out var args));
        Assert.True(args.TryGetValue("location", out var location));
        Assert.True(location.IsNull);
    }

    private static OuiComponent? TextOf(OpenUISession session) =>
        session.Current.Root!["children"].Value.TryGetArray(out var children) && children[0].TryGetComponent(out var text) ? text : null;
}
