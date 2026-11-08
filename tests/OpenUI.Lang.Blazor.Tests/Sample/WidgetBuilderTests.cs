using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using OpenUI.Lang.Samples.BlazorApp.Components.Pages;
using OpenUI.Lang.Samples.BlazorApp.Weather;
using OpenUI.Lang.Samples.BlazorApp.Widgets;
using OpenUI.Lang.Samples.Llm;

namespace OpenUI.Lang.Blazor.Tests.Sample;

/// <summary>The widget builder and the saved widgets page, with a model the test feeds one chunk at a time.</summary>
public sealed class WidgetBuilderTests : IDisposable
{
    private const string Wind = "root = Card([title])\ntitle = TextContent(\"Wind this week\")\n";

    private readonly string _directory = Directory.CreateTempSubdirectory("openui-builder-").FullName;
    private readonly ScriptedModel _model = new();
    private readonly WidgetStore _store;

    public WidgetBuilderTests() => _store = new WidgetStore(_directory, NullLogger<WidgetStore>.Instance);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private BunitContext NewContext()
    {
        var context = TestLibrary.NewContext(services =>
        {
            services.AddMudServices();
            services.AddOpenUIComponents<OpenUI.Lang.Blazor.Mud.MudComponents>();
            services.AddSingleton(new LlmSettings(LlmSettings.OpenAiProvider, null));
            services.AddSingleton<IChatClient>(_model);
            services.AddSingleton(_store);
            services.AddScoped<WidgetDesigner>();
            services.AddSingleton(new WeatherTools(new HttpClient(new NoApi()) { BaseAddress = new Uri("http://localhost:1") }, new ToolCallLog()));
            services.AddLogging();
        });
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Render<MudPopoverProvider>();
        return context;
    }

    private static IElement Button(IRenderedComponent<WidgetBuilder> page, string text) =>
        page.FindAll("button").First(button => button.TextContent.Trim() == text);

    private static void Ask(IRenderedComponent<WidgetBuilder> page, string request)
    {
        page.Find(".builder-request textarea").Input(request);
        page.Find("form.builder-prompt").Submit();
    }

    private async Task BuildAsync(IRenderedComponent<WidgetBuilder> page, string request, string reply)
    {
        var turns = _model.Turns.Count;
        Ask(page, request);
        page.WaitForAssertion(() => Assert.Equal(turns + 1, _model.Turns.Count));
        await _model.Say(reply);
        _model.Finish();
        page.WaitForAssertion(() => Assert.DoesNotContain(page.FindAll("button"), button => button.TextContent.Trim() == "Stop"));
    }

    [Fact]
    public async Task A_description_builds_a_widget_that_draws_as_it_arrives()
    {
        await using var context = NewContext();
        var page = context.Render<WidgetBuilder>();
        var designer = context.Services.GetRequiredService<WidgetDesigner>();

        Ask(page, "Wind for North field");
        page.WaitForAssertion(() => Assert.Single(_model.Turns));
        await _model.Say(Wind);

        page.WaitForAssertion(() => Assert.Contains("Wind this week", page.Find(".builder-tile").TextContent));
        var turn = _model.Turns[0];
        Assert.Equal(designer.GeneratePrompt, turn[0].Text);
        Assert.Equal("Wind for North field", turn[1].Text);

        _model.Finish();
        page.WaitForAssertion(() => Assert.Equal("Wind for North field", page.Find(".builder-name input").GetAttribute("value")));
        Assert.Contains("Not saved yet", page.Markup);
    }

    [Fact]
    public async Task A_refine_sends_the_current_widget_in_edit_mode_and_merges_the_patch()
    {
        await using var context = NewContext();
        var page = context.Render<WidgetBuilder>();
        var designer = context.Services.GetRequiredService<WidgetDesigner>();
        await BuildAsync(page, "Wind for North field", Wind);

        await BuildAsync(page, "Say it is for River bend", "title = TextContent(\"River bend wind\")\n");

        var refine = _model.Turns[1];
        Assert.Equal(designer.EditPrompt, refine[0].Text);
        Assert.StartsWith(WidgetDesigner.RefineLead, refine[1].Text);
        Assert.Contains("TextContent(\"Wind this week\")", refine[1].Text);
        Assert.EndsWith($"{WidgetDesigner.RequestLead} Say it is for River bend", refine[1].Text.TrimEnd());
        page.WaitForAssertion(() =>
        {
            var tile = page.Find(".builder-tile").TextContent;
            Assert.Contains("River bend wind", tile);
            Assert.DoesNotContain("Wind this week", tile);
        });
    }

    [Fact]
    public async Task A_widget_wired_wrong_shows_the_wiring_finding_and_refine_sends_it_back()
    {
        await using var context = NewContext();
        var page = context.Render<WidgetBuilder>();
        await BuildAsync(page, "Forecast for my first field", """
            root = Card([days])
            locs = Query("listLocations", {}, {locations: []})
            forecast = Query("getForecast", {location: locs.locations[0].id, days: 7}, {days: []})
            days = TextContent("Days")

            """);

        page.WaitForAssertion(() => Assert.Contains("x-query-reads-query", page.Find(".builder-preview .mud-alert").TextContent));

        await BuildAsync(page, "Fix it", "title = TextContent(\"Fixed\")\n");

        Assert.Contains(WidgetDesigner.ErrorsLead, _model.Turns[1][1].Text);
        Assert.Contains("x-query-reads-query", _model.Turns[1][1].Text);
    }

    [Fact]
    public async Task Undo_puts_back_the_widget_from_before_the_last_change()
    {
        await using var context = NewContext();
        var page = context.Render<WidgetBuilder>();
        await BuildAsync(page, "Wind for North field", Wind);
        await BuildAsync(page, "Rename it", "title = TextContent(\"Renamed\")\n");
        page.WaitForAssertion(() => Assert.Contains("Renamed", page.Find(".builder-tile").TextContent));

        await page.InvokeAsync(() => Button(page, "Undo").Click());

        page.WaitForAssertion(() => Assert.Contains("Wind this week", page.Find(".builder-tile").TextContent));
        Assert.Contains("builder-turn-undone", page.Markup);
    }

    [Fact]
    public async Task A_failed_reply_keeps_the_widget_it_started_from()
    {
        await using var context = NewContext();
        var page = context.Render<WidgetBuilder>();
        await BuildAsync(page, "Wind for North field", Wind);

        Ask(page, "Break it");
        page.WaitForAssertion(() => Assert.Equal(2, _model.Turns.Count));
        await _model.Say("title = TextContent(\"Half");
        _model.Fail("The provider went away.");

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Wind this week", page.Find(".builder-tile").TextContent);
            Assert.Contains("The provider went away.", page.Find(".builder-history").TextContent);
        });
    }

    [Fact]
    public async Task Saving_writes_the_widget_and_saving_again_updates_it()
    {
        await using var context = NewContext();
        var page = context.Render<WidgetBuilder>();
        await BuildAsync(page, "Wind for North field", Wind);
        page.Find(".builder-name input").Input("Wind");

        await page.InvokeAsync(() => Button(page, "Save").Click());

        var saved = Assert.Single(_store.List());
        Assert.Equal("Wind", saved.Name);
        Assert.Equal(Wind, saved.Program);
        Assert.Equal(["Wind for North field"], saved.Requests);
        Assert.EndsWith($"/widgets/{saved.Id}", context.Services.GetRequiredService<NavigationManager>().Uri);
        page.WaitForAssertion(() => Assert.True(Button(page, "Save").HasAttribute("disabled")));

        await BuildAsync(page, "Rename it", "title = TextContent(\"Renamed\")\n");
        page.WaitForAssertion(() => Assert.Contains("Unsaved changes", page.Markup));
        await page.InvokeAsync(() => Button(page, "Save").Click());

        var updated = Assert.Single(_store.List());
        Assert.Equal(saved.Id, updated.Id);
        Assert.Contains("Renamed", updated.Program);
        Assert.Equal(["Wind for North field", "Rename it"], updated.Requests);
    }

    [Fact]
    public async Task A_saved_widget_opens_in_the_builder_with_how_it_was_made()
    {
        var widget = new Widget { Id = Guid.NewGuid(), Name = "Saved wind", Program = Wind, Requests = ["Wind for North field"] };
        _store.Save(widget);
        await using var context = NewContext();

        var page = context.Render<WidgetBuilder>(parameters => parameters.Add(builder => builder.Id, widget.Id));

        page.WaitForAssertion(() => Assert.Contains("Wind this week", page.Find(".builder-tile").TextContent));
        Assert.Equal("Saved wind", page.Find(".builder-name input").GetAttribute("value"));
        Assert.Contains("Wind for North field", page.Find(".builder-history").TextContent);
        Assert.NotNull(Button(page, "Refine"));
        Assert.True(Button(page, "Undo").HasAttribute("disabled"));
    }

    [Fact]
    public async Task A_hand_edit_to_the_program_changes_the_preview()
    {
        await using var context = NewContext();
        var page = context.Render<WidgetBuilder>();
        await BuildAsync(page, "Wind for North field", Wind);

        await page.InvokeAsync(() => page.Find(".mud-expand-panel-header").Click());
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".builder-source textarea")));
        page.Find(".builder-source textarea").Input("root = Card([TextContent(\"Typed by hand\")])");

        page.WaitForAssertion(() => Assert.Contains("Typed by hand", page.Find(".builder-tile").TextContent), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Saved_widgets_draw_live_and_delete_asks_first()
    {
        var widget = new Widget { Id = Guid.NewGuid(), Name = "Saved wind", Program = Wind };
        _store.Save(widget);
        await using var context = NewContext();
        var dialogs = context.Render<MudDialogProvider>();

        var page = context.Render<SavedWidgets>();

        page.WaitForAssertion(() => Assert.Contains("Wind this week", page.Find(".widget-tile").TextContent));
        Assert.Equal($"/widgets/{widget.Id}", page.Find($"a[aria-label='Edit Saved wind']").GetAttribute("href"));

        await page.ClickWhere("button[aria-label='Delete Saved wind']");
        dialogs.WaitForAssertion(() => Assert.Contains("Delete this widget?", dialogs.Markup));
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").First(button => button.TextContent.Trim() == "Delete").Click());

        page.WaitForAssertion(() => Assert.Contains("No widgets saved yet", page.Markup));
        Assert.Empty(_store.List());
    }
}
