namespace OpenUI.Lang.Blazor.Mud;

/// <summary>A view that draws a grid of cells, out of the session's <see cref="CellAllowance"/>.</summary>
public abstract class GridView : MudView, IDisposable
{
    /// <inheritdoc />
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Context is not null && CellAllowance.Of(Context).Release(this))
        {
            // The views that were cut hear nothing else: this one leaving changed nothing in the session.
            Context.Refresh();
        }
    }

    /// <summary>How much of a grid this size to draw.</summary>
    private protected DrawnGrid FitGrid(int rows, int columns) => CellAllowance.Of(Context).Fit(this, left => DrawnGrid.Fit(rows, columns, left));

    /// <summary>How much of a grid to draw when its columns cost different numbers of cells in a row.</summary>
    private protected DrawnGrid FitGrid(int rows, IReadOnlyList<int> columnCosts) => CellAllowance.Of(Context).Fit(this, left => DrawnGrid.Fit(rows, columnCosts, left));
}
