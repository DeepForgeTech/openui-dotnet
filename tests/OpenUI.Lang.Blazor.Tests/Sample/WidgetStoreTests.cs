using Microsoft.Extensions.Logging.Abstractions;
using OpenUI.Lang.Samples.BlazorApp.Widgets;

namespace OpenUI.Lang.Blazor.Tests.Sample;

public sealed class WidgetStoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("openui-widgets-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private WidgetStore NewStore() => new(_directory, NullLogger<WidgetStore>.Instance);

    private static Widget Sample(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Program = "root = Card([TextContent(\"Wind\")])",
        Requests = ["Wind for North field", "Make it bigger"],
    };

    [Fact]
    public void A_widget_comes_back_whole_from_a_new_store()
    {
        var saved = Sample("Wind");
        NewStore().Save(saved);

        var found = NewStore().Find(saved.Id);

        Assert.NotNull(found);
        Assert.Equal("Wind", found.Name);
        Assert.Equal(saved.Program, found.Program);
        Assert.Equal(["Wind for North field", "Make it bigger"], found.Requests);
    }

    [Fact]
    public void Saving_again_replaces_the_widget_rather_than_adding_one()
    {
        var store = NewStore();
        var widget = Sample("Wind");
        store.Save(widget);

        widget.Name = "Wind this week";
        widget.Program = "root = Card([TextContent(\"Changed\")])";
        store.Save(widget);

        var only = Assert.Single(store.List());
        Assert.Equal("Wind this week", only.Name);
        Assert.Contains("Changed", only.Program);
    }

    [Fact]
    public void The_list_puts_the_latest_save_first_and_skips_a_file_it_cannot_read()
    {
        var store = NewStore();
        var first = Sample("First");
        var second = Sample("Second");
        store.Save(first);
        store.Save(second);
        File.WriteAllText(Path.Combine(_directory, "broken.json"), "{ not json");

        Assert.Equal(["Second", "First"], store.List().Select(widget => widget.Name));
    }

    [Fact]
    public void Delete_removes_the_file_and_says_the_list_changed()
    {
        var store = NewStore();
        var widget = Sample("Wind");
        store.Save(widget);
        var changes = 0;
        store.Changed += () => changes++;

        store.Delete(widget.Id);

        Assert.Null(store.Find(widget.Id));
        Assert.Empty(Directory.EnumerateFiles(_directory));
        Assert.Equal(1, changes);
    }
}
