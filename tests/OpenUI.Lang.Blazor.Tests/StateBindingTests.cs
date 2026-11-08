using Bunit;
using OpenUI.Lang.Blazor.Tests.TestComponents;
using OpenUI.Lang.Runtime;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor.Tests;

/// <summary>A program's <c>$variables</c> bound to a store outside it, through <see cref="IOpenUIStateBinding"/>.</summary>
public sealed class StateBindingTests
{
    private const string Weather = """
        $city = "Rome"
        w = Query("weather", {city: $city}, {text: "none"})
        root = Stack([Text("City " + $city), Text(w.text)])

        """;

    private const string Greeting = """
        $name = "Ann"
        root = Stack([Input("Name", $name), Text("Hello " + $name)])

        """;

    [Fact]
    public async Task The_store_s_value_is_on_the_first_render_and_in_the_first_query()
    {
        await using var context = TestLibrary.NewContext();
        var store = new FakeStateStore("$city");
        store.Set("$city", "Paris");
        var cities = new List<string>();

        var cut = RenderBound(context, Weather, store, WeatherTool(cities));
        var firstRender = cut.FindAll("p.text")[0].TextContent;
        await cut.Instance.Session.WhenIdleAsync();

        Assert.Equal("City Paris", firstRender);
        Assert.Equal(["Paris"], Locked(cities));
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task An_outside_change_runs_a_query_that_reads_the_variable_again()
    {
        await using var context = TestLibrary.NewContext();
        var store = new FakeStateStore("$city");
        store.Set("$city", "Paris");
        var cities = new List<string>();
        var cut = RenderBound(context, Weather, store, WeatherTool(cities));
        cut.WaitForAssertion(() => Assert.Equal("Weather in Paris", cut.FindAll("p.text")[1].TextContent));

        store.Set("$city", "Oslo");

        cut.WaitForAssertion(() => Assert.Equal("Weather in Oslo", cut.FindAll("p.text")[1].TextContent));
        Assert.Equal(["Paris", "Oslo"], Locked(cities));
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task Changes_the_store_makes_on_another_thread_are_never_written_back()
    {
        await using var context = TestLibrary.NewContext();
        var store = new FakeStateStore("$city");
        store.Set("$city", "City 0");
        var cut = RenderBound(context, "$city = \"\"\nroot = Stack([Text($city)])\n", store);

        // Stale write-backs need a write-back to read the session mid-change, so this catches a regression often, not every run.
        await Task.Run(() =>
        {
            for (var i = 1; i <= 500; i++)
            {
                store.Set("$city", $"City {i}");
            }
        });

        cut.WaitForAssertion(() => Assert.Equal("City 500", cut.Find("p.text").TextContent));
        await cut.InvokeAsync(() => Task.Yield());
        await cut.InvokeAsync(() => { });
        Assert.Empty(store.Writes);
        Assert.Equal("City 500", store.Read("$city").ToString());
    }

    [Fact]
    public async Task A_program_write_reaches_the_store_once_and_its_echo_goes_no_further()
    {
        await using var context = TestLibrary.NewContext();
        var store = new FakeStateStore("$name");
        store.Set("$name", "Ann");
        var cut = RenderBound(context, Greeting, store);

        cut.Find("input").Input("Dale");
        await store.WhenAsync(s => s.Read("$name").ToString() == "Dale");
        await SettleAfterOutsideChange(cut, store, "$name", "Eve", "Hello Eve");

        Assert.Equal([("$name", (OuiValue)"Dale")], store.Writes);
    }

    [Fact]
    public async Task A_store_that_does_not_echo_gets_each_change_once()
    {
        await using var context = TestLibrary.NewContext();
        var store = new FakeStateStore("$name") { Echoes = false };
        store.Set("$name", "Ann");
        var cut = RenderBound(context, """
            $name = "Ann"
            $note = ""
            root = Stack([Input("Name", $name), Input("Note", $note), Text("Hello " + $name + " " + $note)])

            """, store);

        cut.FindAll("input")[0].Input("Dale");
        await store.WhenAsync(s => s.Writes.Count > 0);
        cut.FindAll("input")[1].Input("hi");
        cut.WaitForAssertion(() => Assert.Equal("Hello Dale hi", cut.Find("p.text").TextContent));
        await cut.InvokeAsync(() => Task.Yield());
        await cut.InvokeAsync(() => { });

        Assert.Equal([("$name", (OuiValue)"Dale")], store.Writes);
    }

    [Fact]
    public async Task A_default_the_store_has_no_value_for_is_written_to_it_once()
    {
        await using var context = TestLibrary.NewContext();
        var store = new FakeStateStore("$name");

        var cut = RenderBound(context, Greeting, store);

        await store.WhenAsync(s => s.Read("$name").ToString() == "Ann");
        await SettleAfterOutsideChange(cut, store, "$name", "Bo", "Hello Bo");
        Assert.Equal([("$name", (OuiValue)"Ann")], store.Writes);
    }

    [Fact]
    public async Task A_sibling_that_fills_the_store_while_the_page_starts_is_not_lost()
    {
        await using var context = TestLibrary.NewContext();
        var store = new FakeStateStore("$name");

        var cut = context.Render<BoundPage>(p => p
            .Add(x => x.Program, Greeting)
            .Add(x => x.Store, store)
            .Add(x => x.FillKey, "$name")
            .Add(x => x.FillValue, "Kim"));

        cut.WaitForAssertion(() => Assert.Equal("Hello Kim", cut.Find("p.text").TextContent));
        await SettleAfterOutsideChange(cut, store, "$name", "Lou", "Hello Lou");
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task A_list_crosses_as_a_list_both_ways()
    {
        await using var context = TestLibrary.NewContext();
        var store = new FakeStateStore("$tags");
        store.Set("$tags", OuiArray.Create("a", "b"));
        var cut = RenderBound(context, """
            $tags = []
            root = Stack([Text("Tags " + @Count($tags)), Button("More", @Set($tags, ["x", "y", "z"]))])

            """, store);
        Assert.Equal("Tags 2", cut.Find("p.text").TextContent);

        cut.Find("button").Click();

        await store.WhenAsync(s => s.Writes.Count > 0);
        var (key, written) = Assert.Single(store.Writes);
        Assert.Equal("$tags", key);
        Assert.True(written.TryGetArray(out _));
        Assert.Equal((OuiValue)OuiArray.Create("x", "y", "z"), written);
    }

    [Fact]
    public async Task A_host_that_owns_its_session_binds_it_through_the_view()
    {
        await using var context = TestLibrary.NewContext();
        await using var session = OpenUISession.Create(TestLibrary.Definition.Library);
        session.Load(Greeting);
        var store = new FakeStateStore("$name");
        store.Set("$name", "Bo");

        var cut = context.Render<OpenUIView>(p => p
            .Add(x => x.Session, session)
            .Add(x => x.Components, TestLibrary.Definition.Components)
            .Add(x => x.StateBinding, store));

        Assert.Equal("Hello Bo", cut.Find("p.text").TextContent);
        cut.Find("input").Input("Cy");
        await store.WhenAsync(s => s.Read("$name").ToString() == "Cy");
    }

    [Fact]
    public async Task The_store_s_value_wins_when_it_equals_a_default_still_streaming()
    {
        await using var context = TestLibrary.NewContext();
        await using var session = OpenUISession.Create(TestLibrary.Definition.Library);
        session.BeginStream();
        session.Append("root = Stack([Text(\"N \" + $n)])\n$n = \"1");
        var store = new FakeStateStore("$n");
        store.Set("$n", "1");

        var cut = context.Render<OpenUIView>(p => p
            .Add(x => x.Session, session)
            .Add(x => x.Components, TestLibrary.Definition.Components)
            .Add(x => x.StateBinding, store));
        await cut.InvokeAsync(() =>
        {
            session.Append("0\"\n");
            session.EndStream();
        });

        // The default grew to "10" after the store's "1" was taken, and the store's value is the one that stays.
        cut.WaitForAssertion(() => Assert.Equal("N 1", cut.Find("p.text").TextContent));
        Assert.Equal("1", store.Read("$n").ToString());
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task Removing_the_program_stops_following_the_store()
    {
        await using var context = TestLibrary.NewContext();
        var store = new FakeStateStore("$name");
        store.Set("$name", "Ann");
        var cut = context.Render<RemovableHost>(p => p.Add(x => x.Program, Greeting).Add(x => x.StateBinding, store));
        Assert.Equal(1, store.Listeners);

        cut.Render(p => p.Add(x => x.Show, false));

        await store.WhenAsync(s => s.Listeners == 0);
        store.Set("$name", "Late");
        Assert.Empty(store.Writes);
    }

    private static IRenderedComponent<OpenUIProgram> RenderBound(BunitContext context, string program, FakeStateStore store, IOpenUIToolProvider? tools = null) =>
        context.Render<OpenUIProgram>(p => p
            .Add(x => x.Program, program)
            .Add(x => x.Tools, tools)
            .Add(x => x.StateBinding, store));

    private static OpenUITools WeatherTool(List<string> cities) => new OpenUITools().Add("weather", OpenUIToolEffect.Read, (args, _) =>
    {
        var city = args.TryGetObject(out var input) ? input["city"].ToString() : "";
        lock (cities)
        {
            cities.Add(city);
        }

        return ValueTask.FromResult<OuiValue>(new OuiObject.Builder().Set("text", "Weather in " + city).ToObject());
    });

    private static List<string> Locked(List<string> cities)
    {
        lock (cities)
        {
            return [.. cities];
        }
    }

    // A write-back waits its turn on the renderer, so one that should not happen could land late. An outside change
    // queues a render behind it; once that render shows, any write the earlier change would have made has been made.
    private static async Task SettleAfterOutsideChange<T>(IRenderedComponent<T> cut, FakeStateStore store, string key, string value, string shown)
        where T : Microsoft.AspNetCore.Components.IComponent
    {
        store.Set(key, value);
        cut.WaitForAssertion(() => Assert.Equal(shown, cut.Find("p.text").TextContent));
        await cut.InvokeAsync(() => Task.Yield());
        await cut.InvokeAsync(() => { });
    }
}
