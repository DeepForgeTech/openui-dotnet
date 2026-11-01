using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Bunit;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using OpenUI.Lang.Samples.BlazorApp.Chat;
using OpenUI.Lang.Samples.BlazorApp.Components.Pages;
using OpenUI.Lang.Samples.BlazorApp.Weather;
using OpenUI.Lang.Samples.Llm;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor.Tests.Sample;

/// <summary>The chat page with a model the test feeds one chunk at a time.</summary>
public sealed class ChatPageTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("openui-chat-").FullName;
    private readonly ScriptedModel _model = new();
    private readonly ConversationStore _store;

    public ChatPageTests() => _store = new ConversationStore(_directory, NullLogger<ConversationStore>.Instance);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private BunitContext NewContext(string provider = LlmSettings.OpenAiProvider)
    {
        var context = TestLibrary.NewContext(services =>
        {
            services.AddMudServices();
            services.AddSingleton(new LlmSettings(provider, null));
            services.AddSingleton<IChatClient>(_model);
            services.AddSingleton(_store);
            services.AddSingleton(new WeatherTools(new HttpClient(new NoApi()) { BaseAddress = new Uri("http://localhost:1") }, new ToolCallLog()));
            services.AddLogging();
        });
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Render<MudPopoverProvider>();
        return context;
    }

    private static void Send(IRenderedComponent<ChatPage> page, string text)
    {
        page.Find("input").Input(text);
        page.Find("form").Submit();
    }

    // Waits for the page to save, so nothing is writing when the test's directory goes.
    private void FinishReply(IRenderedComponent<ChatPage> page)
    {
        _model.Finish();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("button[aria-label=Stop]")));
    }

    [Fact]
    public async Task A_reply_draws_components_as_it_arrives_and_the_stream_end_finishes_it()
    {
        await using var context = NewContext();
        var page = context.Render<ChatPage>();

        Send(page, "Can we spray?");
        page.WaitForAssertion(() => Assert.Contains("Can we spray?", page.Find(".chat-user").TextContent));
        await _model.Say("root = Card(\"Spray\", [one, two])\none = Text(\"First line\")\n");

        page.WaitForAssertion(() =>
        {
            Assert.Contains("First line", page.Markup);
            Assert.NotEmpty(page.FindAll("button[aria-label=Stop]"));
        });

        await _model.Say("two = Text(\"Second line\")\n");
        _model.Finish();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Second line", page.Markup);
            Assert.Empty(page.FindAll("button[aria-label=Stop]"));
        });
    }

    [Fact]
    public async Task Stop_ends_the_reply_and_keeps_what_arrived()
    {
        await using var context = NewContext();
        var page = context.Render<ChatPage>();

        Send(page, "Go");
        await _model.Say("root = Card(\"Kept\", [line])\nline = Text(\"Kept line\")\n");
        page.WaitForAssertion(() => Assert.Contains("Kept line", page.Markup));

        await page.ClickWhere("button[aria-label=Stop]");

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll("button[aria-label=Stop]"));
            Assert.Contains("Kept line", page.Markup);
        });
        var stored = Assert.Single(_store.List());
        Assert.Contains("Kept line", stored.Messages[1].Text);
    }

    [Fact]
    public async Task Deleting_a_conversation_mid_reply_does_not_bring_it_back()
    {
        await using var context = NewContext();
        var page = context.Render<ChatPage>();

        Send(page, "Go");
        await _model.Say("root = Card(\"Going\", [])\n");
        page.WaitForAssertion(() => Assert.Contains("Going", page.Markup));
        _store.Delete(Assert.Single(_store.List()).Id);

        await page.ClickWhere("button[aria-label=Stop]");

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("button[aria-label=Stop]")));
        Assert.Empty(_store.List());
    }

    [Fact]
    public async Task A_reply_stopped_before_its_first_word_says_so()
    {
        await using var context = NewContext();
        var page = context.Render<ChatPage>();

        Send(page, "Go");
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("button[aria-label=Stop]")));
        await page.ClickWhere("button[aria-label=Stop]");

        page.WaitForAssertion(() => Assert.Contains("Stopped before any reply came", page.Markup));
    }

    [Fact]
    public async Task A_follow_up_goes_back_to_the_model_with_its_context_and_form_values()
    {
        await using var context = NewContext();
        var page = context.Render<ChatPage>();

        Send(page, "Go");
        await _model.Say("$crop = \"Corn\"\nroot = Button(\"More\", @ToAssistant(\"Tell me more\", {why: \"curious\"}))\n");
        _model.Finish();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("button[aria-label=Stop]")));

        await page.ClickWhere("button", "More");

        page.WaitForAssertion(() => Assert.Equal(2, _model.Turns.Count));
        var message = _model.Turns[1][^1];
        Assert.Equal(ChatRole.User, message.Role);
        Assert.StartsWith("Tell me more\ncontext and form values = [\"User clicked: Tell me more\"", message.Text);
        Assert.Contains("\"Corn\"", message.Text);
        Assert.Contains("\"why\":\"curious\"", message.Text);
        Assert.Equal(ChatRole.Assistant, _model.Turns[1][^2].Role);
        FinishReply(page);
    }

    [Fact]
    public async Task An_action_whose_context_is_too_large_is_refused_and_the_conversation_does_not_grow()
    {
        await using var context = NewContext();
        var page = context.Render<ChatPage>();
        // 3.2 million é is well inside what a session holds, but each is six characters once it is JSON.
        var program = $"a = \"{new string('é', 200_000)}\"\nb = a + a + a + a\n$big = [b b b b]\nroot = Button(\"More\", @ToAssistant(\"Tell me more\"))\n";

        Send(page, "Go");
        await _model.Say(program);
        FinishReply(page);

        await page.ClickWhere("button", "More");

        page.WaitForAssertion(() => Assert.Contains("carries too much data", page.Markup));
        Assert.Single(_model.Turns);
        Assert.Equal(2, Assert.Single(_store.List()).Messages.Count);
    }

    [Fact]
    public async Task A_reply_with_errors_shows_them_and_the_next_message_carries_them()
    {
        await using var context = NewContext();
        var page = context.Render<ChatPage>();

        Send(page, "Go");
        await _model.Say("root = Card(\"Bad\", [Nope(\"x\")])\n");
        _model.Finish();
        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll("button[aria-label=Stop]"));
            Assert.Contains("Nope", page.Markup);
        });

        Send(page, "Again");

        page.WaitForAssertion(() => Assert.Equal(2, _model.Turns.Count));
        Assert.Contains("Nope", _model.Turns[1][^1].Text);
        FinishReply(page);
    }

    [Fact]
    public async Task A_reply_that_is_not_a_program_still_shows_its_text()
    {
        await using var context = NewContext();
        var page = context.Render<ChatPage>();

        Send(page, "Go");
        await _model.Say("Sorry, I can only talk about spraying.");
        FinishReply(page);

        page.WaitForAssertion(() => Assert.Contains("Sorry, I can only talk about spraying.", page.Markup));
    }

    [Fact]
    public async Task A_stored_conversation_opens_with_its_reply_drawn()
    {
        var saved = new Conversation
        {
            Id = Guid.NewGuid(),
            Title = "Saved",
            Messages =
            [
                new ChatEntry { Role = "user", Text = "Before" },
                new ChatEntry { Role = "assistant", Text = "root = Card(\"Back\", [Text(\"Drawn again\")])\n" },
            ],
        };
        _store.Save(saved);

        await using var context = NewContext();
        var page = context.Render<ChatPage>(parameters => parameters.Add(component => component.Id, saved.Id));

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Before", page.Find(".chat-user").TextContent);
            Assert.Contains("Drawn again", page.Markup);
        });
    }

    [Fact]
    public async Task A_model_failure_shows_on_the_page_and_keeps_the_conversation()
    {
        await using var context = NewContext();
        var page = context.Render<ChatPage>();

        Send(page, "Go");
        await _model.Say("root = Card(\"Partial\", [])\n");
        _model.Fail("The model went away.");

        page.WaitForAssertion(() =>
        {
            Assert.Contains("The model went away.", page.Markup);
            Assert.Empty(page.FindAll("button[aria-label=Stop]"));
        });
        Assert.Contains("Partial", Assert.Single(_store.List()).Messages[1].Text);
    }

    [Fact]
    public async Task A_reply_past_the_input_limit_stops_there_says_so_and_keeps_what_arrived()
    {
        await using var context = NewContext();
        var page = context.Render<ChatPage>();
        var chunk = new string('x', 1024 * 1024);

        Send(page, "Go");
        for (var sent = 0; sent <= OpenUILimits.Default.MaxInputChars; sent += chunk.Length)
        {
            await _model.Say(chunk);
        }

        page.WaitForAssertion(() =>
        {
            Assert.Contains("The reply was cut off after", page.Markup);
            Assert.Empty(page.FindAll("button[aria-label=Stop]"));
        });
        var kept = Assert.Single(_store.List()).Messages[1].Text;
        Assert.True(kept.Length > 0 && kept.Length <= OpenUILimits.Default.MaxInputChars, $"Kept {kept.Length} characters.");
    }

    [Fact]
    public async Task A_reply_that_takes_longer_than_its_limit_is_cut_off_says_so_and_keeps_what_arrived()
    {
        await using var context = NewContext();
        var page = context.Render<ChatPage>(parameters => parameters.Add(component => component.ReplyTimeout, TimeSpan.FromMilliseconds(300)));

        Send(page, "Go");
        await _model.Say("root = Card(\"Slow\", [Text(\"Got this far\")])\n");

        page.WaitForAssertion(() =>
        {
            Assert.Contains("The model took too long to answer.", page.Markup);
            Assert.Empty(page.FindAll("button[aria-label=Stop]"));
        });
        Assert.Contains("Got this far", Assert.Single(_store.List()).Messages[1].Text);
    }

    [Fact]
    public async Task A_model_client_that_gives_up_on_its_own_shows_as_a_failure_and_keeps_the_conversation()
    {
        await using var context = NewContext();
        var page = context.Render<ChatPage>();

        Send(page, "Go");
        await _model.Say("root = Card(\"Partial\", [])\n");
        _model.Fail(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."));

        page.WaitForAssertion(() =>
        {
            Assert.Contains("The model took too long to answer.", page.Markup);
            Assert.Empty(page.FindAll("button[aria-label=Stop]"));
        });
        Assert.Contains("Partial", Assert.Single(_store.List()).Messages[1].Text);
    }

    [Theory]
    [InlineData(LlmSettings.FakeProvider, true)]
    [InlineData(LlmSettings.OpenAiProvider, false)]
    public async Task Only_the_fake_provider_warns_at_the_prompt_box_that_nothing_is_read(string provider, bool warns)
    {
        await using var context = NewContext(provider);
        var page = context.Render<ChatPage>();

        Assert.Equal(warns, page.Markup.Contains(LlmSettings.FakeNotice, StringComparison.Ordinal));
        Assert.Equal(warns, page.Find("input").GetAttribute("placeholder") == LlmSettings.FakePlaceholder);
    }

    [Fact]
    public async Task The_fake_provider_answers_each_kind_of_message_with_a_program_the_library_accepts()
    {
        foreach (var message in new[] { "Can we spray?", "Show me the forecast", "Plan a spray job", "Here is the plan\ncontext and form values = [\"x\"]" })
        {
            var program = FakeReplies.For([new ChatMessage(ChatRole.User, message)]);
            await using var session = OpenUI.Lang.Runtime.OpenUISession.Create(OpenUI.Lang.Blazor.Mud.MudLibrary.Definition.Library, new OpenUI.Lang.Runtime.OpenUISessionOptions { Tools = new WeatherTools(new HttpClient(new NoApi()) { BaseAddress = new Uri("http://localhost:1") }, new ToolCallLog()) });
            session.Load(program);
            Assert.True(session.Current.Errors.Count == 0, $"{message}: {string.Join("; ", session.Current.Errors.Select(error => error.Message))}");
            Assert.True(Descendants(session.Current.Root!).Any(IsCannedNotice), $"{message}: the notice is not drawn under the root.");
        }
    }

    private static bool IsCannedNotice(OuiComponent node) =>
        node.Type == "Alert" && node.Props.Any(prop => prop.Value.TryGetString(out var text) && text == LlmSettings.CannedReplyNotice);

    private static IEnumerable<OuiComponent> Descendants(OuiComponent node) =>
        node.Props.SelectMany(prop => ComponentsIn(prop.Value)).SelectMany(child => Descendants(child).Prepend(child));

    private static IEnumerable<OuiComponent> ComponentsIn(OuiValue value) =>
        value.TryGetComponent(out var component) ? [component] : value.TryGetArray(out var items) ? items.SelectMany(ComponentsIn) : [];

    /// <summary>Streams exactly what the test tells it to, and remembers each turn it was asked for.</summary>
    private sealed class ScriptedModel : IChatClient
    {
        private readonly List<IReadOnlyList<ChatMessage>> _turns = [];
        private Channel<string> _chunks = Channel.CreateUnbounded<string>();

        public IReadOnlyList<IReadOnlyList<ChatMessage>> Turns
        {
            get
            {
                lock (_turns)
                {
                    return [.. _turns];
                }
            }
        }

        public async Task Say(string text) => await _chunks.Writer.WriteAsync(text);

        public void Finish()
        {
            _chunks.Writer.TryComplete();
        }

        public void Fail(string message) => Fail(new IOException(message));

        public void Fail(Exception error) => _chunks.Writer.TryComplete(error);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            lock (_turns)
            {
                _turns.Add([.. messages]);
            }

            var chunks = _chunks;
            await foreach (var chunk in chunks.Reader.ReadAllAsync(cancellationToken))
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
            }

            // The next turn gets a stream of its own.
            _chunks = Channel.CreateUnbounded<string>();
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The page streams.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class NoApi : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No API in this test.");
    }
}
