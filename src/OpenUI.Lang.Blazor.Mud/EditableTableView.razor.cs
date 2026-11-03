using System.Collections.Immutable;
using OpenUI.Lang.Values;

namespace OpenUI.Lang.Blazor.Mud;

/// <summary>One column of an editable table.</summary>
/// <param name="Index">Its place in each row's values: <c>row.values[i]</c> belongs to <c>columns[i]</c>.</param>
/// <param name="Type"><c>text</c>, <c>number</c>, <c>date-single</c>, <c>select</c> or <c>url</c>.</param>
/// <param name="Header">Its heading.</param>
/// <param name="Width">Its least width in pixels, or null.</param>
/// <param name="Options">What a <c>select</c> offers.</param>
internal sealed record EditableColumn(int Index, string Type, string Header, double? Width, IReadOnlyList<ColumnOption> Options)
{
    /// <summary>
    /// The options a <c>select</c> draws for the price of its one cell. A select draws every option in every row, so a long
    /// list in a tall table would cost far more than the cells it is counted as.
    /// </summary>
    public const int OptionsPerCell = 8;

    internal OccurrenceKey Key { get; init; }

    /// <summary>What a cell of this column costs the <see cref="CellAllowance"/>: one, and a <c>select</c> with many options more.</summary>
    internal int Cost => Type == "select" ? Math.Max(1, (Options.Count + OptionsPerCell - 1) / OptionsPerCell) : 1;
}

/// <summary>One option of a <c>select</c> column.</summary>
/// <param name="Value">What the cell holds.</param>
/// <param name="Label">What to show.</param>
internal readonly record struct ColumnOption(string Value, string Label);

/// <summary>One row of an editable table. Read and write its cells with the table.</summary>
internal sealed class EditableRow
{
    internal EditableRow(OccurrenceKey key, string id, OuiArray values)
    {
        Key = key;
        Id = id;
        Values = values;
    }

    /// <summary>The id the program gave it; empty for none.</summary>
    public string Id { get; }

    internal OccurrenceKey Key { get; }

    internal OuiArray Values { get; }
}

/// <summary>
/// Edits stay here until Save Changes, as upstream's do. Saving puts every row into a form named for the table, under the
/// table's name, and sends that form to the assistant. An edit belongs to a row's id and a column's key, or its header, so
/// it survives rows and columns arriving around it. A repeated or missing id or key is told apart by position, so when the
/// number of rows changes the edits to rows with no id of their own are dropped: they could land on another row. Saved rows
/// coming back replace the edits. The rows it does not draw are saved all the same.
/// </summary>
public partial class EditableTableView
{
    private readonly Dictionary<(OccurrenceKey Row, OccurrenceKey Column), OuiValue> _edits = [];
    private List<EditableColumn> _columns = [];
    private List<EditableRow> _rows = [];
    private Dictionary<OccurrenceKey, EditableColumn> _columnsByKey = [];
    private Dictionary<OccurrenceKey, EditableRow> _rowsByKey = [];
    private int[] _columnCosts = [];
    private (OuiValue Data, OuiValue Columns) _source;
    private OuiValue _saved;

    /// <summary>The columns to draw.</summary>
    private IReadOnlyList<EditableColumn> Columns { get; set; } = [];

    /// <summary>The rows to draw.</summary>
    private IReadOnlyList<EditableRow> Rows { get; set; } = [];

    /// <summary>How much of the table is drawn, and the note to show when some is not.</summary>
    private DrawnGrid Grid { get; set; }

    /// <summary>How many cells differ from what the program or the last save says.</summary>
    private int Changed => _edits.Count(edit =>
        _rowsByKey.TryGetValue(edit.Key.Row, out var row)
        && _columnsByKey.TryGetValue(edit.Key.Column, out var column)
        && !edit.Value.Equals(Original(row, column)));

    /// <summary>What the cell holds now, with the user's edit.</summary>
    private OuiValue Cell(EditableRow row, EditableColumn column)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(column);
        return _edits.TryGetValue((row.Key, column.Key), out var edit) ? edit : Original(row, column);
    }

    /// <summary>The user edited the cell.</summary>
    private void SetCell(EditableRow row, EditableColumn column, OuiValue value)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(column);
        if (value.Equals(Original(row, column)))
        {
            _edits.Remove((row.Key, column.Key));
        }
        else
        {
            _edits[(row.Key, column.Key)] = value;
        }
    }

    /// <summary>The user picked a day for a <c>date-single</c> cell. A picker that echoes the day it was given changes nothing, as with <see cref="MudView.SetDay"/>.</summary>
    private void SetCellDay(EditableRow row, EditableColumn column, DateTime? day)
    {
        if (day != DateValues.Read(Cell(row, column)))
        {
            SetCell(row, column, DateValues.Write(day));
        }
    }

    /// <summary>Drops the user's edits.</summary>
    private void Reset() => _edits.Clear();

    /// <summary>Puts every row into the table's form and sends that form to the assistant.</summary>
    private async Task SaveAsync()
    {
        var edited = _edits.Keys.Select(edit => edit.Row).ToHashSet();
        var rows = _rows.Select(row => (OuiValue)OuiObject.Create(
        [
            new("id", row.Id),
            new("values", !edited.Contains(row.Key) && row.Values.Count == _columns.Count ? row.Values : OuiArray.Create(_columns.Select(column => Cell(row, column)).ToImmutableArray())),
        ]));
        var name = Text("name");
        Context.Session.Forms.Set(name, name, OuiArray.Create(rows.ToImmutableArray()), Node.Spec?.Traits.FieldComponentType ?? "input");
        await Context.RunActionAsync(Node, OuiAction.ToAssistant("Save Changes"), name);
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        ReadSource();

        // Every time, and not only when the data changes: what the page has left can change with nothing here changing.
        Grid = FitGrid(_rows.Count, _columnCosts);
        Columns = _columns[..Grid.Columns];
        Rows = _rows[..Grid.Rows];
    }

    private void ReadSource()
    {
        var saved = Context.Session.Forms.Get(Text("name"), Text("name"));
        var source = (Data: saved.IsNull ? Value("data") : saved, Columns: Value("columns"));
        if (source.Equals(_source))
        {
            return;
        }

        if (!saved.IsNull && !saved.Equals(_saved))
        {
            _edits.Clear();
        }

        _saved = saved;
        _source = source;
        _columns = ReadColumns(source.Columns);
        _columnCosts = [.. _columns.Select(column => column.Cost)];
        var rows = ReadRows(source.Data);
        if (rows.Count != _rows.Count)
        {
            DropEditsOfUnidentifiedRows(_rows, rows);
        }

        _rows = rows;
        _columnsByKey = _columns.ToDictionary(column => column.Key);
        _rowsByKey = _rows.ToDictionary(row => row.Key);
    }

    // A missing or repeated id tells a row only by its place, and a row arriving or leaving moves the places.
    private void DropEditsOfUnidentifiedRows(List<EditableRow> before, List<EditableRow> after)
    {
        var unidentified = before.Concat(after).Where(row => row.Id.Length == 0 || row.Key.Occurrence > 0).Select(row => row.Id).ToHashSet();
        foreach (var edit in _edits.Keys.Where(edit => unidentified.Contains(edit.Row.Name)).ToList())
        {
            _edits.Remove(edit);
        }
    }

    private static OuiValue Original(EditableRow row, EditableColumn column) =>
        column.Index < row.Values.Count ? row.Values[column.Index] : OuiValue.Null;

    private static List<EditableRow> ReadRows(OuiValue data)
    {
        var rows = (data.TryGetArray(out var list) ? list : OuiArray.Empty).Select(row => row.TryGetObject(out var fields) ? fields : OuiObject.Empty).ToList();
        var keys = OccurrenceKey.Of(rows.Select(row => row.Text("id")));
        return [.. rows.Select((row, index) => new EditableRow(keys[index], row.Text("id"), row["values"].TryGetArray(out var values) ? values : OuiArray.Empty))];
    }

    private static List<EditableColumn> ReadColumns(OuiValue columns)
    {
        var fields = (columns.TryGetArray(out var list) ? list : OuiArray.Empty).Select(column => column.TryGetObject(out var entries) ? entries : OuiObject.Empty).ToList();
        var keys = OccurrenceKey.Of(fields.Select(column => column.Text("key") is { Length: > 0 } key ? key : column.Text("header")));
        return [.. fields.Select((column, index) => new EditableColumn(
            index,
            column.Text("type"),
            column.Text("header"),
            RuntimeValues.AsNumber(column["width"]) is > 0 and var width && double.IsFinite(width) ? width : null,
            [.. (column["options"].TryGetArray(out var options) ? options : OuiArray.Empty)
                .Select(option => option.TryGetObject(out var entry) ? entry : OuiObject.Empty)
                .Select(entry => new ColumnOption(entry.Text("value"), entry.Text("label")))])
        {
            Key = keys[index],
        })];
    }
}
