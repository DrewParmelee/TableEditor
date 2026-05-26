using AOTableEditor.Models;
using System.Windows;
using System.Windows.Input;

namespace AOTableEditor;

public partial class ExportTablesWindow : Window
{
    public ExportTablesWindow(IReadOnlyList<TableDefinition> tables)
    {
        InitializeComponent();

        TablesList.ItemsSource = tables;

        if (tables.Count > 0)
        {
            TablesList.SelectedIndex = 0;
        }
    }

    public IReadOnlyList<TableDefinition> SelectedTables { get; private set; } = [];

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        TablesList.SelectAll();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        CommitSelection();
    }

    private void TablesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        CommitSelection();
    }

    private void CommitSelection()
    {
        List<TableDefinition> selectedTables = TablesList.SelectedItems
            .OfType<TableDefinition>()
            .ToList();

        if (selectedTables.Count == 0)
        {
            return;
        }

        SelectedTables = selectedTables;
        DialogResult = true;
    }
}
