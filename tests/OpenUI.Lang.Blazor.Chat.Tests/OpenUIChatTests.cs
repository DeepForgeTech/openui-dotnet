using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.AI;
using OpenUI.Lang.Prompting;

namespace OpenUI.Lang.Blazor.Chat.Tests;

/// <summary>The chat component with a model the test feeds one piece at a time.</summary>
public sealed class OpenUIChatTests
{
    private readonly ScriptedModel _model = new();
    private readonly OpenUIInMemoryConversationStore _store = new();

    private BunitContext NewContext(Action<OpenUIBlazorChatOptions>? configure = null) => ChatTestContext.Create(_model, _store, configure);

    // On the renderer's thread, since a piece of the last reply can land between a find and its event.
    private static async Task Send(IRenderedComponent<OpenUIChat> chat, string text)
    {
        await chat.InvokeAsync(() => chat.Find("input").Input(text));
        await chat.InvokeAsync(() => chat.Find("form").Submit());
    }

    private static void WaitForTheEnd(IRenderedComponent<OpenUIChat> chat) =>
        chat.WaitForAssertion(() => Assert.Empty(chat.FindAll("button[aria-label=Stop]")));

    private void FinishReply(IRenderedComponent<OpenUIChat> chat)
    {
        _model.Finish();
        WaitForTheEnd(chat);
    }

    private async Task<OpenUIConversation> Stored() => Assert.Single(await _store.ListAsync());

    [Fact]
    public async Task A_reply_draws_components_as_it_arrives_and_the_stream_end_finishes_it()
    {
        await using var context = NewContext();
        var chat = context.Render<OpenUIChat>();

        await Send(chat, "Can we spray?");
        chat.WaitForAssertion(() => Assert.Contains("Can we spray?", chat.Find(".oui-chat-user").TextContent));
        await _model.Say("root = Card([one, two])\none = TextContent(\"First line\")\n");

        chat.WaitForAssertion(() =>
        {
            Assert.Contains("First line", chat.Markup);
            Assert.NotEmpty(chat.FindAll("button[aria-label=Stop]"));
        });

        await _model.Say("two = TextContent(\"Second line\")\n");
        _model.Finish();

        chat.WaitForAssertion(() =>
        {
            Assert.Contains("Second line", chat.Markup);
            Assert.Empty(chat.FindAll("button[aria-label=Stop]"));
        });
    }

    [Fact]
    public async Task The_first_message_tells_the_host_the_new_conversations_id_and_it_stays_open_at_that_id()
    {
        await using var context = NewContext();
        Guid? announced = null;
        var chat = context.Render<OpenUIChat>(parameters => parameters
            .Add(component => component.ConversationIdChanged, EventCallback.Factory.Create<Guid?>(this, id => announced = id)));

        await Send(chat, "Go");
        await _model.Say("root = Card([TextContent(\"Kept\")])\n");
        chat.WaitForAssertion(() => Assert.NotNull(announced));
        chat.Render(parameters => parameters.Add(component => component.ConversationId, announced));
        FinishReply(chat);

        Assert.Equal((await Stored()).Id, announced);
        Assert.Contains("Kept", chat.Markup);
    }

    [Fact]
    public async Task Stop_ends_the_reply_and_keeps_what_arrived()
    {
        await using var context = NewContext();
        var chat = context.Render<OpenUIChat>();

        await Send(chat, "Go");
        await _model.Say("root = Card([line])\nline = TextContent(\"Kept line\")\n");
        chat.WaitForAssertion(() => Assert.Contains("Kept line", chat.Markup));

        await chat.ClickWhere("button[aria-label=Stop]");

        chat.WaitForAssertion(() =>
        {
            Assert.Empty(chat.FindAll("button[aria-label=Stop]"));
            Assert.Contains("Kept line", chat.Markup);
        });
        Assert.Contains("Kept line", (await Stored()).Messages[1].Text);
    }

    [Fact]
    public async Task Deleting_a_conversation_mid_reply_does_not_bring_it_back()
    {
        await using var context = NewContext();
        var chat = context.Render<OpenUIChat>();

        await Send(chat, "Go");
        await _model.Say("root = Card([TextContent(\"Going\")])\n");
        chat.WaitForAssertion(() => Assert.Contains("Going", chat.Markup));
        await _store.DeleteAsync((await Stored()).Id);

        await chat.ClickWhere("button[aria-label=Stop]");

        WaitForTheEnd(chat);
        Assert.Empty(await _store.ListAsync());
    }

    [Fact]
    public async Task A_reply_stopped_before_its_first_word_says_so()
    {
        await using var context = NewContext();
        var chat = context.Render<OpenUIChat>();

        await Send(chat, "Go");
        chat.WaitForAssertion(() => Assert.NotEmpty(chat.FindAll("button[aria-label=Stop]")));
        await chat.ClickWhere("button[aria-label=Stop]");

        chat.WaitForAssertion(() => Assert.Contains("Stopped before any reply came", chat.Markup));
    }

    [Fact]
    public async Task A_follow_up_goes_back_to_the_model_with_its_context_and_form_values()
    {
        await using var context = NewContext();
        var chat = context.Render<OpenUIChat>();

        await Send(chat, "Go");
        await _model.Say("$crop = \"Corn\"\nroot = Button(\"More\", @ToAssistant(\"Tell me more\", {why: \"curious\"}))\n");
        FinishReply(chat);

        await chat.ClickWhere("button", "More");

        chat.WaitForAssertion(() => Assert.Equal(2, _model.Turns.Count));
        var message = _model.Turns[1][^1];
        Assert.Equal(ChatRole.User, message.Role);
        Assert.StartsWith("Tell me more\ncontext and form values = [\"User clicked: Tell me more\"", message.Text);
        Assert.Contains("\"Corn\"", message.Text);
        Assert.Contains("\"why\":\"curious\"", message.Text);
        Assert.Equal(ChatRole.Assistant, _model.Turns[1][^2].Role);
        FinishReply(chat);
    }

    [Fact]
    public async Task An_open_url_action_goes_to_the_host()
    {
        await using var context = NewContext();
        string? opened = null;
        var chat = context.Render<OpenUIChat>(parameters => parameters
            .Add(component => component.OnOpenUrl, EventCallback.Factory.Create<string>(this, url => opened = url)));

        await Send(chat, "Go");
        await _model.Say("root = Button(\"Read\", @OpenUrl(\"https://example.com/drift\"))\n");
        FinishReply(chat);

        await chat.ClickWhere("button", "Read");

        chat.WaitForAssertion(() => Assert.Equal("https://example.com/drift", opened));
    }

    [Fact]
    public async Task An_action_whose_context_is_too_large_is_refused_and_the_conversation_does_not_grow()
    {
        await using var context = NewContext();
        var chat = context.Render<OpenUIChat>();
        // 80,000 control characters are inside what a page's session holds, but each is six characters once it is JSON. A
        // statement is at most 256 KiB of source, so they come in two.
        var control = string.Concat(Enumerable.Repeat("\\u0001", 40_000));
        var program = $"a = \"{control}\"\nb = \"{control}\"\n$big = [a, b]\nroot = Button(\"More\", @ToAssistant(\"Tell me more\"))\n";

        await Send(chat, "Go");
        await _model.Say(program);
        FinishReply(chat);

        await chat.ClickWhere("button", "More");

        chat.WaitForAssertion(() => Assert.Contains("carries too much data", chat.Find(".oui-chat-failure").TextContent));
        Assert.Single(_model.Turns);
        Assert.Equal(2, (await Stored()).Messages.Count);
    }

    [Fact]
    public async Task A_reply_that_repeats_a_long_string_past_what_a_page_holds_says_so()
    {
        await using var context = NewContext();
        var chat = context.Render<OpenUIChat>();
        var long32Kb = new string('x', 32 * 1024);

        await Send(chat, "Go");
        await _model.Say($"a = \"{long32Kb}\"\nroot = Stack([{string.Join(", ", Enumerable.Repeat("TextContent(a)", 16))}])\n");
        FinishReply(chat);

        chat.WaitForAssertion(() => Assert.Contains($"{OpenUIProgram.DefaultMaxTextChars} characters", chat.Markup));
    }

    [Fact]
    public async Task A_reply_with_errors_shows_them_and_the_next_message_carries_them()
    {
        await using var context = NewContext();
        var chat = context.Render<OpenUIChat>();

        await Send(chat, "Go");
        await _model.Say("root = Card([Nope(\"x\")])\n");
        _model.Finish();
        chat.WaitForAssertion(() =>
        {
            Assert.Empty(chat.FindAll("button[aria-label=Stop]"));
            Assert.Contains("Nope", chat.Find(".oui-chat-errors").TextContent);
        });

        await Send(chat, "Again");

        chat.WaitForAssertion(() => Assert.Equal(2, _model.Turns.Count));
        Assert.Contains("Your last program had these errors", _model.Turns[1][^1].Text);
        Assert.Contains("Nope", _model.Turns[1][^1].Text);
        FinishReply(chat);
    }

    [Fact]
    public async Task A_finished_reply_wired_wrong_shows_the_wiring_finding_and_the_next_message_carries_it()
    {
        await using var context = NewContext(options =>
        {
            options.CheckWiring = true;
            options.PromptOptions = new PromptOptions { Tools = [new ToolDescriptor("listLocations"), new ToolDescriptor("getForecast")] };
        });
        var chat = context.Render<OpenUIChat>();

        await Send(chat, "Forecast for my first field");
        await _model.Say("root = Card([days])\nlocs = Query(\"listLocations\", {}, {locations: []})\n");
        await _model.Say("forecast = Query(\"getForecast\", {location: locs.locations[0].id, days: 7}, {days: []})\ndays = TextContent(\"Days\")\n");
        Assert.DoesNotContain("x-query-reads-query", chat.Markup);
        _model.Finish();
        chat.WaitForAssertion(() => Assert.Contains("x-query-reads-query", chat.Find(".oui-chat-reply .oui-chat-errors").TextContent));

        await Send(chat, "Again");

        chat.WaitForAssertion(() => Assert.Equal(2, _model.Turns.Count));
        Assert.Contains("x-query-reads-query", _model.Turns[1][^1].Text);
        FinishReply(chat);
    }

    [Fact]
    public async Task Without_the_wiring_check_a_reply_wired_wrong_shows_no_finding()
    {
        await using var context = NewContext();
        var chat = context.Render<OpenUIChat>();

        await Send(chat, "Forecast");
        await _model.Say("root = Card([days])\nlocs = Query(\"listLocations\", {}, {locations: []})\nforecast = Query(\"getForecast\", {location: locs.locations[0].id}, {days: []})\ndays = TextContent(\"Days\")\n");
        FinishReply(chat);

        chat.WaitForAssertion(() => Assert.Contains("Days", chat.Markup));
        Assert.DoesNotContain("x-query-reads-query", chat.Markup);
    }

    [Fact]
    public async Task A_reply_that_is_not_a_program_still_shows_its_text()
    {
        await using var context = NewContext();
        var chat = context.Render<OpenUIChat>();

        await Send(chat, "Go");
        await _model.Say("Sorry, I can only talk about spraying.");
        FinishReply(chat);

        chat.WaitForAssertion(() => Assert.Contains("Sorry, I can only talk about spraying.", chat.Find(".oui-chat-text").TextContent));
    }

    [Fact]
    public async Task A_stored_conversation_opens_with_its_reply_drawn()
    {
        var saved = new OpenUIConversation
        {
            Id = Guid.NewGuid(),
            Title = "Saved",
            Messages =
            [
                new OpenUIChatEntry { Role = OpenUIChatRoles.User, Text = "Before" },
                new OpenUIChatEntry { Role = OpenUIChatRoles.Assistant, Text = "root = Card([TextContent(\"Drawn again\")])\n" },
            ],
        };
        await _store.SaveAsync(saved);

        await using var context = NewContext();
        var chat = context.Render<OpenUIChat>(parameters => parameters.Add(component => component.ConversationId, saved.Id));

        chat.WaitForAssertion(() =>
        {
            Assert.Contains("Before", chat.Find(".oui-chat-user").TextContent);
            Assert.Contains("Drawn again", chat.Markup);
        });
    }

    [Fact]
    public async Task A_model_failure_shows_and_keeps_the_conversation()
    {
        await using var context = NewContext();
        var chat = context.Render<OpenUIChat>();

        await Send(chat, "Go");
        await _model.Say("root = Card([TextContent(\"Partial\")])\n");
        _model.Fail("The model went away.");

        chat.WaitForAssertion(() =>
        {
            Assert.Contains("The model went away.", chat.Find(".oui-chat-failure").TextContent);
            Assert.Empty(chat.FindAll("button[aria-label=Stop]"));
        });
        Assert.Contains("Partial", (await Stored()).Messages[1].Text);
    }

    [Fact]
    public async Task A_failure_can_be_drawn_by_the_host()
    {
        await using var context = NewContext();
        var chat = context.Render<OpenUIChat>(parameters => parameters
            .Add(component => component.FailureContent, failure => $"<p class=\"host-failure\">{failure}</p>"));

        await Send(chat, "Go");
        _model.Fail("Down.");

        chat.WaitForAssertion(() => Assert.Equal("Down.", chat.Find(".host-failure").TextContent));
        Assert.Empty(chat.FindAll(".oui-chat-failure"));
    }

    [Fact]
    public async Task A_reply_past_the_input_limit_stops_there_says_so_and_keeps_what_arrived()
    {
        await using var context = NewContext();
        var chat = context.Render<OpenUIChat>();
        var chunk = new string('x', 1024 * 1024);

        await Send(chat, "Go");
        for (var sent = 0; sent <= OpenUILimits.Default.MaxInputChars; sent += chunk.Length)
        {
            await _model.Say(chunk);
        }

        chat.WaitForAssertion(() =>
        {
            Assert.Contains("The reply was cut off after", chat.Markup);
            Assert.Empty(chat.FindAll("button[aria-label=Stop]"));
        });
        var kept = (await Stored()).Messages[1].Text;
        Assert.True(kept.Length > 0 && kept.Length <= OpenUILimits.Default.MaxInputChars, $"Kept {kept.Length} characters.");
    }

    [Fact]
    public async Task A_reply_that_takes_longer_than_its_limit_is_cut_off_says_so_and_keeps_what_arrived()
    {
        await using var context = NewContext(options => options.ReplyTimeout = TimeSpan.FromMilliseconds(300));
        var chat = context.Render<OpenUIChat>();

        await Send(chat, "Go");
        await _model.Say("root = Card([TextContent(\"Got this far\")])\n");

        chat.WaitForAssertion(() =>
        {
            Assert.Contains("The model took too long to answer.", chat.Markup);
            Assert.Empty(chat.FindAll("button[aria-label=Stop]"));
        });
        Assert.Contains("Got this far", (await Stored()).Messages[1].Text);
    }

    public static TheoryData<Exception> ProviderFailures => new()
    {
        new IOException("Connection reset by secret.example:443"),
        new HttpRequestException("Response status code does not indicate success: 401 (key sk-secret)."),
        new InvalidOperationException("prompt: You are ..."),
    };

    [Theory]
    [MemberData(nameof(ProviderFailures))]
    public async Task A_providers_own_failure_shows_as_a_fixed_sentence_and_not_its_text(Exception failure)
    {
        await using var context = NewContext();
        var chat = context.Render<OpenUIChat>();

        await Send(chat, "Go");
        _model.Fail(failure);

        chat.WaitForAssertion(() => Assert.Equal(OpenUIChatSession.FailedSentence, chat.Find(".oui-chat-failure").TextContent));
        Assert.DoesNotContain("secret", chat.Markup);
    }

    [Fact]
    public async Task A_model_client_that_gives_up_on_its_own_shows_as_a_failure_and_keeps_the_conversation()
    {
        await using var context = NewContext();
        var chat = context.Render<OpenUIChat>();

        await Send(chat, "Go");
        await _model.Say("root = Card([TextContent(\"Partial\")])\n");
        _model.Fail(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."));

        chat.WaitForAssertion(() =>
        {
            Assert.Contains("The model took too long to answer.", chat.Markup);
            Assert.Empty(chat.FindAll("button[aria-label=Stop]"));
        });
        Assert.Contains("Partial", (await Stored()).Messages[1].Text);
    }

    [Fact]
    public async Task The_empty_state_is_the_hosts_and_can_send()
    {
        await using var context = NewContext();
        var chat = context.Render<OpenUIChat>(parameters => parameters
            .Add(component => component.EmptyContent, session => builder =>
            {
                builder.OpenElement(0, "button");
                builder.AddAttribute(1, "class", "starter");
                builder.AddAttribute(2, "onclick", EventCallback.Factory.Create(this, () => session.SendAsync("From a starter")));
                builder.AddContent(3, "Start");
                builder.CloseElement();
            }));

        await chat.ClickWhere("button.starter");

        chat.WaitForAssertion(() => Assert.Equal("From a starter", chat.Find(".oui-chat-user").TextContent));
        Assert.Empty(chat.FindAll(".starter"));
        FinishReply(chat);
    }

    [Fact]
    public async Task Leaving_the_page_stops_the_reply_and_keeps_what_arrived()
    {
        var context = NewContext();
        var chat = context.Render<OpenUIChat>();
        await Send(chat, "Go");
        await _model.Say("root = Card([TextContent(\"Before leaving\")])\n");
        chat.WaitForAssertion(() => Assert.Contains("Before leaving", chat.Markup));

        await context.DisposeAsync();

        await WaitUntil(async () => (await Stored()).Messages[1].Text.Contains("Before leaving", StringComparison.Ordinal));
    }

    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out.");
            await Task.Delay(20);
        }
    }
}
