using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using OpenUI.Lang.Blazor.Mud;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor.Tests.Mud;

/// <summary>What happens to a table's unsaved edits as the program changes around them, and to the rows a capped table does not draw.</summary>
public sealed class EditableTableTests
{
    private const string Name = "{type: \"text\", key: \"name\", header: \"Crop\"}";
    private const string Note = "{type: \"text\", key: \"note\", header: \"Note\"}";

    private static BunitContext NewContext()
    {
        var context = TestLibrary.NewContext(services => services.AddMudServices().AddOpenUIComponents(TestLibrary.Mud));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Render<MudPopoverProvider>();
        return context;
    }

    private static IRenderedComponent<OpenUIProgram> Render(BunitContext context, string program, bool streaming = false) =>
        context.Render<OpenUIProgram>(parameters => parameters
            .Add(component => component.Program, program)
            .Add(component => component.IsStreaming, streaming));

    private static void Update(IRenderedComponent<OpenUIProgram> cut, string program, bool streaming = false) =>
        cut.Render(parameters => parameters
            .Add(component => component.Program, program)
            .Add(component => component.IsStreaming, streaming));

    private static string Table(string columns, params string[] rows) => $"root = EditableTable(\"t\", [{columns}], [{string.Join(", ", rows)}])\n";

    private static string Row(string id, params string[] values) => $"{{id: \"{id}\", values: [{string.Join(", ", values.Select(value => $"\"{value}\""))}]}}";

    private static List<string> Cells(IRenderedComponent<OpenUIProgram> cut) =>
        [.. cut.FindComponents<MudTextField<string>>().Select(field => field.Find("input").GetAttribute("value") ?? "")];

    private static Task Edit(IRenderedComponent<OpenUIProgram> cut, int cell, string text) =>
        cut.InvokeAsync(() => cut.FindComponents<MudTextField<string>>()[cell].Instance.ValueChanged.InvokeAsync(text));

    [Fact]
    public async Task An_edit_survives_a_row_arriving()
    {
        await using var context = NewContext();
        var cut = Render(context, Table(Name, Row("a", "Corn")), streaming: true);
        await Edit(cut, 0, "Maize");
        cut.WaitForAssertion(() => Assert.Contains("1 changes made", cut.Markup));

        Update(cut, Table(Name, Row("a", "Corn"), Row("b", "Wheat")), streaming: true);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["Maize", "Wheat"], Cells(cut));
            Assert.Contains("1 changes made", cut.Markup);
        });
    }

    [Fact]
    public async Task An_edit_follows_its_row_when_one_arrives_ahead_of_it()
    {
        await using var context = NewContext();
        var cut = Render(context, Table(Name, Row("a", "Corn")));
        await Edit(cut, 0, "Maize");

        Update(cut, Table(Name, Row("n", "New"), Row("a", "Corn")));

        cut.WaitForAssertion(() => Assert.Equal(["New", "Maize"], Cells(cut)));
    }

    [Fact]
    public async Task An_edit_survives_a_column_arriving_and_follows_its_column()
    {
        await using var context = NewContext();
        var cut = Render(context, Table(Name, Row("a", "Corn")));
        await Edit(cut, 0, "Maize");

        Update(cut, Table(Name + ", " + Note, Row("a", "Corn", "early")));
        cut.WaitForAssertion(() => Assert.Equal(["Maize", "early"], Cells(cut)));

        Update(cut, Table(Note + ", " + Name, Row("a", "early", "Corn")));
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["early", "Maize"], Cells(cut));
            Assert.Contains("1 changes made", cut.Markup);
        });
    }

    [Fact]
    public async Task An_edit_the_data_catches_up_with_is_no_longer_a_change()
    {
        await using var context = NewContext();
        var cut = Render(context, Table(Name, Row("a", "Corn")));
        await Edit(cut, 0, "Maize");
        cut.WaitForAssertion(() => Assert.Contains("1 changes made", cut.Markup));

        Update(cut, Table(Name, Row("a", "Maize")));

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("changes made", cut.Markup);
            Assert.Equal(["Maize"], Cells(cut));
        });
    }

    [Fact]
    public async Task New_data_under_an_edit_keeps_the_edit_and_still_counts_it()
    {
        await using var context = NewContext();
        var cut = Render(context, Table(Name, Row("a", "Corn")));
        await Edit(cut, 0, "Maize");

        Update(cut, Table(Name, Row("a", "Sorghum")));

        cut.WaitForAssertion(() => Assert.Equal(["Maize"], Cells(cut)));
        Assert.Contains("1 changes made", cut.Markup);

        await cut.ClickWhere("button", "Reset");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["Sorghum"], Cells(cut));
            Assert.DoesNotContain("changes made", cut.Markup);
        });
    }

    [Fact]
    public async Task Typing_a_cell_back_to_what_it_was_is_no_change()
    {
        await using var context = NewContext();
        var cut = Render(context, Table(Name, Row("a", "Corn")));
        await Edit(cut, 0, "Maize");
        cut.WaitForAssertion(() => Assert.Contains("1 changes made", cut.Markup));

        await Edit(cut, 0, "Corn");

        cut.WaitForAssertion(() => Assert.DoesNotContain("changes made", cut.Markup));
    }

    [Fact]
    public async Task An_edit_to_a_row_that_leaves_comes_back_with_it_and_does_not_count_while_it_is_gone()
    {
        await using var context = NewContext();
        var cut = Render(context, Table(Name, Row("a", "Corn"), Row("b", "Wheat")));
        await Edit(cut, 0, "Maize");
        cut.WaitForAssertion(() => Assert.Contains("1 changes made", cut.Markup));

        Update(cut, Table(Name, Row("b", "Wheat")));
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["Wheat"], Cells(cut));
            Assert.DoesNotContain("changes made", cut.Markup);
        });

        Update(cut, Table(Name, Row("a", "Corn"), Row("b", "Wheat")));
        cut.WaitForAssertion(() => Assert.Equal(["Maize", "Wheat"], Cells(cut)));
    }

    [Fact]
    public async Task Rows_with_one_id_are_told_apart_by_position_until_the_number_of_rows_changes()
    {
        await using var context = NewContext();
        var cut = Render(context, Table(Name, Row("a", "One"), Row("a", "Two")));
        await Edit(cut, 1, "Second");

        Update(cut, Table(Name, Row("a", "1"), Row("a", "Two")));
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["1", "Second"], Cells(cut));
            Assert.Contains("1 changes made", cut.Markup);
        });

        Update(cut, Table(Name, Row("a", "New"), Row("a", "1"), Row("a", "Two")));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["New", "1", "Two"], Cells(cut));
            Assert.DoesNotContain("changes made", cut.Markup);
        });
    }

    [Fact]
    public async Task Rows_with_no_id_are_told_apart_by_position_until_the_number_of_rows_changes()
    {
        await using var context = NewContext();
        const string one = "{values: [\"One\"]}";
        const string two = "{values: [\"Two\"]}";
        var cut = Render(context, $"root = EditableTable(\"t\", [{Name}], [{one}, {two}])\n");
        await Edit(cut, 1, "Second");

        Update(cut, $"root = EditableTable(\"t\", [{Name}], [{one}, {{values: [\"2\"]}}])\n");
        cut.WaitForAssertion(() => Assert.Equal(["One", "Second"], Cells(cut)));

        Update(cut, $"root = EditableTable(\"t\", [{Name}], [{{values: [\"New\"]}}, {one}, {two}])\n");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["New", "One", "Two"], Cells(cut));
            Assert.DoesNotContain("changes made", cut.Markup);
        });
    }

    [Fact]
    public async Task A_row_count_change_keeps_the_edits_of_rows_with_an_id_of_their_own()
    {
        await using var context = NewContext();
        var cut = Render(context, Table(Name, Row("a", "One"), Row("", "Two"), Row("b", "Three")));
        await Edit(cut, 0, "First");
        await Edit(cut, 1, "Second");

        Update(cut, Table(Name, Row("c", "New"), Row("a", "One"), Row("", "Two"), Row("b", "Three")));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["New", "First", "Two", "Three"], Cells(cut));
            Assert.Contains("1 changes made", cut.Markup);
        });
    }

    [Fact]
    public async Task A_keyed_column_keeps_its_edit_through_a_new_header()
    {
        await using var context = NewContext();
        const string before = "{type: \"text\", key: \"crop\", header: \"Crop\"}";
        const string after = "{type: \"text\", key: \"crop\", header: \"Planted crop\"}";
        var cut = Render(context, Table(before, Row("a", "Corn")));
        await Edit(cut, 0, "Maize");

        Update(cut, Table(after, Row("a", "Corn")));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Planted crop", cut.Find("th").TextContent);
            Assert.Equal(["Maize"], Cells(cut));
            Assert.Contains("1 changes made", cut.Markup);
        });
    }

    [Fact]
    public async Task Columns_with_one_key_are_told_apart_by_position()
    {
        await using var context = NewContext();
        var cut = Render(context, Table(Name + ", " + Name, Row("a", "One", "Two")));

        await Edit(cut, 1, "Second");

        cut.WaitForAssertion(() => Assert.Equal(["One", "Second"], Cells(cut)));
    }

    [Fact]
    public async Task Saved_rows_coming_back_replace_the_edits_and_win_over_later_program_data()
    {
        await using var context = NewContext();
        var cut = Render(context, Table(Name, Row("a", "Corn")));
        await Edit(cut, 0, "Maize");
        await cut.ClickWhere("button", "Save Changes");
        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("changes made", cut.Markup);
            Assert.Equal(["Maize"], Cells(cut));
        });

        Update(cut, Table(Name, Row("a", "Sorghum"), Row("b", "Wheat")));

        cut.WaitForAssertion(() => Assert.Equal(["Maize"], Cells(cut)));
    }

    [Fact]
    public async Task New_saved_rows_replace_an_edit_still_open()
    {
        await using var context = NewContext();
        var program = Table(Name, Row("a", "Corn"));
        var cut = Render(context, program);
        await Edit(cut, 0, "Maize");
        await cut.ClickWhere("button", "Save Changes");
        cut.WaitForAssertion(() => Assert.DoesNotContain("changes made", cut.Markup));
        await Edit(cut, 0, "Sorghum");
        cut.WaitForAssertion(() => Assert.Contains("1 changes made", cut.Markup));

        var rows = OuiValue.FromJson(JsonDocument.Parse("""[{"id": "a", "values": ["Rye"]}]""").RootElement);
        await cut.InvokeAsync(() => cut.Instance.Session.Forms.Set("t", "t", rows, "editabletable"));
        Update(cut, program);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["Rye"], Cells(cut));
            Assert.DoesNotContain("changes made", cut.Markup);
        });
    }

    [Fact]
    public async Task An_edit_made_after_a_save_counts_against_the_saved_rows()
    {
        await using var context = NewContext();
        var cut = Render(context, Table(Name, Row("a", "Corn")));
        await Edit(cut, 0, "Maize");
        await cut.ClickWhere("button", "Save Changes");
        cut.WaitForAssertion(() => Assert.DoesNotContain("changes made", cut.Markup));

        await Edit(cut, 0, "Sorghum");
        cut.WaitForAssertion(() => Assert.Contains("1 changes made", cut.Markup));
        await Edit(cut, 0, "Maize");

        cut.WaitForAssertion(() => Assert.DoesNotContain("changes made", cut.Markup));
    }

    private static string Wide(int rows, int columns)
    {
        var column = "k = {type: \"text\", key: \"a\", header: \"A\"}\n";
        var row = "r = {id: \"1\", values: []}\n";
        return $"{column}{row}root = EditableTable(\"t\", [{string.Join(",", Enumerable.Repeat("k", columns))}], [{string.Join(",", Enumerable.Repeat("r", rows))}])\n";
    }

    [Theory]
    [InlineData(300, 300)]
    [InlineData(1000, 60)]
    [InlineData(60, 1000)]
    public async Task A_table_too_big_to_draw_draws_what_fits_and_says_so(int rows, int columns)
    {
        await using var context = NewContext();

        var cut = Render(context, Wide(rows, columns));

        Assert.Empty(cut.Instance.Session.Current.Errors);
        Assert.InRange(cut.FindAll("tbody td").Count, 1, DrawnGrid.MaxCells);
        Assert.Contains("Showing", cut.Markup);
    }

    [Fact]
    public async Task Saving_a_capped_table_keeps_the_rows_it_did_not_draw()
    {
        await using var context = NewContext();
        var rows = Enumerable.Range(0, 1000).Select(index => $"{{id: \"r{index}\", values: []}}");
        var cut = Render(context, $"root = EditableTable(\"t\", [{Name}, {Note}, {Name}], [{string.Join(",", rows)}])\n");
        Assert.Contains("Showing 833 of 1000 rows.", cut.Markup);
        Assert.Equal(833, cut.FindAll("tbody tr").Count);

        await Edit(cut, 0, "Maize");
        await cut.ClickWhere("button", "Save Changes");

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Instance.Session.Forms.Get("t", "t").TryGetArray(out var saved));
            Assert.Equal(1000, saved.Count);
            Assert.True(saved[999].TryGetObject(out var last));
            Assert.Equal("r999", last["id"].ToString());
            Assert.True(saved[0].TryGetObject(out var first));
            Assert.True(first["values"].TryGetArray(out var values));
            Assert.Equal("Maize", values[0].ToString());
            Assert.Equal(3, values.Count);
        });
    }

    [Fact]
    public async Task Saving_a_capped_table_keeps_the_values_of_the_columns_it_did_not_draw()
    {
        await using var context = NewContext();
        var columns = string.Join(",", Enumerable.Range(0, 60).Select(index => $"{{type: \"text\", key: \"c{index}\", header: \"C\"}}"));
        var hidden = string.Join(",", Enumerable.Range(0, 60).Select(index => $"\"v{index}\""));
        var rows = string.Join(",", Enumerable.Range(0, 60).Select(index => $"{{id: \"r{index}\", values: [{hidden}]}}"));
        var cut = Render(context, $"v = 1\nroot = EditableTable(\"t\", [{columns}], [{rows}])\n");
        Assert.Contains("Showing 50 of 60 rows and 50 of 60 columns.", cut.Markup);

        await Edit(cut, 0, "Edited");
        await cut.ClickWhere("button", "Save Changes");

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Instance.Session.Forms.Get("t", "t").TryGetArray(out var saved));
            Assert.True(saved[0].TryGetObject(out var row));
            Assert.True(row["values"].TryGetArray(out var values));
            Assert.Equal("Edited", values[0].ToString());
            Assert.Equal("v59", values[59].ToString());
        });
    }

    [Fact]
    public async Task A_date_cell_the_picker_echoes_is_no_change()
    {
        await using var context = NewContext();
        var cut = Render(context, "root = EditableTable(\"t\", [{type: \"date-single\", key: \"d\", header: \"Day\"}], [{id: \"a\", values: [\"2026-03-15T19:30:00Z\"]}])\n");
        var picker = cut.FindComponent<MudDatePicker>();

        await cut.InvokeAsync(() => picker.Instance.DateChanged.InvokeAsync(picker.Instance.Date));

        Assert.DoesNotContain("changes made", cut.Markup);
    }
}
