namespace OpenUI.Lang.Blazor.Mud;

/// <summary>
/// How much of a table or chart a view draws. A program's size limit counts rows plus columns, since a reference to
/// one list costs one list, but drawing costs rows times columns, so a few lines of program could otherwise run a
/// server out of memory. A view draws the first <see cref="Rows"/> by the first <see cref="Columns"/>, and shows
/// <see cref="Note"/> when <see cref="IsCut"/>.
/// </summary>
/// <param name="Rows">The rows to draw.</param>
/// <param name="Columns">The columns to draw.</param>
/// <param name="TotalRows">The rows the program has.</param>
/// <param name="TotalColumns">The columns the program has.</param>
/// <param name="Cells">What it costs the session's <see cref="CellAllowance"/>: the rows drawn times what one row costs.</param>
internal readonly record struct DrawnGrid(int Rows, int Columns, int TotalRows, int TotalColumns, int Cells)
{
    /// <summary>
    /// The most cells a view draws. An EditableTable costs the most, a live input per cell: 2500 cells hold about 90 MB
    /// and draw in about 0.3 s, and 10000 hold 360 MB. Table and HorizontalBarChart hold under 15 MB at this size, and
    /// it still leaves 250 rows of ten columns.
    /// </summary>
    public const int MaxCells = 2500;

    /// <summary>True when part of the grid is not drawn.</summary>
    public bool IsCut => Rows < TotalRows || Columns < TotalColumns;

    /// <summary>What fits of a grid this size. A grid too big keeps all of its short side when it can, and otherwise a square.</summary>
    public static DrawnGrid Fit(int rows, int columns) => Fit(rows, columns, MaxCells);

    /// <summary>What fits of a grid this size in fewer cells. With none left, nothing is drawn.</summary>
    public static DrawnGrid Fit(int rows, int columns, int maxCells)
    {
        if ((long)rows * columns <= maxCells)
        {
            return new DrawnGrid(rows, columns, rows, columns, rows * columns);
        }

        if (maxCells == 0)
        {
            return new DrawnGrid(0, 0, rows, columns, 0);
        }

        var drawnColumns = Math.Min(columns, Math.Max((int)Math.Sqrt(maxCells), maxCells / rows));
        var drawnRows = Math.Min(rows, maxCells / drawnColumns);
        return new DrawnGrid(drawnRows, drawnColumns, rows, columns, drawnRows * drawnColumns);
    }

    /// <summary>
    /// What fits of a grid whose columns cost a different number of cells in each row, such as a select that draws its options.
    /// It keeps the first columns that fit a row's share, as <see cref="Fit(int, int, int)"/> does, and at least one when a
    /// row of it fits at all.
    /// </summary>
    /// <param name="rows">The rows the program has.</param>
    /// <param name="columnCosts">What each column costs in a row, one cell at least.</param>
    /// <param name="maxCells">The most cells to use.</param>
    public static DrawnGrid Fit(int rows, IReadOnlyList<int> columnCosts, int maxCells)
    {
        ArgumentNullException.ThrowIfNull(columnCosts);
        if (columnCosts.All(cost => cost == 1))
        {
            return Fit(rows, columnCosts.Count, maxCells);
        }

        var rowCost = columnCosts.Sum(cost => (long)cost);
        if (rows * rowCost <= maxCells)
        {
            return new DrawnGrid(rows, columnCosts.Count, rows, columnCosts.Count, (int)(rows * rowCost));
        }

        if (columnCosts[0] > maxCells)
        {
            return new DrawnGrid(0, 0, rows, columnCosts.Count, 0);
        }

        var share = Math.Max((int)Math.Sqrt(maxCells), maxCells / rows);
        var drawnColumns = 1;
        var drawnCost = columnCosts[0];
        while (drawnColumns < columnCosts.Count && drawnCost + columnCosts[drawnColumns] <= share)
        {
            drawnCost += columnCosts[drawnColumns++];
        }

        var drawnRows = Math.Min(rows, maxCells / drawnCost);
        return new DrawnGrid(drawnRows, drawnColumns, rows, columnCosts.Count, drawnRows * drawnCost);
    }

    /// <summary>Says what is not drawn, such as <c>Showing 50 of 900 rows.</c></summary>
    /// <param name="rows">What to call the rows.</param>
    /// <param name="columns">What to call the columns.</param>
    public string Note(string rows = "rows", string columns = "columns") => (Rows < TotalRows, Columns < TotalColumns) switch
    {
        (true, true) => $"Showing {Rows} of {TotalRows} {rows} and {Columns} of {TotalColumns} {columns}.",
        (true, false) => $"Showing {Rows} of {TotalRows} {rows}.",
        (false, true) => $"Showing {Columns} of {TotalColumns} {columns}.",
        _ => "",
    };
}
