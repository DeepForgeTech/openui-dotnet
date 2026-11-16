using System.Runtime.CompilerServices;

namespace OpenUI.Lang.Blazor.Mud;

/// <summary>
/// The cells a session's tables and charts may draw between them. <see cref="DrawnGrid"/> limits one view, and a list of
/// a few characters can repeat that view many times, each repeat costing a program one reference and the server thousands
/// of live cells. Each view holds what it drew, and fitting it again replaces what it holds, so drawing again costs nothing.
/// Views that come when the allowance is gone draw what is left, and their notes say so.
/// </summary>
internal sealed class CellAllowance
{
    /// <summary>
    /// The cells of a session. The dearest cell is a date picker: <see cref="Total"/> of them hold about 390 MB and take about
    /// 5 s to draw in a Release build, against 255 MB for a select with no options, 141 for a number and 134 for a text or
    /// url. A select with eight options comes to about the same as a date, which is why a select is charged a cell for every
    /// eight (see <see cref="EditableColumn.OptionsPerCell"/>). Five tables of 50 rows and ten columns still draw in full.
    /// </summary>
    public const int Total = 4000;

    private static readonly ConditionalWeakTable<OpenUIContext, CellAllowance> Sessions = [];

    private readonly Dictionary<object, Held> _grids = [];
    private int _used;

    /// <summary>The allowance of a view's session.</summary>
    public static CellAllowance Of(OpenUIContext context) => Sessions.GetOrCreateValue(context);

    /// <summary>What the view may draw of a grid, in place of what it held.</summary>
    /// <param name="view">The view.</param>
    /// <param name="fit">What fits of the grid in this many cells.</param>
    public DrawnGrid Fit(object view, Func<int, DrawnGrid> fit)
    {
        Return(view);
        var grid = fit(Math.Min(DrawnGrid.MaxCells, Total - _used));
        _grids[view] = new Held(grid, IsWithinOneView: grid == fit(DrawnGrid.MaxCells));
        _used += grid.Cells;
        return grid;
    }

    /// <summary>Gives back what the view held. True when another view drew less than it wanted and can now have more.</summary>
    public bool Release(object view)
    {
        Return(view);
        return _grids.Values.Any(held => held.Grid.IsCut && !held.IsWithinOneView);
    }

    private void Return(object view)
    {
        if (_grids.Remove(view, out var held))
        {
            _used -= held.Grid.Cells;
        }
    }

    // IsWithinOneView: the view drew all that one view may draw of its grid, whatever else is on the page.
    private readonly record struct Held(DrawnGrid Grid, bool IsWithinOneView);
}
