using System.Collections;
using System.ComponentModel;
using System.Globalization;

namespace AOTableEditor.Models;

public sealed class RenderedTable
{
    public const string RowKindColumnName = "__RowKind";
    private const string ChangedColumnPrefix = "__Changed_";

    private readonly int[] _rowSetStrides;
    private readonly int[] _colSetStrides;
    private int _selectedPageIndex;

    private RenderedTable(TableDefinition table)
    {
        Table = table;
        RowHeaderColumnCount = table.RowSets.Count;
        ValueColumnCount = CountCombinations(table.ColSets);
        DataRowCount = CountCombinations(table.RowSets);
        PageCount = Math.Max(1, table.PageKeys.Count);
        HeaderRowCount = table.ColSets.Count + 1;
        VisibleColumnCount = RowHeaderColumnCount + ValueColumnCount;

        _rowSetStrides = BuildStrides(table.RowSets);
        _colSetStrides = BuildStrides(table.ColSets);

        Columns = Enumerable
            .Range(0, VisibleColumnCount)
            .Select(index => new RenderedTableColumn($"C{index}", index < RowHeaderColumnCount))
            .ToList();

        Rows = new RenderedTableRowCollection(this);
    }

    public TableDefinition Table { get; }
    public int RowHeaderColumnCount { get; }
    public int ValueColumnCount { get; }
    public int DataRowCount { get; }
    public int PageCount { get; }
    public int HeaderRowCount { get; }
    public int VisibleColumnCount { get; }
    public bool HasPages => Table.PageKeys.Count > 0;
    public int SelectedPageIndex
    {
        get => _selectedPageIndex;
        set => _selectedPageIndex = Math.Clamp(value, 0, PageCount - 1);
    }
    public IReadOnlyList<string> PageNames => Table.PageKeys;
    public IReadOnlyList<RenderedTableColumn> Columns { get; }
    public IList Rows { get; }
    public RenderedTable? ComparisonTable { get; set; }
    public bool IsStructureEditingEnabled { get; set; }

    public event EventHandler<TableCellChangedEventArgs>? CellValueChanged;
    public event EventHandler<TableHeaderChangedEventArgs>? HeaderValueChanged;

    public static RenderedTable Create(TableDefinition table)
    {
        return new RenderedTable(table);
    }

    public string GetRowKind(int rowIndex)
    {
        return rowIndex < HeaderRowCount ? "Header" : "Data";
    }

    public string GetCellValue(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || columnIndex < 0 || columnIndex >= VisibleColumnCount)
        {
            return "";
        }

        if (rowIndex < Table.ColSets.Count)
        {
            return GetColumnSetHeaderValue(rowIndex, columnIndex);
        }

        if (rowIndex == Table.ColSets.Count)
        {
            return GetRowSetHeaderValue(columnIndex);
        }

        return GetDataValue(rowIndex - HeaderRowCount, columnIndex);
    }

    public void SetCellValue(int rowIndex, int columnIndex, string value)
    {
        string cellValue = value ?? "";

        if (TrySetHeaderValue(rowIndex, columnIndex, cellValue))
        {
            return;
        }

        int dataRowIndex = rowIndex - HeaderRowCount;
        int valueColumnIndex = columnIndex - RowHeaderColumnCount;
        int sourceRowIndex = GetSourceRowIndex(dataRowIndex);

        if (dataRowIndex < 0 || valueColumnIndex < 0 || valueColumnIndex >= ValueColumnCount)
        {
            return;
        }

        while (Table.DataRows.Count <= sourceRowIndex)
        {
            Table.DataRows.Add([]);
        }

        List<string> dataRow = Table.DataRows[sourceRowIndex];

        while (dataRow.Count <= valueColumnIndex)
        {
            dataRow.Add("");
        }

        if (dataRow[valueColumnIndex] == cellValue)
        {
            return;
        }

        dataRow[valueColumnIndex] = cellValue;
        CellValueChanged?.Invoke(
            this,
            new TableCellChangedEventArgs(
                Table,
                sourceRowIndex,
                valueColumnIndex,
                cellValue));
    }

    public bool IsCellChanged(int rowIndex, int columnIndex)
    {
        if (ComparisonTable is null)
        {
            return false;
        }

        if (rowIndex < 0 ||
            columnIndex < 0 ||
            rowIndex >= CountRows() ||
            columnIndex >= VisibleColumnCount ||
            rowIndex >= ComparisonTable.CountRows() ||
            columnIndex >= ComparisonTable.VisibleColumnCount)
        {
            return true;
        }

        return !string.Equals(
            GetCellValue(rowIndex, columnIndex),
            ComparisonTable.GetCellValue(rowIndex, columnIndex),
            StringComparison.Ordinal);
    }

    public static string GetChangedColumnName(string columnName)
    {
        return $"{ChangedColumnPrefix}{columnName}";
    }

    public bool TryGetHeaderTarget(
        int rowIndex,
        int columnIndex,
        out TableHeaderTarget target)
    {
        target = new TableHeaderTarget(TableHeaderTargetKind.RowHeader, -1, -1);

        if (rowIndex < 0 || columnIndex < 0 || columnIndex >= VisibleColumnCount)
        {
            return false;
        }

        if (rowIndex < Table.ColSets.Count)
        {
            if (columnIndex >= RowHeaderColumnCount)
            {
                int keyIndex = GetKeyIndex(Table.ColSets, _colSetStrides, columnIndex - RowHeaderColumnCount, rowIndex);
                target = new TableHeaderTarget(TableHeaderTargetKind.ColumnHeader, rowIndex, keyIndex);
                return keyIndex >= 0;
            }

            int labelColumnIndex = Math.Max(0, RowHeaderColumnCount - 1);

            if (columnIndex == labelColumnIndex)
            {
                target = new TableHeaderTarget(TableHeaderTargetKind.ColumnSetName, rowIndex, -1);
                return true;
            }

            return false;
        }

        if (rowIndex == Table.ColSets.Count)
        {
            if (columnIndex < RowHeaderColumnCount)
            {
                target = new TableHeaderTarget(TableHeaderTargetKind.RowSetName, columnIndex, -1);
                return true;
            }

            return false;
        }

        if (columnIndex < RowHeaderColumnCount)
        {
            int keyIndex = GetKeyIndex(Table.RowSets, _rowSetStrides, rowIndex - HeaderRowCount, columnIndex);
            target = new TableHeaderTarget(TableHeaderTargetKind.RowHeader, columnIndex, keyIndex);
            return keyIndex >= 0;
        }

        return false;
    }

    private bool TrySetHeaderValue(int rowIndex, int columnIndex, string value)
    {
        if (!TryGetHeaderTarget(rowIndex, columnIndex, out TableHeaderTarget target))
        {
            return false;
        }

        if (!IsStructureEditingEnabled)
        {
            return true;
        }

        string trimmedValue = value.Trim();

        if (string.IsNullOrWhiteSpace(trimmedValue))
        {
            return true;
        }

        string currentValue = GetCellValue(rowIndex, columnIndex);

        if (string.Equals(currentValue, trimmedValue, StringComparison.Ordinal))
        {
            return true;
        }

        ApplyHeaderValue(target, trimmedValue);
        HeaderValueChanged?.Invoke(
            this,
            new TableHeaderChangedEventArgs(Table, target.Kind, target.SetIndex, target.KeyIndex, trimmedValue));
        return true;
    }

    private void ApplyHeaderValue(TableHeaderTarget target, string value)
    {
        switch (target.Kind)
        {
            case TableHeaderTargetKind.RowSetName:
                Table.RowSets[target.SetIndex].Name = value;
                break;

            case TableHeaderTargetKind.ColumnSetName:
                Table.ColSets[target.SetIndex].Name = value;
                break;

            case TableHeaderTargetKind.RowHeader:
                Table.RowSets[target.SetIndex].Keys[target.KeyIndex] = value;
                break;

            case TableHeaderTargetKind.ColumnHeader:
                Table.ColSets[target.SetIndex].Keys[target.KeyIndex] = value;
                break;
        }
    }

    private string GetColumnSetHeaderValue(int colSetIndex, int columnIndex)
    {
        if (columnIndex >= RowHeaderColumnCount)
        {
            int valueColumnIndex = columnIndex - RowHeaderColumnCount;
            string displayValue = GetCombinationValue(Table.ColSets, _colSetStrides, valueColumnIndex, colSetIndex);

            if (!IsStructureEditingEnabled &&
                valueColumnIndex > 0 &&
                colSetIndex < Table.ColSets.Count - 1 &&
                GetCombinationValue(Table.ColSets, _colSetStrides, valueColumnIndex - 1, colSetIndex) == displayValue)
            {
                return "";
            }

            return displayValue;
        }

        int labelColumnIndex = Math.Max(0, RowHeaderColumnCount - 1);
        return columnIndex == labelColumnIndex ? Table.ColSets[colSetIndex].Name : "";
    }

    private string GetRowSetHeaderValue(int columnIndex)
    {
        return columnIndex < RowHeaderColumnCount
            ? Table.RowSets[columnIndex].Name
            : "";
    }

    private string GetDataValue(int dataRowIndex, int columnIndex)
    {
        if (dataRowIndex < 0 || dataRowIndex >= DataRowCount)
        {
            return "";
        }

        if (columnIndex < RowHeaderColumnCount)
        {
            string displayValue = GetCombinationValue(Table.RowSets, _rowSetStrides, dataRowIndex, columnIndex);

            if (!IsStructureEditingEnabled &&
                dataRowIndex > 0 &&
                columnIndex < RowHeaderColumnCount - 1 &&
                GetCombinationValue(Table.RowSets, _rowSetStrides, dataRowIndex - 1, columnIndex) == displayValue)
            {
                return "";
            }

            return displayValue;
        }

        int valueColumnIndex = columnIndex - RowHeaderColumnCount;
        int sourceRowIndex = GetSourceRowIndex(dataRowIndex);

        return sourceRowIndex < Table.DataRows.Count && valueColumnIndex < Table.DataRows[sourceRowIndex].Count
            ? Table.DataRows[sourceRowIndex][valueColumnIndex]
            : "";
    }

    private int GetSourceRowIndex(int dataRowIndex)
    {
        return (SelectedPageIndex * DataRowCount) + dataRowIndex;
    }

    private int CountRows()
    {
        return HeaderRowCount + DataRowCount;
    }

    private static int CountCombinations(List<KeySetDefinition> sets)
    {
        if (sets.Count == 0)
        {
            return 1;
        }

        int total = 1;

        foreach (KeySetDefinition set in sets)
        {
            if (set.Keys.Count == 0)
            {
                return 0;
            }

            total = checked(total * set.Keys.Count);
        }

        return total;
    }

    private static int[] BuildStrides(List<KeySetDefinition> sets)
    {
        var strides = new int[sets.Count];
        int stride = 1;

        for (int index = sets.Count - 1; index >= 0; index--)
        {
            strides[index] = stride;

            if (sets[index].Keys.Count == 0)
            {
                stride = 0;
                continue;
            }

            stride = checked(stride * sets[index].Keys.Count);
        }

        return strides;
    }

    private static string GetCombinationValue(
        List<KeySetDefinition> sets,
        int[] strides,
        int combinationIndex,
        int setIndex)
    {
        if (setIndex < 0 ||
            setIndex >= sets.Count ||
            combinationIndex < 0 ||
            strides[setIndex] == 0 ||
            sets[setIndex].Keys.Count == 0)
        {
            return "";
        }

        int keyIndex = (combinationIndex / strides[setIndex]) % sets[setIndex].Keys.Count;
        return sets[setIndex].Keys[keyIndex];
    }

    private static int GetKeyIndex(
        List<KeySetDefinition> sets,
        int[] strides,
        int combinationIndex,
        int setIndex)
    {
        if (setIndex < 0 ||
            setIndex >= sets.Count ||
            combinationIndex < 0 ||
            strides[setIndex] == 0 ||
            sets[setIndex].Keys.Count == 0)
        {
            return -1;
        }

        return (combinationIndex / strides[setIndex]) % sets[setIndex].Keys.Count;
    }
}

public sealed record RenderedTableColumn(string Name, bool IsRowHeader);

public sealed record TableCellChangedEventArgs(
    TableDefinition Table,
    int DataRowIndex,
    int ValueColumnIndex,
    string Value);

public enum TableHeaderTargetKind
{
    RowSetName,
    ColumnSetName,
    RowHeader,
    ColumnHeader
}

public sealed record TableHeaderTarget(
    TableHeaderTargetKind Kind,
    int SetIndex,
    int KeyIndex);

public sealed record TableHeaderChangedEventArgs(
    TableDefinition Table,
    TableHeaderTargetKind Kind,
    int SetIndex,
    int KeyIndex,
    string Value);

public sealed class RenderedTableRow : INotifyPropertyChanged
{
    private readonly RenderedTable _table;

    public RenderedTableRow(RenderedTable table, int rowIndex)
    {
        _table = table;
        RowIndex = rowIndex;
    }

    public int RowIndex { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal bool BelongsTo(RenderedTable table)
    {
        return ReferenceEquals(_table, table);
    }

    public string this[string columnName]
    {
        get
        {
            if (columnName == RenderedTable.RowKindColumnName)
            {
                return _table.GetRowKind(RowIndex);
            }

            if (TryGetChangedColumnIndex(columnName, out int changedColumnIndex))
            {
                return _table.IsCellChanged(RowIndex, changedColumnIndex)
                    ? bool.TrueString
                    : bool.FalseString;
            }

            return TryGetColumnIndex(columnName, out int columnIndex)
                ? _table.GetCellValue(RowIndex, columnIndex)
                : "";
        }
        set
        {
            if (!TryGetColumnIndex(columnName, out int columnIndex))
            {
                return;
            }

            _table.SetCellValue(RowIndex, columnIndex, value ?? "");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
    }

    public override bool Equals(object? obj)
    {
        return obj is RenderedTableRow row &&
            ReferenceEquals(row._table, _table) &&
            row.RowIndex == RowIndex;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_table, RowIndex);
    }

    private static bool TryGetColumnIndex(string columnName, out int columnIndex)
    {
        columnIndex = -1;

        return columnName.Length > 1 &&
            columnName[0] == 'C' &&
            int.TryParse(
                columnName.AsSpan(1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out columnIndex);
    }

    private static bool TryGetChangedColumnIndex(string columnName, out int columnIndex)
    {
        const string prefix = "__Changed_";
        columnIndex = -1;

        return columnName.StartsWith(prefix, StringComparison.Ordinal) &&
            TryGetColumnIndex(columnName[prefix.Length..], out columnIndex);
    }
}

public sealed class RenderedTableRowCollection : IList
{
    private readonly RenderedTable _table;
    private readonly Dictionary<int, RenderedTableRow> _rows = [];

    public RenderedTableRowCollection(RenderedTable table)
    {
        _table = table;
    }

    public int Count => _table.HeaderRowCount + _table.DataRowCount;
    public bool IsFixedSize => true;
    public bool IsReadOnly => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    public object? this[int index]
    {
        get
        {
            if (index < 0 || index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (!_rows.TryGetValue(index, out RenderedTableRow? row))
            {
                row = new RenderedTableRow(_table, index);
                _rows.Add(index, row);
            }

            return row;
        }
        set => throw new NotSupportedException();
    }

    public int Add(object? value)
    {
        throw new NotSupportedException();
    }

    public void Clear()
    {
        throw new NotSupportedException();
    }

    public bool Contains(object? value)
    {
        return IndexOf(value) >= 0;
    }

    public int IndexOf(object? value)
    {
        return value is RenderedTableRow row &&
            row.BelongsTo(_table) &&
            row.RowIndex >= 0 &&
            row.RowIndex < Count
            ? row.RowIndex
            : -1;
    }

    public void Insert(int index, object? value)
    {
        throw new NotSupportedException();
    }

    public void Remove(object? value)
    {
        throw new NotSupportedException();
    }

    public void RemoveAt(int index)
    {
        throw new NotSupportedException();
    }

    public void CopyTo(Array array, int index)
    {
        for (int rowIndex = 0; rowIndex < Count; rowIndex++)
        {
            array.SetValue(this[rowIndex], index + rowIndex);
        }
    }

    public IEnumerator GetEnumerator()
    {
        for (int rowIndex = 0; rowIndex < Count; rowIndex++)
        {
            yield return this[rowIndex];
        }
    }
}
