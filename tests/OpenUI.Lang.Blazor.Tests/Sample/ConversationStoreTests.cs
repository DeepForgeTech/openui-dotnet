using Microsoft.Extensions.Logging.Abstractions;
using OpenUI.Lang.Samples.BlazorApp.Chat;

namespace OpenUI.Lang.Blazor.Tests.Sample;

public sealed class ConversationStoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("openui-store-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private ConversationStore NewStore() => new(_directory, NullLogger<ConversationStore>.Instance);

    private static Conversation Sample(string title) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Messages =
        [
            new ChatEntry { Role = "user", Text = "Clicked", Context = """["User clicked: Clicked"]""" },
            new ChatEntry { Role = "assistant", Text = "root = Card(\"x\", [])", Errors = "[]" },
        ],
    };

    [Fact]
    public void A_conversation_comes_back_whole_from_a_new_store()
    {
        var saved = Sample("Spray");
        NewStore().Save(saved);

        var found = NewStore().Find(saved.Id);

        Assert.NotNull(found);
        Assert.Equal("Spray", found.Title);
        Assert.Equal(2, found.Messages.Count);
        Assert.Equal("""["User clicked: Clicked"]""", found.Messages[0].Context);
        Assert.Equal("root = Card(\"x\", [])", found.Messages[1].Text);
        Assert.Equal("[]", found.Messages[1].Errors);
    }

    [Fact]
    public void The_list_has_the_most_recently_saved_first()
    {
        var store = NewStore();
        var first = Sample("First");
        var second = Sample("Second");
        store.Save(first);
        store.Save(second);
        store.Save(first);

        Assert.Equal(["First", "Second"], store.List().Select(conversation => conversation.Title));
    }

    [Fact]
    public void A_deleted_conversation_is_gone()
    {
        var store = NewStore();
        var saved = Sample("Gone");
        store.Save(saved);

        store.Delete(saved.Id);

        Assert.Null(store.Find(saved.Id));
        Assert.Empty(store.List());
    }

    [Fact]
    public void A_corrupt_file_costs_only_itself()
    {
        var store = NewStore();
        store.Save(Sample("Fine"));
        File.WriteAllText(Path.Combine(_directory, $"{Guid.NewGuid():N}.json"), "{ not json");

        Assert.Equal("Fine", Assert.Single(store.List()).Title);
    }

    [Fact]
    public void Saving_tells_whoever_is_listening()
    {
        var store = NewStore();
        var heard = 0;
        store.Changed += () => heard++;

        store.Save(Sample("Heard"));

        Assert.Equal(1, heard);
    }
}
