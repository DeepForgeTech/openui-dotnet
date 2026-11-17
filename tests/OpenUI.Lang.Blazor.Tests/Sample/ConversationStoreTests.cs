using Microsoft.Extensions.Logging.Abstractions;
using OpenUI.Lang.Blazor.Chat;
using OpenUI.Lang.Samples.BlazorApp.Chat;

namespace OpenUI.Lang.Blazor.Tests.Sample;

public sealed class ConversationStoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("openui-store-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private ConversationStore NewStore() => new(_directory, NullLogger<ConversationStore>.Instance);

    private static OpenUIConversation Sample(string title) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Messages =
        [
            new OpenUIChatEntry { Role = OpenUIChatRoles.User, Text = "Clicked", Context = """["User clicked: Clicked"]""" },
            new OpenUIChatEntry { Role = OpenUIChatRoles.Assistant, Text = "root = Card(\"x\", [])", Errors = "[]" },
        ],
    };

    [Fact]
    public async Task A_conversation_comes_back_whole_from_a_new_store()
    {
        var saved = Sample("Spray");
        await NewStore().SaveAsync(saved);

        var found = await NewStore().FindAsync(saved.Id);

        Assert.NotNull(found);
        Assert.Equal("Spray", found.Title);
        Assert.Equal(2, found.Messages.Count);
        Assert.Equal("""["User clicked: Clicked"]""", found.Messages[0].Context);
        Assert.Equal("root = Card(\"x\", [])", found.Messages[1].Text);
        Assert.Equal("[]", found.Messages[1].Errors);
    }

    [Fact]
    public async Task A_file_written_before_the_chat_package_still_reads()
    {
        var id = Guid.NewGuid();
        File.WriteAllText(Path.Combine(_directory, $"{id:N}.json"), $$"""
            {
              "id": "{{id}}",
              "title": "Old",
              "updatedAt": "2026-10-01T09:00:00+00:00",
              "messages": [
                { "role": "user", "text": "Before", "context": null, "errors": null },
                { "role": "assistant", "text": "root = Card([])", "context": null, "errors": "[]" }
              ]
            }
            """);

        var found = await NewStore().FindAsync(id);

        Assert.Equal("Old", found?.Title);
        Assert.Equal(["Before", "root = Card([])"], found!.Messages.Select(entry => entry.Text));
    }

    [Fact]
    public async Task The_list_has_the_most_recently_saved_first()
    {
        var store = NewStore();
        var first = Sample("First");
        var second = Sample("Second");
        await store.SaveAsync(first);
        await store.SaveAsync(second);
        await store.SaveAsync(first);

        Assert.Equal(["First", "Second"], (await store.ListAsync()).Select(conversation => conversation.Title));
    }

    [Fact]
    public async Task A_deleted_conversation_is_gone()
    {
        var store = NewStore();
        var saved = Sample("Gone");
        await store.SaveAsync(saved);

        await store.DeleteAsync(saved.Id);

        Assert.Null(await store.FindAsync(saved.Id));
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task A_corrupt_file_costs_only_itself()
    {
        var store = NewStore();
        await store.SaveAsync(Sample("Fine"));
        File.WriteAllText(Path.Combine(_directory, $"{Guid.NewGuid():N}.json"), "{ not json");

        Assert.Equal("Fine", Assert.Single(await store.ListAsync()).Title);
    }

    [Fact]
    public async Task Saving_tells_whoever_is_listening()
    {
        var store = NewStore();
        var heard = 0;
        store.Changed += () => heard++;

        await store.SaveAsync(Sample("Heard"));

        Assert.Equal(1, heard);
    }
}
