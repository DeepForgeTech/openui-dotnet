using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using OpenUI.Lang.Blazor.Chat;
using OpenUI.Lang.Samples.BlazorApp.Chat;
using OpenUI.Lang.Samples.BlazorApp.Components.Pages;
using OpenUI.Lang.Samples.BlazorApp.Weather;
using OpenUI.Lang.Samples.Llm;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor.Tests.Sample;

/// <summary>
/// The sample's chat page: its starters, its address, the fake model and its Mud parts. The send loop and the reply are the
/// chat package's, and its own tests cover them.
/// </summary>
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
            services.AddOpenUIComponents<OpenUI.Lang.Blazor.Mud.MudComponents>();
            services.AddSingleton(new LlmSettings(provider, null));
            services.AddSingleton<IChatClient>(_model);
            services.AddSingleton(_store);
            services.AddOpenUIChat(ChatPrompt.Configure).AddConversationStore(services => services.GetRequiredService<ConversationStore>());
            services.AddSingleton(new WeatherTools(new HttpClient(new NoApi()) { BaseAddress = new Uri("http://localhost:1") }, new ToolCallLog()));
            services.AddLogging();
        });
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Render<MudPopoverProvider>();
        return context;
    }

    private void FinishReply(IRenderedComponent<ChatPage> page)
    {
        _model.Finish();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("button[aria-label=Stop]")));
    }

    [Fact]
    public async Task A_starter_sends_its_question_and_the_address_names_the_new_conversation()
    {
        await using var context = NewContext();
        var navigation = context.Services.GetRequiredService<BunitNavigationManager>();
        var page = context.Render<ChatPage>();
        Assert.Equal("New chat", page.Find("h1").TextContent);

        await page.ClickWhere("button.chat-starter", "Show me the forecast");

        page.WaitForAssertion(() => Assert.Equal("Show me the forecast", page.Find(".oui-chat-user").TextContent));
        var stored = Assert.Single(await _store.ListAsync());
        page.WaitForAssertion(() => Assert.EndsWith($"/chat/{stored.Id}", navigation.Uri, StringComparison.Ordinal));
        Assert.Empty(page.FindAll(".chat-starter"));

        page.Render(parameters => parameters.Add(component => component.Id, stored.Id));
        await _model.Say("root = Card([TextContent(\"Still streaming here\")])\n");
        page.WaitForAssertion(() =>
        {
            Assert.Contains("Still streaming here", page.Markup);
            Assert.Equal("Show me the forecast", page.Find("h1").TextContent);
        });
        FinishReply(page);
    }

    [Fact]
    public async Task The_model_is_taught_the_sample_prompt_with_the_weather_tools()
    {
        await using var context = NewContext();
        var page = context.Render<ChatPage>();

        await page.ClickWhere("button.chat-starter", "Plan a spray job");

        page.WaitForAssertion(() => Assert.Single(_model.Turns));
        var prompt = _model.Turns[0][0];
        Assert.Equal(ChatRole.System, prompt.Role);
        Assert.Equal(new OpenUIComponents([new OpenUI.Lang.Blazor.Mud.MudComponents()]).SystemPrompt(ChatPrompt.Options), prompt.Text);
        FinishReply(page);
    }

    [Fact]
    public async Task A_stored_conversation_opens_at_its_address_with_its_title()
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
        var page = context.Render<ChatPage>(parameters => parameters.Add(component => component.Id, saved.Id));

        page.WaitForAssertion(() =>
        {
            Assert.Equal("Saved", page.Find("h1").TextContent);
            Assert.Contains("Before", page.Find(".oui-chat-user").TextContent);
            Assert.Contains("Drawn again", page.Markup);
        });
    }

    [Fact]
    public async Task A_link_in_a_reply_opens_only_after_the_person_agrees()
    {
        await using var context = NewContext();
        var dialog = context.Render<MudDialogProvider>();
        var page = context.Render<ChatPage>();

        await page.ClickWhere("button.chat-starter", "Can we spray");
        await _model.Say("root = Button(\"Read about drift\", @OpenUrl(\"https://example.com/drift\"))\n");
        FinishReply(page);

        await page.ClickWhere("button", "Read about drift");

        dialog.WaitForAssertion(() => Assert.Contains("https://example.com/drift", dialog.Markup));
        Assert.DoesNotContain(context.JSInterop.Invocations, invocation => invocation.Identifier == "open");
    }

    [Fact]
    public async Task A_failure_shows_as_a_mud_alert()
    {
        await using var context = NewContext();
        var page = context.Render<ChatPage>();

        await page.ClickWhere("button.chat-starter", "Can we spray");
        _model.Fail("The model went away.");

        page.WaitForAssertion(() => Assert.Contains("The model went away.", page.Find(".mud-alert").TextContent));
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
            await using var session = OpenUI.Lang.Runtime.OpenUISession.Create(TestLibrary.Mud.Library, new OpenUI.Lang.Runtime.OpenUISessionOptions { Tools = new WeatherTools(new HttpClient(new NoApi()) { BaseAddress = new Uri("http://localhost:1") }, new ToolCallLog()) });
            session.Load(program);
            Assert.True(session.Current.Errors.Count == 0, $"{message}: {string.Join("; ", session.Current.Errors.Select(error => error.Message))}");
            Assert.True(Descendants(session.Current.Root!).Any(IsCannedNotice), $"{message}: the notice is not drawn under the root.");
        }
    }

    private static bool IsCannedNotice(OuiComponent node) =>
        node.Type == "Callout" && node.Props.Any(prop => prop.Value.TryGetString(out var text) && text == LlmSettings.CannedReplyNotice);

    private static IEnumerable<OuiComponent> Descendants(OuiComponent node) =>
        node.Props.SelectMany(prop => ComponentsIn(prop.Value)).SelectMany(child => Descendants(child).Prepend(child));

    private static IEnumerable<OuiComponent> ComponentsIn(OuiValue value) =>
        value.TryGetComponent(out var component) ? [component] : value.TryGetArray(out var items) ? items.SelectMany(ComponentsIn) : [];
}
