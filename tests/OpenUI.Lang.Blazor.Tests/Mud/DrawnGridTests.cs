using OpenUI.Lang.Blazor.Mud;

namespace OpenUI.Lang.Blazor.Tests.Mud;

public sealed class DrawnGridTests
{
    [Theory]
    [InlineData(50, 10, 0, 0, 0)]
    [InlineData(0, 10, 0, 0, 10)]
    [InlineData(50, 10, 3, 3, 1)]
    [InlineData(50, 50, 1500, 39, 38)]
    [InlineData(1000, 10, 1000, 100, 10)]
    public void With_fewer_cells_left_a_grid_keeps_within_them(int rows, int columns, int maxCells, int drawnRows, int drawnColumns)
    {
        var grid = DrawnGrid.Fit(rows, columns, maxCells);

        Assert.Equal((drawnRows, drawnColumns), (grid.Rows, grid.Columns));
        Assert.InRange(grid.Rows * grid.Columns, 0, maxCells);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 100000)]
    [InlineData(100000, 0)]
    [InlineData(50, 50)]
    [InlineData(2500, 1)]
    [InlineData(1, 2500)]
    public void A_grid_that_fits_is_drawn_whole(int rows, int columns)
    {
        var grid = DrawnGrid.Fit(rows, columns);

        Assert.Equal((rows, columns), (grid.Rows, grid.Columns));
        Assert.False(grid.IsCut);
        Assert.Equal("", grid.Note());
    }

    [Theory]
    [InlineData(51, 50, 50, 50)]
    [InlineData(1000, 10, 250, 10)]
    [InlineData(100000, 3, 833, 3)]
    [InlineData(1000, 1000, 50, 50)]
    [InlineData(3, 2000, 3, 833)]
    [InlineData(2, 100000, 2, 1250)]
    [InlineData(int.MaxValue, int.MaxValue, 50, 50)]
    public void A_grid_too_big_keeps_within_the_limit(int rows, int columns, int drawnRows, int drawnColumns)
    {
        var grid = DrawnGrid.Fit(rows, columns);

        Assert.Equal((drawnRows, drawnColumns), (grid.Rows, grid.Columns));
        Assert.True(grid.IsCut);
        Assert.InRange(grid.Rows * grid.Columns, 1, DrawnGrid.MaxCells);
    }

    [Theory]
    [InlineData(300, new[] { 125 }, 2500, 20, 1)]
    [InlineData(1000, new[] { 1, 125 }, 2500, 1000, 1)]
    [InlineData(10, new[] { 1, 125 }, 2500, 10, 2)]
    [InlineData(300, new[] { 3000 }, 2500, 0, 0)]
    [InlineData(300, new[] { 125 }, 100, 0, 0)]
    [InlineData(0, new[] { 125 }, 2500, 0, 1)]
    public void Columns_that_cost_more_than_a_cell_take_a_row_s_share(int rows, int[] costs, int maxCells, int drawnRows, int drawnColumns)
    {
        var grid = DrawnGrid.Fit(rows, costs, maxCells);

        Assert.Equal((drawnRows, drawnColumns), (grid.Rows, grid.Columns));
        Assert.Equal(grid.Rows * costs.Take(grid.Columns).Sum(), grid.Cells);
        Assert.InRange(grid.Cells, 0, maxCells);
    }

    [Theory]
    [InlineData(50, 10, 0)]
    [InlineData(50, 50, 1500)]
    [InlineData(1000, 10, 1000)]
    [InlineData(100000, 3, 2500)]
    [InlineData(1000, 1000, 2500)]
    public void Columns_that_cost_a_cell_each_fit_as_they_always_did(int rows, int columns, int maxCells) =>
        Assert.Equal(DrawnGrid.Fit(rows, columns, maxCells), DrawnGrid.Fit(rows, [.. Enumerable.Repeat(1, columns)], maxCells));

    [Theory]
    [InlineData(3000, 1, "Showing 2500 of 3000 rows.")]
    [InlineData(1, 3000, "Showing 2500 of 3000 columns.")]
    [InlineData(100, 100, "Showing 50 of 100 rows and 50 of 100 columns.")]
    public void The_note_says_what_is_not_drawn(int rows, int columns, string note) =>
        Assert.Equal(note, DrawnGrid.Fit(rows, columns).Note());

    [Fact]
    public void The_note_takes_the_view_s_own_words() =>
        Assert.Equal("Showing 50 of 100 labels and 50 of 100 series.", DrawnGrid.Fit(100, 100).Note("labels", "series"));
}
