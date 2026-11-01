using System.Runtime.CompilerServices;
using Bunit;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenUI.Lang.Blazor;
using OpenUI.Lang.Samples.Llm;
using OpenUI.Lang.Samples.WidgetPlayground.Catalog;
using OpenUI.Lang.Samples.WidgetPlayground.Components.Pages;
using OpenUI.Lang.Samples.WidgetPlayground.Examples;
using OpenUI.Lang.Samples.WidgetPlayground.Generation;

namespace OpenUI.Lang.Samples.WidgetPlayground.Tests;

/// <summary>Generated programs that used to end the circuit. A render that throws would fail these.</summary>
public sealed class PlaygroundPageTests : BunitContext
{
    private const string TooDeep = "nested too deeply";

    public PlaygroundPageTests()
    {
        DefaultWaitTimeout = TimeSpan.FromSeconds(30);
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(PlaygroundLibraries.Build());
        Services.AddSingleton(ExampleCatalog.Load());
        Services.AddSingleton<IChatClient>(new FakeChatClient(_ => "root = Text(\"unused\")"));
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        Services.AddLogging();
        Services.AddSingleton<ProgramDesigner>();
        Services.AddSingleton(new LlmSettings(LlmSettings.FakeProvider, null));
    }

    private static string Stacks(int depth) =>
        string.Concat(Enumerable.Repeat("Stack([", depth)) + "Text(\"deep\")" + string.Concat(Enumerable.Repeat("])", depth));

    private const string PageWithPicker = "$location = \"north\"\nroot = Page(\"Named\", [picker, map])\npicker = LocationPicker($location, 3, 1)\nmap = MapView($location, 4, 3)";


    [Fact]
    public void The_fake_provider_warns_at_the_prompt_box_and_says_each_reply_is_canned()
    {
        var page = Render<WidgetMode>();
        Assert.Contains(LlmSettings.FakeNotice, page.Find(".prompt-bar").TextContent);
        Assert.Equal(LlmSettings.FakePlaceholder, page.Find("textarea.prompt-input").GetAttribute("placeholder"));

        page.Find("textarea.prompt-input").Input("Anything");
        page.Find("button.generate").Click();

        page.WaitForAssertion(() => Assert.Contains(LlmSettings.CannedReplyNotice, page.Find("ol.history").TextContent));
    }

    [Fact]
    public void A_real_provider_shows_none_of_it()
    {
        Services.AddSingleton(new LlmSettings(LlmSettings.OpenAiProvider, null));
        var page = Render<WidgetMode>();

        page.Find("textarea.prompt-input").Input("Anything");
        page.Find("button.generate").Click();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("ol.history li")));
        Assert.DoesNotContain("Fake model", page.Markup);
        Assert.DoesNotContain(LlmSettings.CannedReplyNotice, page.Markup);
        Assert.NotEqual(LlmSettings.FakePlaceholder, page.Find("textarea.prompt-input").GetAttribute("placeholder"));
    }

    [Fact]
    public void A_state_value_too_deep_for_JSON_says_so_in_the_widget_state_table_and_the_page_keeps_working()
    {
        var page = Render<WidgetMode>();
        page.Find("textarea.editor").Input($"$s = {Stacks(25)}\nroot = Stack([Text(\"still here\")])");
        page.WaitForAssertion(() => Assert.Contains(TooDeep, page.Find("table.state").TextContent));

        page.Find("textarea.editor").Input("$s = 1\nroot = Stack([Text(\"recovered\")])");

        page.WaitForAssertion(() =>
        {
            Assert.DoesNotContain(TooDeep, page.Find("table.state").TextContent);
            Assert.Contains("recovered", page.Markup);
        });
    }

    [Fact]
    public void A_state_value_too_large_to_show_says_so_in_the_widget_state_table()
    {
        var page = Render<WidgetMode>();

        var text = new string('x', 20_000);
        page.Find("textarea.editor").Input($"t = \"{text}\"\n$s = [{string.Join(' ', Enumerable.Repeat("t", 20))}]\nroot = Stack([Text(\"still here\")])");

        page.WaitForAssertion(() => Assert.Contains("too large to show", page.Find("table.state").TextContent));
        Assert.Contains("still here", page.Markup);
    }

    [Fact]
    public void A_program_past_the_drawing_limit_reports_it_instead_of_drawing_it()
    {
        var page = Render<WidgetMode>();
        var items = string.Join(", ", Enumerable.Repeat("Text(\"ZZZ\")", OpenUIProgram.DefaultMaxValueNodes + 1000));

        page.Find("textarea.editor").Input($"items = [{items}]\nroot = Stack(items)");

        page.WaitForAssertion(() => Assert.Contains($"more than {OpenUIProgram.DefaultMaxValueNodes} nodes", page.Find(".editor-pane").TextContent));
        Assert.DoesNotContain("ZZZ", page.Find(".live-pane").TextContent);
    }

    [Fact]
    public void A_nested_action_in_state_says_so_too()
    {
        var page = Render<WidgetMode>();
        var actions = string.Concat(Enumerable.Repeat("@Set($q, ", 30)) + "1" + new string(')', 30);

        page.Find("textarea.editor").Input($"$q = 0\n$d = {actions}\nroot = Stack([Text(\"still here\")])");

        page.WaitForAssertion(() => Assert.Contains("still here", page.Markup));
        page.Find("textarea.editor").Input("root = Stack([Text(\"recovered\")])");
        page.WaitForAssertion(() => Assert.Contains("recovered", page.Markup));
    }

    [Fact]
    public void A_page_variable_too_deep_to_store_is_a_problem_on_the_page_and_the_last_good_page_stays()
    {
        var page = Render<PageMode>();
        page.Find("textarea.editor").Input(PageWithPicker);
        page.WaitForAssertion(() => Assert.Contains("Named", page.Find(".live-pane").TextContent));

        var arrays = new string('[', 63) + new string(']', 63);
        page.Find("textarea.editor").Input($"root = Page(\"Deep\", [LocationPicker($location, 3, 1)])\n$location = \"north\"\n$a = {arrays}");

        page.WaitForAssertion(() => Assert.Contains(TooDeep, page.Find(".editor-pane").TextContent));
        Assert.Contains("Named", page.Find(".live-pane").TextContent);

        page.Find("textarea.editor").Input(PageWithPicker.Replace("Named", "Recovered"));
        page.WaitForAssertion(() =>
        {
            Assert.Contains("Recovered", page.Find(".live-pane").TextContent);
            Assert.DoesNotContain(TooDeep, page.Find(".editor-pane").TextContent);
        });
    }

    [Fact]
    public void Two_inline_widgets_get_their_own_keys_when_the_page_is_refined_to_named_statements()
    {
        var page = Render<PageMode>();
        page.Find("textarea.editor").Input("$location = \"north\"\nroot = Page(\"Inline\", [LocationPicker($location, 3, 1), MapView($location, 4, 3)])");
        page.WaitForAssertion(() => Assert.Contains("Inline", page.Find(".live-pane").TextContent));
        Assert.Equal(2, page.FindAll(".page-cell").Count);

        page.Find("textarea.editor").Input(PageWithPicker);

        page.WaitForAssertion(() => Assert.Contains("Named", page.Find(".live-pane").TextContent));
        Assert.Equal(2, page.FindAll(".page-cell").Count);
    }

    [Fact]
    public void One_statement_placed_twice_gets_two_keys()
    {
        var page = Render<PageMode>();

        page.Find("textarea.editor").Input("$location = \"north\"\nmap = MapView($location, 4, 3)\nroot = Page(\"Twice\", [map, map])");

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Twice", page.Find(".live-pane").TextContent);
            Assert.Equal(2, page.FindAll(".page-cell").Count);
        });
    }

    private IRenderedComponent<PromptBar> RenderPromptBar(Func<CancellationToken, IAsyncEnumerable<string>> reply, List<ProgramUpdate> updates, TimeSpan? timeout = null)
    {
        Services.AddSingleton<IChatClient>(new ScriptedModel(reply));
        var bundle = Services.GetRequiredService<PlaygroundLibraries>().Widget;
        var page = Render<PromptBar>(parameters => parameters
            .Add(bar => bar.Bundle, bundle)
            .Add(bar => bar.Program, "")
            .Add(bar => bar.OnProgram, (ProgramUpdate update) => updates.Add(update))
            .Add(bar => bar.ReplyTimeout, timeout ?? TimeSpan.FromMinutes(5)));
        page.Find("textarea.prompt-input").Input("Anything");
        page.Find("button.generate").Click();
        return page;
    }

    private static async IAsyncEnumerable<string> ThenFail(Exception error)
    {
        yield return "root = Text(\"Partial\")\n";
        await Task.Yield();
        throw error;
    }

    [Fact]
    public void A_reply_past_the_input_limit_stops_there_says_so_and_keeps_what_arrived()
    {
        var updates = new List<ProgramUpdate>();
        var chunk = new string('x', 1024 * 1024);
        var page = RenderPromptBar(ct => Repeat(chunk, 6), updates);

        page.WaitForAssertion(() => Assert.Contains("cut off", page.Find("ol.history").TextContent));
        var kept = updates[^1];
        Assert.False(kept.Streaming);
        Assert.True(kept.Text.Length > 0 && kept.Text.Length <= OpenUILimits.Default.MaxInputChars, $"Kept {kept.Text.Length} characters.");
        Assert.Empty(page.FindAll("button.stop"));

        static async IAsyncEnumerable<string> Repeat(string text, int times)
        {
            for (var i = 0; i < times; i++)
            {
                await Task.Yield();
                yield return text;
            }
        }
    }

    [Fact]
    public void A_reply_that_takes_longer_than_its_limit_shows_as_a_failure_and_puts_the_editor_back()
    {
        var updates = new List<ProgramUpdate>();
        var page = RenderPromptBar(Stall, updates, TimeSpan.FromMilliseconds(300));

        page.WaitForAssertion(() => Assert.Contains("took too long", page.Find("ol.history").TextContent));
        Assert.Equal(new ProgramUpdate("", Streaming: false), updates[^1]);
        Assert.Empty(page.FindAll("button.stop"));

        static async IAsyncEnumerable<string> Stall([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return "root = Text(\"Slow\")\n";
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    [Fact]
    public void A_model_client_that_gives_up_on_its_own_shows_as_a_failure_and_puts_the_editor_back()
    {
        var updates = new List<ProgramUpdate>();
        var page = RenderPromptBar(ct => ThenFail(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.")), updates);

        page.WaitForAssertion(() => Assert.Contains("took too long", page.Find("ol.history").TextContent));
        Assert.Equal(new ProgramUpdate("", Streaming: false), updates[^1]);
        Assert.Empty(page.FindAll("button.stop"));
    }

    private sealed class ScriptedModel(Func<CancellationToken, IAsyncEnumerable<string>> reply) : StreamingChatClient
    {
        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var text in reply(cancellationToken).WithCancellation(cancellationToken))
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, text);
            }
        }
    }
}
