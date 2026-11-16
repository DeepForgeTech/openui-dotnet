using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using OpenUI.Lang.Blazor.Mud;

namespace OpenUI.Lang.Blazor.Tests.Mud;

/// <summary>The cells a session's tables and charts share, however many times a program repeats one.</summary>
public sealed class CellAllowanceTests
{
    private static BunitContext NewContext()
    {
        var context = TestLibrary.NewContext(services => services.AddMudServices().AddOpenUIComponents(TestLibrary.Mud));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Render<MudPopoverProvider>();
        return context;
    }

    private static IRenderedComponent<OpenUIProgram> Render(BunitContext context, string program) =>
        context.Render<OpenUIProgram>(parameters => parameters.Add(component => component.Program, program));

    private static string Repeat(string text, int times) => string.Join(", ", Enumerable.Repeat(text, times));

    // One long list and the rest empty: a program's size limit counts rows plus columns, and drawing costs rows times columns.
    private static string Table(string name, int rows, int columns) =>
        $"{name}Long = [{Repeat("1", rows)}]\n{name}Empty = Col(\"C\", [])\n{name} = Table([Col(\"C\", {name}Long), {Repeat($"{name}Empty", columns - 1)}])\n";

    private static string Editable(string name, int rows, int columns) =>
        $"{name}Col = {{}}\n{name}Row = {{}}\n{name} = EditableTable(\"{name}\", [{Repeat($"{name}Col", columns)}], [{Repeat($"{name}Row", rows)}])\n";

    // A select draws every option in every row, so the options are what a tall table costs.
    private static string SelectTable(string name, int rows, int options, string otherColumns = "") =>
        $"{name}Option = {{value: \"v\", label: \"l\"}}\n{name}Column = {{type: \"select\", header: \"h\", options: [{Repeat($"{name}Option", options)}]}}\n{name}Row = {{}}\n" +
        $"{name} = EditableTable(\"{name}\", [{name}Column{otherColumns}], [{Repeat($"{name}Row", rows)}])\n";

    private static string Bars(string name, int labels, int series) =>
        $"{name}Series = Series(\"S\", [])\n{name} = HorizontalBarChart([{Repeat("\"L\"", labels)}], [{Repeat($"{name}Series", series)}])\n";

    private static string Stack(params string[] names) => $"root = Stack([{string.Join(", ", names)}])\n";

    [Fact]
    public async Task Five_tables_of_50_by_10_draw_in_full()
    {
        await using var context = NewContext();
        var names = Enumerable.Range(0, 5).Select(index => $"t{index}").ToArray();

        var cut = Render(context, string.Concat(names.Select(name => Table(name, 50, 10))) + Stack(names));

        Assert.Equal(2500, cut.FindAll("tbody td").Count);
        Assert.DoesNotContain("Showing", cut.Markup);
    }

    [Fact]
    public async Task Five_editable_tables_of_50_by_10_draw_in_full()
    {
        await using var context = NewContext();
        var names = Enumerable.Range(0, 5).Select(index => $"t{index}").ToArray();

        var cut = Render(context, string.Concat(names.Select(name => Editable(name, 50, 10))) + Stack(names));

        Assert.Equal(2500, cut.FindAll("tbody td").Count);
        Assert.DoesNotContain("Showing", cut.Markup);
    }

    [Fact]
    public async Task One_table_repeated_forty_times_stops_at_the_allowance_and_says_so()
    {
        await using var context = NewContext();

        var cut = Render(context, Editable("t", 50, 50) + Stack([.. Enumerable.Repeat("t", 40)]));

        Assert.InRange(cut.FindAll("tbody td").Count, 3900, CellAllowance.Total);
        Assert.Contains("Showing 0 of 50 rows and 0 of 50 columns.", cut.Markup);
        Assert.DoesNotContain("No rows.", cut.Markup);
    }

    [Fact]
    public async Task Tables_and_charts_share_it()
    {
        await using var context = NewContext();

        var cut = Render(context, Table("t", 50, 50) + Bars("b", 50, 50) + Editable("e", 50, 50) + Stack("t", "b", "e"));

        Assert.Equal(2500, cut.FindAll("table").First().QuerySelectorAll("tbody td").Length);
        Assert.Contains("Showing 39 of 50 labels and 38 of 50 series.", cut.Markup);
        Assert.Contains("Showing 4 of 50 rows and 4 of 50 columns.", cut.Markup);
    }

    [Fact]
    public async Task A_chart_with_nothing_left_says_so_and_not_that_it_has_no_data()
    {
        await using var context = NewContext();

        var cut = Render(context, Table("t", 50, 50) + Table("u", 50, 30) + Bars("b", 5, 5) + Stack("t", "u", "b"));

        Assert.Contains("Showing 0 of 5 labels and 0 of 5 series.", cut.Markup);
        Assert.DoesNotContain("No data.", cut.Markup);
    }

    [Fact]
    public async Task Drawing_again_does_not_use_it_up()
    {
        await using var context = NewContext();
        var program = Table("t", 50, 50) + Editable("e", 30, 50);
        var cut = Render(context, program + Stack("t", "e"));

        for (var pass = 0; pass < 10; pass++)
        {
            cut.Render(parameters => parameters.Add(component => component.Program, $"{program}unused{pass} = {pass}\n{Stack("t", "e")}"));
        }

        Assert.Equal(4000, cut.FindAll("tbody td").Count);
        Assert.DoesNotContain("Showing", cut.Markup);
    }

    [Fact]
    public async Task A_table_leaving_gives_its_cells_to_one_that_was_cut()
    {
        await using var context = NewContext();
        var tables = Table("t", 50, 50) + Table("u", 50, 50);
        var cut = Render(context, tables + Stack("t", "u"));
        Assert.InRange(cut.FindAll("tbody td").Count, 3900, CellAllowance.Total);

        cut.Render(parameters => parameters.Add(component => component.Program, tables + Stack("u")));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2500, cut.FindAll("tbody td").Count);
            Assert.DoesNotContain("Showing", cut.Markup);
        });
    }

    [Fact]
    public async Task A_select_column_with_a_thousand_options_is_charged_for_them_and_the_table_says_what_it_left_out()
    {
        await using var context = NewContext();

        var cut = Render(context, SelectTable("t", 300, 1000) + Stack("t"));

        Assert.Contains("Showing 20 of 300 rows.", cut.Markup);
        Assert.Equal(20 * 1000, cut.FindComponents<MudSelectItem<string>>().Count);
    }

    [Fact]
    public async Task A_select_table_repeated_stays_within_the_allowance()
    {
        await using var context = NewContext();

        // 300 options cost 38 cells a row, so the 4000 cells of a session are 105 rows of them: 65, 39 and 1.
        var cut = Render(context, SelectTable("t", 100, 300) + Stack([.. Enumerable.Repeat("t", 4)]));

        Assert.Equal(105 * 300, cut.FindComponents<MudSelectItem<string>>().Count);
        Assert.Contains("Showing 65 of 100 rows.", cut.Markup);
        Assert.Contains("Showing 0 of 100 rows and 0 of 1 columns.", cut.Markup);
    }

    [Theory]
    [InlineData(250, 8)]
    [InlineData(50, 40)]
    public async Task A_select_with_an_ordinary_list_of_options_draws_in_full(int rows, int options)
    {
        await using var context = NewContext();

        var cut = Render(context, SelectTable("t", rows, options, ", {type: \"text\", header: \"name\"}") + Stack("t"));

        Assert.Equal(rows, cut.FindAll("tbody tr").Count);
        Assert.Equal(rows * options, cut.FindComponents<MudSelectItem<string>>().Count);
        Assert.DoesNotContain("Showing", cut.Markup);
    }

    [Fact]
    public async Task Each_session_has_its_own()
    {
        await using var context = NewContext();
        var program = Table("t", 50, 50) + Table("u", 30, 50) + Stack("t", "u");

        var first = Render(context, program);
        var second = Render(context, program);

        Assert.Equal(4000, first.FindAll("tbody td").Count);
        Assert.Equal(4000, second.FindAll("tbody td").Count);
    }
}
