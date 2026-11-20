using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace OpenUI.Lang.Blazor.Chat.Tests;

public sealed class StoreAndListTests
{
    private readonly OpenUIInMemoryConversationStore _store = new();

    private static OpenUIConversation Sample(string title) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Messages = [new OpenUIChatEntry { Role = OpenUIChatRoles.User, Text = title }],
    };

    [Fact]
    public async Task The_in_memory_store_lists_the_most_recently_saved_first_and_keeps_copies()
    {
        var first = Sample("First");
        var second = Sample("Second");
        await _store.SaveAsync(first);
        await _store.SaveAsync(second);
        await _store.SaveAsync(first);
        first.Messages.Clear();

        Assert.Equal(["First", "Second"], (await _store.ListAsync()).Select(conversation => conversation.Title));
        Assert.Single((await _store.FindAsync(first.Id))!.Messages);
    }

    [Fact]
    public async Task The_in_memory_store_says_when_it_changes()
    {
        var heard = 0;
        _store.Changed += () => heard++;
        var saved = Sample("Heard");

        await _store.SaveAsync(saved);
        await _store.DeleteAsync(saved.Id);
        await _store.DeleteAsync(saved.Id);

        Assert.Equal(2, heard);
        Assert.Null(await _store.FindAsync(saved.Id));
    }

    [Fact]
    public async Task The_list_links_each_conversation_follows_the_store_and_deletes()
    {
        await using var context = ChatTestContext.Create(new ScriptedModel(), _store);
        var first = Sample("First");
        await _store.SaveAsync(first);
        OpenUIConversation? deleted = null;
        var list = context.Render<OpenUIConversationList>(parameters => parameters
            .Add(component => component.Href, conversation => $"/chat/{conversation.Id}")
            .Add(component => component.OnDelete, EventCallback.Factory.Create<OpenUIConversation>(this, conversation => deleted = conversation)));

        Assert.Equal($"/chat/{first.Id}", list.Find("a.oui-conversation-link").GetAttribute("href"));
        Assert.Equal("Recent chats", list.Find(".oui-conversations-heading").TextContent);

        await _store.SaveAsync(Sample("Second"));
        list.WaitForAssertion(() => Assert.Equal(["Second", "First"], list.FindAll(".oui-conversation-link").Select(link => link.TextContent)));

        await list.InvokeAsync(() => list.Find("button[aria-label='Delete First']").Click());

        list.WaitForAssertion(() => Assert.Single(list.FindAll(".oui-conversation-link")));
        Assert.Equal(first.Id, deleted?.Id);
        Assert.Null(await _store.FindAsync(first.Id));
    }

    [Fact]
    public async Task Without_an_address_the_list_calls_the_host_with_the_conversation_pressed()
    {
        await using var context = ChatTestContext.Create(new ScriptedModel(), _store);
        var saved = Sample("Pick me");
        await _store.SaveAsync(saved);
        OpenUIConversation? picked = null;
        var list = context.Render<OpenUIConversationList>(parameters => parameters
            .Add(component => component.OnSelect, EventCallback.Factory.Create<OpenUIConversation>(this, conversation => picked = conversation))
            .Add(component => component.CanDelete, false));

        await list.InvokeAsync(() => list.Find("button.oui-conversation-link").Click());

        Assert.Equal(saved.Id, picked?.Id);
        Assert.Empty(list.FindAll(".oui-conversation-delete"));
    }

    [Fact]
    public void Registration_defaults_to_the_chat_client_and_memory_and_the_endpoint_replaces_the_backend()
    {
        var services = new ServiceCollection();
        services.AddOpenUIComponents<Kit.ChatKit>();
        services.AddSingleton<IChatClient>(new ScriptedModel());
        services.AddSingleton(new HttpClient { BaseAddress = new Uri("http://localhost/") });
        services.AddOpenUIChat(options => options.TitleLength = 9);

        using (var provider = services.BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            Assert.IsType<OpenUIChatClientBackend>(scope.ServiceProvider.GetRequiredService<IOpenUIChatBackend>());
            Assert.IsType<OpenUIInMemoryConversationStore>(scope.ServiceProvider.GetRequiredService<IOpenUIConversationStore>());
            Assert.Equal(9, provider.GetRequiredService<OpenUIBlazorChatOptions>().TitleLength);
        }

        Assert.Contains("absolute", Assert.Throws<ArgumentException>(() => services.AddOpenUIChat().UseEndpoint("/api/chat")).Message);
        services.AddOpenUIChat().UseEndpoint("http://localhost/api/chat");

        using (var provider = services.BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            Assert.IsType<OpenUIChatEndpointBackend>(scope.ServiceProvider.GetRequiredService<IOpenUIChatBackend>());
        }
    }
}
