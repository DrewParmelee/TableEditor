using AOTableEditor.Models;
using AOTableEditor.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AOTableEditor;

public partial class MainWindow : Window
{
    private const string RowKindColumnName = "__RowKind";

    private List<TableDefinition> _tables = [];

    private TableDefinition? _currentTable;
    private int _currentColumnCount = -1;
    private int _currentRowHeaderColumnCount = -1;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open table XML file",
            Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _currentTable = null;
            _currentColumnCount = -1;
            _currentRowHeaderColumnCount = -1;

            RatingGrid.ItemsSource = null;
            RatingGrid.Columns.Clear();

            _tables = TableXmlParser.Load(dialog.FileName);
            TablesList.ItemsSource = _tables;

            if (_tables.Count > 0)
            {
                TablesList.SelectedIndex = 0;
            }

            Title = $"Table Editor - {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not open XML file.\n\n{ex.Message}",
                "Open failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void TablesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TablesList.SelectedItem is not TableDefinition table)
        {
            return;
        }

        RenderTable(table);
    }

    private void RenderTable(TableDefinition table)
    {
        if (ReferenceEquals(_currentTable, table))
        {
            return;
        }

        DataTable dataTable = table.RenderedDataTable ??= BuildDataTable(table);

        int visibleColumnCount = dataTable.Columns.Count - 1;
        int rowHeaderColumnCount = table.RowSets.Count;

        bool columnsNeedRebuild =
            RatingGrid.Columns.Count != visibleColumnCount ||
            _currentColumnCount != visibleColumnCount ||
            _currentRowHeaderColumnCount != rowHeaderColumnCount;

        RatingGrid.ItemsSource = null;

        if (columnsNeedRebuild)
        {
            RatingGrid.Columns.Clear();

            for (int i = 0; i < visibleColumnCount; i++)
            {
                string columnName = dataTable.Columns[i].ColumnName;
                bool isRowHeader = i < rowHeaderColumnCount;

                var gridColumn = new DataGridTextColumn
                {
                    Header = "",
                    Binding = new Binding($"[{columnName}]")
                    {
                        Mode = BindingMode.TwoWay
                    },
                    Width = GetColumnWidth(isRowHeader),
                    MinWidth = isRowHeader ? 100 : 60,
                    CanUserResize = true,
                    IsReadOnly = isRowHeader,
                    CellStyle = (Style)FindResource(isRowHeader ? "RowHeaderCellStyle" : "ValueCellStyle")
                };

                RatingGrid.Columns.Add(gridColumn);
            }

            _currentColumnCount = visibleColumnCount;
            _currentRowHeaderColumnCount = rowHeaderColumnCount;
        }

        RatingGrid.ItemsSource = dataTable.DefaultView;
        _currentTable = table;
    }

    private static DataTable BuildDataTable(TableDefinition table)
    {
        var dataTable = new DataTable();

        List<List<string>> colCombinations = GetColumnCombinations(table);
        int totalVisibleColumns = table.RowSets.Count + colCombinations.Count;

        for (int i = 0; i < totalVisibleColumns; i++)
        {
            dataTable.Columns.Add($"C{i}");
        }

        dataTable.Columns.Add(RowKindColumnName);

        AddColumnSetHeaderRows(dataTable, table, colCombinations);
        AddRowSetHeaderRow(dataTable, table);
        AddDataRows(dataTable, table, colCombinations);

        return dataTable;
    }

    private static void AddColumnSetHeaderRows(
        DataTable dataTable,
        TableDefinition table,
        List<List<string>> colCombinations)
    {
        if (table.ColSets.Count == 0)
        {
            return;
        }

        for (int colSetIndex = 0; colSetIndex < table.ColSets.Count; colSetIndex++)
        {
            DataRow headerRow = dataTable.NewRow();
            headerRow[RowKindColumnName] = "Header";

            int labelColumnIndex = Math.Max(0, table.RowSets.Count - 1);
            headerRow[labelColumnIndex] = table.ColSets[colSetIndex].Name;

            for (int colComboIndex = 0; colComboIndex < colCombinations.Count; colComboIndex++)
            {
                int targetColumnIndex = table.RowSets.Count + colComboIndex;
                headerRow[targetColumnIndex] = colCombinations[colComboIndex][colSetIndex];
            }

            dataTable.Rows.Add(headerRow);
        }
    }

    private static void AddRowSetHeaderRow(DataTable dataTable, TableDefinition table)
    {
        DataRow headerRow = dataTable.NewRow();
        headerRow[RowKindColumnName] = "Header";

        for (int rowSetIndex = 0; rowSetIndex < table.RowSets.Count; rowSetIndex++)
        {
            headerRow[rowSetIndex] = table.RowSets[rowSetIndex].Name;
        }

        dataTable.Rows.Add(headerRow);
    }

    private static void AddDataRows(
        DataTable dataTable,
        TableDefinition table,
        List<List<string>> colCombinations)
    {
        List<List<string>> rowCombinations = BuildCombinations(
            table.RowSets.Select(x => x.Keys).ToList());

        dataTable.BeginLoadData();

        for (int rowIndex = 0; rowIndex < rowCombinations.Count; rowIndex++)
        {
            object[] rowValues = new object[dataTable.Columns.Count];
            rowValues[^1] = "Data";

            List<string> rowCombo = rowCombinations[rowIndex];

            for (int rowSetIndex = 0; rowSetIndex < rowCombo.Count; rowSetIndex++)
            {
                string displayValue = rowCombo[rowSetIndex];

                if (rowIndex > 0 &&
                    rowSetIndex < rowCombo.Count - 1 &&
                    rowCombinations[rowIndex - 1][rowSetIndex] == displayValue)
                {
                    displayValue = "";
                }

                rowValues[rowSetIndex] = displayValue;
            }

            List<string> values = rowIndex < table.DataRows.Count
                ? table.DataRows[rowIndex]
                : [];

            for (int colIndex = 0; colIndex < colCombinations.Count; colIndex++)
            {
                int targetColumnIndex = table.RowSets.Count + colIndex;
                rowValues[targetColumnIndex] = colIndex < values.Count ? values[colIndex] : "";
            }

            dataTable.Rows.Add(rowValues);
        }

        dataTable.EndLoadData();
    }

    private static List<List<string>> GetColumnCombinations(TableDefinition table)
    {
        return BuildCombinations(table.ColSets.Select(x => x.Keys).ToList());
    }

    private static List<List<string>> BuildCombinations(List<List<string>> sets)
    {
        var result = new List<List<string>>();

        if (sets.Count == 0)
        {
            result.Add([]);
            return result;
        }

        BuildCombinationsRecursive(sets, 0, [], result);
        return result;
    }

    private static void BuildCombinationsRecursive(
        List<List<string>> sets,
        int depth,
        List<string> current,
        List<List<string>> result)
    {
        if (depth == sets.Count)
        {
            result.Add([.. current]);
            return;
        }

        foreach (string value in sets[depth])
        {
            current.Add(value);
            BuildCombinationsRecursive(sets, depth + 1, current, result);
            current.RemoveAt(current.Count - 1);
        }
    }

    private static DataGridLength GetColumnWidth(bool isRowHeader)
    {
        return isRowHeader
            ? new DataGridLength(190)
            : new DataGridLength(85);
    }
}