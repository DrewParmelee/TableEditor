using AOTableEditor.Models;
using AOTableEditor.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Linq;

namespace AOTableEditor;

public partial class MainWindow : Window
{
    private const string UntitledFileName = "Untitled.xml";

    private readonly ObservableCollection<OpenDocumentTab> _openDocuments = [];

    private List<TableDefinition> _tables = [];
    private List<TableDefinition> _originalTables = [];

    private string? _currentFilePath;
    private string? _ghostFilePath;
    private XDocument? _originalDocument;
    private XDocument? _ghostDocument;
    private OpenDocumentTab? _activeDocument;
    private TableDefinition? _currentTable;
    private RenderedTable? _activeRenderedTable;
    private ContextMenu? _tableContextMenu;
    private ScrollViewer? _pendingGridScrollViewer;
    private ScrollViewer? _currentGridScrollViewer;
    private bool _isUpdatingPageTabs;
    private bool _isUpdatingViewToggles;
    private bool _isGridRefreshPending;
    private bool _isApplyingClipboardPaste;
    private bool _clipboardPasteChanged;
    private bool _isSynchronizingGridScroll;
    private bool _isSwitchingDocuments;
    private int _untitledDocumentCount;

    public MainWindow()
    {
        InitializeComponent();
        ConfigureDocumentTabs();
        ConfigureTableContextMenu();
        ConfigureGridScrollSynchronization();
        UpdateSaveState();
        UpdateViewVisibility();
    }

    private void ConfigureDocumentTabs()
    {
        DocumentTabsList.ItemsSource = _openDocuments;
        UpdateDocumentTabsVisibility();
    }

    private void DocumentTabsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSwitchingDocuments ||
            DocumentTabsList.SelectedItem is not OpenDocumentTab document ||
            ReferenceEquals(document, _activeDocument))
        {
            return;
        }

        SelectDocument(document);
    }

    private void CloseDocumentTab_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if ((sender as FrameworkElement)?.DataContext is OpenDocumentTab document)
        {
            CloseDocument(document);
        }
    }

    private void SelectDocument(OpenDocumentTab document)
    {
        if (!ReferenceEquals(_activeDocument, document))
        {
            SaveActiveDocumentState();
        }

        _isSwitchingDocuments = true;

        try
        {
            DocumentTabsList.SelectedItem = document;
        }
        finally
        {
            _isSwitchingDocuments = false;
        }

        LoadDocumentState(document);
    }

    private void SaveActiveDocumentState()
    {
        if (_activeDocument is null)
        {
            return;
        }

        _activeDocument.FilePath = _currentFilePath;
        _activeDocument.GhostFilePath = _ghostFilePath;
        _activeDocument.OriginalDocument = _originalDocument;
        _activeDocument.GhostDocument = _ghostDocument;
        _activeDocument.Tables = _tables;
        _activeDocument.OriginalTables = _originalTables;
        _activeDocument.SelectedTableIndex = Math.Max(0, TablesList.SelectedIndex);
        _activeDocument.HasChanges = HasAnyChanges();
        _activeDocument.RefreshDisplayName();
    }

    private void LoadDocumentState(OpenDocumentTab document)
    {
        DetachActiveRenderedTable();

        _activeDocument = document;
        _currentFilePath = document.FilePath;
        _ghostFilePath = document.GhostFilePath;
        _originalDocument = document.OriginalDocument;
        _ghostDocument = document.GhostDocument;
        _tables = document.Tables;
        _originalTables = document.OriginalTables;
        _currentTable = null;

        ClearTableViews();
        ApplyDirtyStates();
        document.HasChanges = HasAnyChanges();

        TablesList.ItemsSource = null;
        TablesList.ItemsSource = _tables;

        if (_tables.Count > 0)
        {
            TablesList.SelectedIndex = Math.Clamp(document.SelectedTableIndex, 0, _tables.Count - 1);
        }

        UpdateSaveState();
        UpdateTitle();
        UpdateViewVisibility();
        UpdateDocumentTabsVisibility();
    }

    private void UpdateDocumentTabsVisibility()
    {
        if (DocumentTabsRow is null)
        {
            return;
        }

        DocumentTabsRow.Visibility = _openDocuments.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void CloseDocument(OpenDocumentTab document)
    {
        if (!EnsureDocumentCanClose(document))
        {
            return;
        }

        bool wasActive = ReferenceEquals(document, _activeDocument);
        int oldIndex = _openDocuments.IndexOf(document);

        _openDocuments.Remove(document);

        if (wasActive)
        {
            _activeDocument = null;

            if (_openDocuments.Count > 0)
            {
                int nextIndex = Math.Clamp(oldIndex, 0, _openDocuments.Count - 1);
                SelectDocument(_openDocuments[nextIndex]);
            }
            else
            {
                ClearActiveDocument();
            }
        }
        else
        {
            UpdateDocumentTabsVisibility();
        }
    }

    private bool EnsureDocumentCanClose(OpenDocumentTab document)
    {
        SaveActiveDocumentState();

        if (!DocumentHasChanges(document))
        {
            return true;
        }

        string name = document.FilePath is null
            ? document.UntitledName
            : Path.GetFileName(document.FilePath);

        MessageBoxResult result = MessageBox.Show(
            $"Save changes to '{name}' before closing?",
            "Unsaved changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Cancel)
        {
            return false;
        }

        if (result == MessageBoxResult.No)
        {
            return true;
        }

        SelectDocument(document);
        return SaveActiveDocumentChanges();
    }

    private void ClearActiveDocument()
    {
        DetachActiveRenderedTable();
        _currentFilePath = null;
        _ghostFilePath = null;
        _originalDocument = null;
        _ghostDocument = null;
        _tables = [];
        _originalTables = [];
        _currentTable = null;

        TablesList.ItemsSource = null;
        ClearTableViews();

        _isSwitchingDocuments = true;

        try
        {
            DocumentTabsList.SelectedItem = null;
        }
        finally
        {
            _isSwitchingDocuments = false;
        }

        UpdateSaveState();
        UpdateTitle();
        UpdateViewVisibility();
        UpdateDocumentTabsVisibility();
    }

    private void ConfigureTableContextMenu()
    {
        var contextMenu = new ContextMenu();

        var propertiesItem = new MenuItem
        {
            Header = "Properties"
        };
        propertiesItem.Click += TableProperties_Click;

        var viewXmlItem = new MenuItem
        {
            Header = "View Table XML"
        };
        viewXmlItem.Click += ViewTableXml_Click;

        var deleteTableItem = new MenuItem
        {
            Header = "Delete Table"
        };
        deleteTableItem.Click += RemoveTable_Click;

        contextMenu.Items.Add(propertiesItem);
        contextMenu.Items.Add(viewXmlItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(deleteTableItem);

        _tableContextMenu = contextMenu;
        TablesList.ContextMenu = contextMenu;
    }

    private void ConfigureGridScrollSynchronization()
    {
        RatingGrid.Loaded += Grid_Loaded;
        CurrentGrid.Loaded += Grid_Loaded;
    }

    private void Grid_Loaded(object sender, RoutedEventArgs e)
    {
        AttachGridScrollSynchronization();
    }

    private void AttachGridScrollSynchronization()
    {
        AttachGridScrollViewer(RatingGrid, ref _pendingGridScrollViewer);
        AttachGridScrollViewer(CurrentGrid, ref _currentGridScrollViewer);
    }

    private void AttachGridScrollViewer(
        DataGrid grid,
        ref ScrollViewer? scrollViewer)
    {
        ScrollViewer? nextScrollViewer = FindChild<ScrollViewer>(grid);

        if (ReferenceEquals(scrollViewer, nextScrollViewer))
        {
            return;
        }

        if (scrollViewer is not null)
        {
            scrollViewer.ScrollChanged -= GridScrollViewer_ScrollChanged;
        }

        scrollViewer = nextScrollViewer;

        if (scrollViewer is not null)
        {
            scrollViewer.ScrollChanged += GridScrollViewer_ScrollChanged;
        }
    }

    private void GridScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isSynchronizingGridScroll ||
            sender is not ScrollViewer source ||
            e.VerticalChange == 0 &&
            e.HorizontalChange == 0)
        {
            return;
        }

        ScrollViewer? target = ReferenceEquals(source, _pendingGridScrollViewer)
            ? _currentGridScrollViewer
            : ReferenceEquals(source, _currentGridScrollViewer)
                ? _pendingGridScrollViewer
                : null;

        if (target is null)
        {
            return;
        }

        SynchronizeGridScroll(source, target);
    }

    private void SynchronizeGridScroll(
        ScrollViewer source,
        ScrollViewer target)
    {
        _isSynchronizingGridScroll = true;

        try
        {
            target.ScrollToHorizontalOffset(source.HorizontalOffset);
            target.ScrollToVerticalOffset(source.VerticalOffset);
        }
        finally
        {
            _isSynchronizingGridScroll = false;
        }
    }

    private void SynchronizeCurrentScrollToPending()
    {
        AttachGridScrollSynchronization();

        if (_pendingGridScrollViewer is not null &&
            _currentGridScrollViewer is not null)
        {
            SynchronizeGridScroll(_pendingGridScrollViewer, _currentGridScrollViewer);
        }
    }

    private void NewFile_Click(object sender, RoutedEventArgs e)
    {
        CreateNewDocument();
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder containing table XML files"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var picker = new XmlFilePickerWindow(dialog.FolderName, GetOpenDocumentFilePaths())
        {
            Owner = this
        };

        if (picker.ShowDialog() != true ||
            picker.SelectedFilePaths.Count == 0)
        {
            return;
        }

        try
        {
            foreach (string filePath in picker.SelectedFilePaths)
            {
                OpenFileInTab(filePath);
            }
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

    private void Metadata_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetLoadedDocument(out XDocument ghostDocument))
        {
            return;
        }

        var metadataWindow = new MetadataWindow(TableMetadataService.ReadMetadata(ghostDocument))
        {
            Owner = this
        };

        if (metadataWindow.ShowDialog() != true ||
            metadataWindow.Result is null)
        {
            return;
        }

        TableMetadataService.ApplyMetadata(ghostDocument, metadataWindow.Result);
        ReloadGhostState(TablesList.SelectedIndex);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveActiveDocumentChanges();
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        SaveAs();
    }

    private void ExportTables_Click(object sender, RoutedEventArgs e)
    {
        if (_tables.Count == 0)
        {
            ShowInformation("Open an XML file with at least one table first.");
            return;
        }

        if (!TryCommitPendingGridEdit())
        {
            return;
        }

        var exportWindow = new ExportTablesWindow(_tables)
        {
            Owner = this
        };

        if (exportWindow.ShowDialog() != true ||
            exportWindow.SelectedTables.Count == 0)
        {
            return;
        }

        try
        {
            if (exportWindow.SelectedTables.Count == 1)
            {
                ExportSingleTable(exportWindow.SelectedTables[0]);
            }
            else
            {
                ExportMultipleTables(exportWindow.SelectedTables);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not export table data.\n\n{ex.Message}",
                "Export failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void XmlFormatReference_Click(object sender, RoutedEventArgs e)
    {
        var referenceWindow = new XmlFormatReferenceWindow
        {
            Owner = this
        };

        referenceWindow.ShowDialog();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFilePath is null)
        {
            return;
        }

        try
        {
            LoadFile(_currentFilePath, TablesList.SelectedIndex);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not reload XML file.\n\n{ex.Message}",
                "Reload failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool TryCommitPendingGridEdit()
    {
        if (!RatingGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true))
        {
            return false;
        }

        RatingGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        return true;
    }

    private void ExportSingleTable(TableDefinition table)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export table",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = $"{GetSafeCsvBaseName(table.Name)}.csv"
        };

        string? directory = _currentFilePath is null
            ? null
            : Path.GetDirectoryName(_currentFilePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            dialog.InitialDirectory = directory;
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        WriteTableCsv(table, dialog.FileName);
        ShowInformation($"Exported '{table.Name}' to:\n{dialog.FileName}");
    }

    private void ExportMultipleTables(IReadOnlyList<TableDefinition> tables)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose where to export table CSV files"
        };

        string? directory = _currentFilePath is null
            ? null
            : Path.GetDirectoryName(_currentFilePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            dialog.InitialDirectory = directory;
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (TableDefinition table in tables)
        {
            string filePath = GetAvailableCsvPath(dialog.FolderName, table.Name, usedPaths);
            WriteTableCsv(table, filePath);
            usedPaths.Add(filePath);
        }

        ShowInformation($"Exported {tables.Count} tables to:\n{dialog.FolderName}");
    }

    private static void WriteTableCsv(TableDefinition table, string filePath)
    {
        File.WriteAllText(filePath, TableCsvExporter.ToCsv(table), Encoding.UTF8);
    }

    private static string GetAvailableCsvPath(
        string folderPath,
        string tableName,
        HashSet<string> usedPaths)
    {
        string baseName = GetSafeCsvBaseName(tableName);
        string filePath = Path.Combine(folderPath, $"{baseName}.csv");

        if (!File.Exists(filePath) && !usedPaths.Contains(filePath))
        {
            return filePath;
        }

        for (int suffix = 2; ; suffix++)
        {
            filePath = Path.Combine(folderPath, $"{baseName} ({suffix}).csv");

            if (!File.Exists(filePath) && !usedPaths.Contains(filePath))
            {
                return filePath;
            }
        }
    }

    private static string GetSafeCsvBaseName(string tableName)
    {
        string safeName = string.Join(
            "_",
            tableName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

        safeName = safeName.Trim();
        return string.IsNullOrWhiteSpace(safeName) ? "Table" : safeName;
    }

    private void CreateNewDocument()
    {
        _untitledDocumentCount++;
        string untitledName = _untitledDocumentCount == 1
            ? UntitledFileName
            : $"Untitled {_untitledDocumentCount}.xml";
        XDocument originalDocument = CreateEmptyTableDocument();

        var document = new OpenDocumentTab
        {
            UntitledName = untitledName,
            GhostFilePath = CreateGhostFilePath(untitledName),
            OriginalDocument = originalDocument,
            GhostDocument = new XDocument(originalDocument),
            OriginalTables = [],
            Tables = []
        };

        PersistGhostDocument(document);
        _openDocuments.Add(document);
        SelectDocument(document);
        LoadGhostTables();
    }

    private void OpenFileInTab(string filePath)
    {
        string fullPath = Path.GetFullPath(filePath);
        OpenDocumentTab? existingDocument = _openDocuments.FirstOrDefault(document =>
            document.FilePath is not null &&
            string.Equals(Path.GetFullPath(document.FilePath), fullPath, StringComparison.OrdinalIgnoreCase));

        if (existingDocument is not null)
        {
            SelectDocument(existingDocument);
            return;
        }

        XDocument originalDocument = XDocument.Load(filePath, LoadOptions.PreserveWhitespace);
        var document = new OpenDocumentTab
        {
            FilePath = filePath,
            GhostFilePath = CreateGhostFilePath(filePath),
            OriginalDocument = originalDocument,
            GhostDocument = new XDocument(originalDocument),
            OriginalTables = TableXmlParser.LoadDocument(originalDocument),
            Tables = []
        };

        PersistGhostDocument(document);
        _openDocuments.Add(document);
        SelectDocument(document);
        LoadGhostTables();
    }

    private bool SaveAs()
    {
        if (_ghostDocument is null)
        {
            MessageBox.Show(
                "Open or create an XML file first.",
                "No XML file",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save table XML file",
            Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
            DefaultExt = ".xml",
            AddExtension = true,
            FileName = _currentFilePath is null
                ? _activeDocument?.UntitledName ?? UntitledFileName
                : Path.GetFileName(_currentFilePath)
        };

        string? directory = _currentFilePath is null
            ? null
            : Path.GetDirectoryName(_currentFilePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            dialog.InitialDirectory = directory;
        }

        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        try
        {
            SavePendingDocument(dialog.FileName);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not save XML file.\n\n{ex.Message}",
                "Save failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private void SavePendingDocument(string filePath)
    {
        if (_ghostDocument is null)
        {
            throw new InvalidOperationException("No working XML document is loaded.");
        }

        _ghostFilePath ??= CreateGhostFilePath(filePath);

        int selectedIndex = TablesList.SelectedIndex;
        PersistGhostDocument();
        File.Copy(_ghostFilePath, filePath, overwrite: true);
        LoadFile(filePath, selectedIndex);
    }

    private bool SaveActiveDocumentChanges()
    {
        if (_ghostDocument is null)
        {
            return false;
        }

        if (_currentFilePath is null)
        {
            return SaveAs();
        }

        try
        {
            SavePendingDocument(_currentFilePath);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not save XML file.\n\n{ex.Message}",
                "Save failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private IEnumerable<string> GetOpenDocumentFilePaths()
    {
        SaveActiveDocumentState();

        return _openDocuments
            .Select(document => document.FilePath)
            .Where(filePath => !string.IsNullOrWhiteSpace(filePath))
            .Select(filePath => Path.GetFullPath(filePath!))
            .ToList();
    }

    private bool ConfirmDiscardPendingChanges()
    {
        if (!HasAnyChanges())
        {
            return true;
        }

        return MessageBox.Show(
            "Discard pending changes?",
            "Unsaved changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void LoadFile(string filePath, int selectedIndex = 0)
    {
        if (_activeDocument is null)
        {
            var document = new OpenDocumentTab();
            _openDocuments.Add(document);
            _activeDocument = document;
        }

        _currentFilePath = filePath;
        _ghostFilePath = CreateGhostFilePath(filePath);
        _originalDocument = XDocument.Load(filePath, LoadOptions.PreserveWhitespace);
        _ghostDocument = new XDocument(_originalDocument);
        PersistGhostDocument();

        _originalTables = TableXmlParser.LoadDocument(_originalDocument);
        LoadGhostTables(selectedIndex);
    }

    private static XDocument CreateEmptyTableDocument()
    {
        return new XDocument(new XElement("tables"));
    }

    private void LoadGhostTables(int selectedIndex = 0)
    {
        if (_ghostDocument is null)
        {
            return;
        }

        DetachActiveRenderedTable();
        _currentTable = null;
        ClearTableViews();

        _tables = TableXmlParser.LoadDocument(_ghostDocument);
        ApplyDirtyStates();

        TablesList.ItemsSource = null;
        TablesList.ItemsSource = _tables;

        if (_tables.Count > 0)
        {
            TablesList.SelectedIndex = Math.Clamp(selectedIndex, 0, _tables.Count - 1);
        }

        UpdateSaveState();
        UpdateTitle();
        SaveActiveDocumentState();
        UpdateDocumentTabsVisibility();
    }

    private void ClearTableViews()
    {
        CommentText.Text = "";
        RatingGrid.ItemsSource = null;
        RatingGrid.Columns.Clear();
        CurrentGrid.ItemsSource = null;
        CurrentGrid.Columns.Clear();
        PageTabs.ItemsSource = null;
        PageTabs.Visibility = Visibility.Collapsed;
        PageTabsRow.Visibility = Visibility.Collapsed;
    }

    private void TablesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TablesList.SelectedItem is not TableDefinition table)
        {
            return;
        }

        RenderTable(table);
        SaveActiveDocumentState();
    }

    private void RenderTable(TableDefinition table)
    {
        _currentTable = table;
        CommentText.Text = string.IsNullOrWhiteSpace(table.Comment)
            ? ""
            : table.Comment;

        RenderedTable updatedTable = table.RenderedTable ??= RenderedTable.Create(table);
        AttachActiveRenderedTable(updatedTable);

        TableDefinition? originalDefinition = _originalTables.ElementAtOrDefault(table.SourceIndex);
        RenderedTable? originalTable = null;

        if (originalDefinition is not null)
        {
            originalTable = originalDefinition.RenderedTable ??= RenderedTable.Create(originalDefinition);
        }

        if (originalTable is not null)
        {
            originalTable.SelectedPageIndex = Math.Min(updatedTable.SelectedPageIndex, originalTable.PageCount - 1);
            originalTable.ComparisonTable = updatedTable;
            updatedTable.ComparisonTable = originalTable;
        }
        else
        {
            updatedTable.ComparisonTable = null;
        }

        ConfigurePageTabs(updatedTable);
        RenderGrid(RatingGrid, updatedTable, isReadOnly: false, useUpdatedChangeBrush: true);

        if (originalTable is not null)
        {
            RenderGrid(CurrentGrid, originalTable, isReadOnly: true, useUpdatedChangeBrush: false);
        }
        else
        {
            CurrentGrid.ItemsSource = null;
            CurrentGrid.Columns.Clear();
        }

        UpdateViewVisibility();
    }

    private void RenderGrid(
        DataGrid grid,
        RenderedTable renderedTable,
        bool isReadOnly,
        bool useUpdatedChangeBrush)
    {
        grid.ItemsSource = null;
        grid.Columns.Clear();
        grid.IsReadOnly = isReadOnly;

        foreach (RenderedTableColumn column in renderedTable.Columns)
        {
            var gridColumn = new DataGridTextColumn
            {
                Header = "",
                Binding = new Binding($"[{column.Name}]")
                {
                    Mode = isReadOnly || column.IsRowHeader
                        ? BindingMode.OneWay
                        : BindingMode.TwoWay
                },
                Width = GetColumnWidth(column.IsRowHeader),
                MinWidth = column.IsRowHeader ? 100 : 60,
                CanUserResize = true,
                IsReadOnly = isReadOnly || column.IsRowHeader,
                CellStyle = CreateCellStyle(column, useUpdatedChangeBrush)
            };

            grid.Columns.Add(gridColumn);
        }

        grid.ItemsSource = renderedTable.Rows;
    }

    private Style CreateCellStyle(RenderedTableColumn column, bool useUpdatedChangeBrush)
    {
        var baseStyle = (Style)FindResource(column.IsRowHeader ? "RowHeaderCellStyle" : "ValueCellStyle");
        var style = new Style(typeof(DataGridCell), baseStyle);

        var changedTrigger = new DataTrigger
        {
            Binding = new Binding($"[{RenderedTable.GetChangedColumnName(column.Name)}]"),
            Value = bool.TrueString
        };

        changedTrigger.Setters.Add(new Setter(
            DataGridCell.BackgroundProperty,
            FindResource(useUpdatedChangeBrush ? "AoGreenFadedBrush" : "AoOrangeFadedBrush")));
        changedTrigger.Setters.Add(new Setter(
            DataGridCell.ForegroundProperty,
            FindResource("AoCharcoalBrush")));

        style.Triggers.Add(changedTrigger);
        return style;
    }

    private void AttachActiveRenderedTable(RenderedTable renderedTable)
    {
        if (ReferenceEquals(_activeRenderedTable, renderedTable))
        {
            return;
        }

        DetachActiveRenderedTable();
        _activeRenderedTable = renderedTable;
        _activeRenderedTable.CellValueChanged += RenderedTable_CellValueChanged;
        _activeRenderedTable.HeaderValueChanged += RenderedTable_HeaderValueChanged;
    }

    private void DetachActiveRenderedTable()
    {
        if (_activeRenderedTable is null)
        {
            return;
        }

        _activeRenderedTable.CellValueChanged -= RenderedTable_CellValueChanged;
        _activeRenderedTable.HeaderValueChanged -= RenderedTable_HeaderValueChanged;
        _activeRenderedTable = null;
    }

    private void RenderedTable_CellValueChanged(object? sender, TableCellChangedEventArgs e)
    {
        if (_ghostDocument is null)
        {
            return;
        }

        UpdateGhostDataCell(e.Table, e.DataRowIndex, e.ValueColumnIndex, e.Value);

        if (_isApplyingClipboardPaste)
        {
            _clipboardPasteChanged = true;
            return;
        }

        FinalizeGhostChanges();
    }

    private void RenderedTable_HeaderValueChanged(object? sender, TableHeaderChangedEventArgs e)
    {
        if (_ghostDocument is null)
        {
            return;
        }

        XElement? tableElement = GetTableElement(_ghostDocument, e.Table);

        if (tableElement is null)
        {
            return;
        }

        ApplyHeaderValueToGhost(tableElement, e);
        FinalizeGhostChanges();
    }

    private void FinalizeGhostChanges()
    {
        PersistGhostDocument();
        ApplyDirtyStates();
        UpdateSaveState();
        UpdateTitle();
        SaveActiveDocumentState();
        UpdateViewVisibility();
        ScheduleGridRefresh();
    }

    private void ScheduleGridRefresh()
    {
        if (_isGridRefreshPending)
        {
            return;
        }

        _isGridRefreshPending = true;
        QueueGridRefresh();
    }

    private void QueueGridRefresh()
    {
        Dispatcher.BeginInvoke(RefreshGridsWhenReady, DispatcherPriority.ContextIdle);
    }

    private void RefreshGridsWhenReady()
    {
        if (IsGridRefreshBlocked(RatingGrid) ||
            IsGridRefreshBlocked(CurrentGrid))
        {
            QueueGridRefresh();
            return;
        }

        try
        {
            RatingGrid.Items.Refresh();
            CurrentGrid.Items.Refresh();
            _isGridRefreshPending = false;
        }
        catch (InvalidOperationException ex) when (IsRefreshDuringEditTransaction(ex))
        {
            QueueGridRefresh();
        }
    }

    private static bool IsGridRefreshBlocked(DataGrid grid)
    {
        if (grid.Items is IEditableCollectionView itemsView &&
            IsEditableViewBusy(itemsView))
        {
            return true;
        }

        return grid.ItemsSource is not null &&
            CollectionViewSource.GetDefaultView(grid.ItemsSource) is IEditableCollectionView sourceView &&
            IsEditableViewBusy(sourceView);
    }

    private static bool IsEditableViewBusy(IEditableCollectionView view)
    {
        return view.IsAddingNew || view.IsEditingItem;
    }

    private static bool IsRefreshDuringEditTransaction(InvalidOperationException ex)
    {
        return ex.Message.Contains(
            "AddNew or EditItem transaction",
            StringComparison.OrdinalIgnoreCase);
    }

    private void RatingGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (_currentTable?.RenderedTable is not RenderedTable renderedTable ||
            e.Row.Item is not RenderedTableRow row ||
            e.Column is null)
        {
            return;
        }

        int columnIndex = RatingGrid.Columns.IndexOf(e.Column);

        if (renderedTable.TryGetHeaderTarget(row.RowIndex, columnIndex, out _))
        {
            e.Cancel = true;
        }
    }

    private void RatingGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit ||
            e.EditingElement is not TextBox editor)
        {
            return;
        }

        if (!TryNormalizeEditedCellValue(e, editor.Text, out string normalizedValue, out string error))
        {
            e.Cancel = true;
            ShowInvalidCellValueMessage(error);

            Dispatcher.BeginInvoke(
                () =>
                {
                    editor.Focus();
                    editor.SelectAll();
                },
                DispatcherPriority.Background);
            return;
        }

        if (editor.Text != normalizedValue)
        {
            editor.Text = normalizedValue;
        }

        editor.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    private bool TryNormalizeEditedCellValue(
        DataGridCellEditEndingEventArgs e,
        string value,
        out string normalizedValue,
        out string error)
    {
        normalizedValue = value;
        error = "";

        if (_currentTable?.RenderedTable is not RenderedTable renderedTable ||
            e.Row.Item is not RenderedTableRow row ||
            e.Column is null)
        {
            return true;
        }

        int columnIndex = RatingGrid.Columns.IndexOf(e.Column);

        return TryNormalizeDataCellValue(
            renderedTable,
            row.RowIndex,
            columnIndex,
            value,
            out normalizedValue,
            out error);
    }

    private void RatingGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return &&
            (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == 0)
        {
            MoveRatingGridSelectionDownAfterCommit(e);
            return;
        }

        if (e.Key != Key.V ||
            (Keyboard.Modifiers & ModifierKeys.Control) == 0 ||
            !Clipboard.ContainsText())
        {
            return;
        }

        string clipboardText = Clipboard.GetText(TextDataFormat.UnicodeText);

        if (string.IsNullOrEmpty(clipboardText))
        {
            return;
        }

        if (e.OriginalSource is TextBox && !ContainsTableBreaks(clipboardText))
        {
            return;
        }

        if (_currentTable?.RenderedTable is not RenderedTable renderedTable ||
            !TryGetPasteStartCell(renderedTable, out int startRowIndex, out int startColumnIndex))
        {
            return;
        }

        List<List<string>> pastedRows = ParseTabDelimitedText(clipboardText);

        if (pastedRows.Count == 0)
        {
            return;
        }

        e.Handled = true;

        if (!RatingGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true))
        {
            return;
        }

        RatingGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        ApplyClipboardRows(renderedTable, startRowIndex, startColumnIndex, pastedRows);
    }

    private void MoveRatingGridSelectionDownAfterCommit(KeyEventArgs e)
    {
        if (_currentTable?.RenderedTable is not RenderedTable renderedTable ||
            !TryGetRatingGridCurrentPosition(e.OriginalSource as DependencyObject, out RenderedTableRow row, out int columnIndex))
        {
            return;
        }

        if (row.RowIndex < renderedTable.HeaderRowCount ||
            columnIndex < renderedTable.RowHeaderColumnCount ||
            columnIndex >= renderedTable.VisibleColumnCount)
        {
            return;
        }

        e.Handled = true;

        if (!RatingGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true))
        {
            return;
        }

        RatingGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        int nextRowIndex = Math.Min(row.RowIndex + 1, renderedTable.Rows.Count - 1);

        Dispatcher.BeginInvoke(
            () => MoveRatingGridCurrentCell(nextRowIndex, columnIndex),
            DispatcherPriority.ApplicationIdle);
    }

    private bool TryGetRatingGridCurrentPosition(
        DependencyObject? source,
        out RenderedTableRow row,
        out int columnIndex)
    {
        if (source is not null &&
            FindParent<DataGridCell>(source) is DataGridCell sourceCell &&
            sourceCell.DataContext is RenderedTableRow sourceRow &&
            sourceCell.Column is not null)
        {
            row = sourceRow;
            columnIndex = RatingGrid.Columns.IndexOf(sourceCell.Column);
            return columnIndex >= 0;
        }

        if (RatingGrid.CurrentCell.Item is RenderedTableRow currentRow &&
            RatingGrid.CurrentCell.Column is not null)
        {
            row = currentRow;
            columnIndex = RatingGrid.Columns.IndexOf(RatingGrid.CurrentCell.Column);
            return columnIndex >= 0;
        }

        row = null!;
        columnIndex = -1;
        return false;
    }

    private void MoveRatingGridCurrentCell(int rowIndex, int columnIndex)
    {
        if (_currentTable?.RenderedTable is not RenderedTable renderedTable ||
            rowIndex < renderedTable.HeaderRowCount ||
            rowIndex >= renderedTable.Rows.Count ||
            columnIndex < renderedTable.RowHeaderColumnCount ||
            columnIndex >= RatingGrid.Columns.Count ||
            renderedTable.Rows[rowIndex] is not RenderedTableRow nextRow)
        {
            return;
        }

        DataGridColumn nextColumn = RatingGrid.Columns[columnIndex];
        var nextCell = new DataGridCellInfo(nextRow, nextColumn);

        RatingGrid.CurrentCell = nextCell;
        RatingGrid.SelectedCells.Clear();
        RatingGrid.SelectedCells.Add(nextCell);
        RatingGrid.ScrollIntoView(nextRow, nextColumn);
        RatingGrid.UpdateLayout();

        if (!TryFocusRatingGridCell(nextRow, nextColumn))
        {
            RatingGrid.Focus();
        }
    }

    private bool TryFocusRatingGridCell(RenderedTableRow row, DataGridColumn column)
    {
        DataGridRow? rowContainer = RatingGrid.ItemContainerGenerator.ContainerFromItem(row) as DataGridRow;

        if (rowContainer is null)
        {
            RatingGrid.ScrollIntoView(row, column);
            RatingGrid.UpdateLayout();
            rowContainer = RatingGrid.ItemContainerGenerator.ContainerFromItem(row) as DataGridRow;
        }

        if (rowContainer is null)
        {
            return false;
        }

        DataGridCellsPresenter? presenter = FindChild<DataGridCellsPresenter>(rowContainer);
        DataGridCell? cell = presenter?.ItemContainerGenerator.ContainerFromIndex(column.DisplayIndex) as DataGridCell;

        if (cell is null)
        {
            RatingGrid.ScrollIntoView(row, column);
            RatingGrid.UpdateLayout();
            presenter = FindChild<DataGridCellsPresenter>(rowContainer);
            cell = presenter?.ItemContainerGenerator.ContainerFromIndex(column.DisplayIndex) as DataGridCell;
        }

        return cell?.Focus() == true;
    }

    private bool TryGetPasteStartCell(
        RenderedTable renderedTable,
        out int rowIndex,
        out int columnIndex)
    {
        if (RatingGrid.CurrentCell.Item is RenderedTableRow currentRow &&
            RatingGrid.CurrentCell.Column is not null)
        {
            rowIndex = currentRow.RowIndex;
            columnIndex = RatingGrid.Columns.IndexOf(RatingGrid.CurrentCell.Column);
        }
        else if (TryGetFirstSelectedCell(out rowIndex, out columnIndex))
        {
            // Values are assigned by the helper.
        }
        else
        {
            rowIndex = -1;
            columnIndex = -1;
            return false;
        }

        if (rowIndex < renderedTable.HeaderRowCount)
        {
            rowIndex = renderedTable.HeaderRowCount;
        }

        if (columnIndex < renderedTable.RowHeaderColumnCount)
        {
            columnIndex = renderedTable.RowHeaderColumnCount;
        }

        return rowIndex < renderedTable.Rows.Count &&
            columnIndex < renderedTable.VisibleColumnCount;
    }

    private bool TryGetFirstSelectedCell(out int rowIndex, out int columnIndex)
    {
        rowIndex = int.MaxValue;
        columnIndex = int.MaxValue;

        foreach (DataGridCellInfo cell in RatingGrid.SelectedCells)
        {
            if (cell.Item is not RenderedTableRow row ||
                cell.Column is null)
            {
                continue;
            }

            int cellColumnIndex = RatingGrid.Columns.IndexOf(cell.Column);

            if (row.RowIndex < rowIndex ||
                row.RowIndex == rowIndex && cellColumnIndex < columnIndex)
            {
                rowIndex = row.RowIndex;
                columnIndex = cellColumnIndex;
            }
        }

        return rowIndex != int.MaxValue &&
            columnIndex != int.MaxValue;
    }

    private void ApplyClipboardRows(
        RenderedTable renderedTable,
        int startRowIndex,
        int startColumnIndex,
        List<List<string>> pastedRows)
    {
        _isApplyingClipboardPaste = true;
        _clipboardPasteChanged = false;
        int rejectedPasteCount = 0;
        string? firstPasteError = null;

        try
        {
            for (int pastedRowIndex = 0; pastedRowIndex < pastedRows.Count; pastedRowIndex++)
            {
                int targetRowIndex = startRowIndex + pastedRowIndex;

                if (targetRowIndex >= renderedTable.Rows.Count)
                {
                    break;
                }

                List<string> pastedColumns = pastedRows[pastedRowIndex];

                for (int pastedColumnIndex = 0; pastedColumnIndex < pastedColumns.Count; pastedColumnIndex++)
                {
                    int targetColumnIndex = startColumnIndex + pastedColumnIndex;

                    if (targetColumnIndex >= renderedTable.VisibleColumnCount)
                    {
                        break;
                    }

                    if (!TryNormalizeDataCellValue(
                        renderedTable,
                        targetRowIndex,
                        targetColumnIndex,
                        pastedColumns[pastedColumnIndex],
                        out string normalizedValue,
                        out string error))
                    {
                        rejectedPasteCount++;
                        firstPasteError ??= error;
                        continue;
                    }

                    renderedTable.SetCellValue(
                        targetRowIndex,
                        targetColumnIndex,
                        normalizedValue);
                }
            }
        }
        finally
        {
            _isApplyingClipboardPaste = false;
        }

        if (_clipboardPasteChanged)
        {
            _clipboardPasteChanged = false;
            FinalizeGhostChanges();
        }

        if (rejectedPasteCount > 0)
        {
            ShowInvalidCellValueMessage(
                $"{rejectedPasteCount} pasted value(s) were rejected. {firstPasteError}");
        }
    }

    private static bool TryNormalizeDataCellValue(
        RenderedTable renderedTable,
        int rowIndex,
        int columnIndex,
        string value,
        out string normalizedValue,
        out string error)
    {
        normalizedValue = value;
        error = "";

        if (rowIndex < renderedTable.HeaderRowCount ||
            columnIndex < renderedTable.RowHeaderColumnCount ||
            columnIndex >= renderedTable.VisibleColumnCount)
        {
            return true;
        }

        return TableValueFormatter.TryNormalizeValue(
            renderedTable.Table,
            value,
            out normalizedValue,
            out error);
    }

    private static void ShowInvalidCellValueMessage(string message)
    {
        MessageBox.Show(
            message,
            "Invalid Cell Value",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static bool ContainsTableBreaks(string text)
    {
        return text.Contains('\t') ||
            text.Contains('\r') ||
            text.Contains('\n');
    }

    private static List<List<string>> ParseTabDelimitedText(string text)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var currentValue = new StringBuilder();
        bool isInQuotes = false;

        for (int index = 0; index < text.Length; index++)
        {
            char value = text[index];

            if (value == '"')
            {
                if (isInQuotes &&
                    index + 1 < text.Length &&
                    text[index + 1] == '"')
                {
                    currentValue.Append('"');
                    index++;
                }
                else
                {
                    isInQuotes = !isInQuotes;
                }
            }
            else if (value == '\t' && !isInQuotes)
            {
                currentRow.Add(currentValue.ToString());
                currentValue.Clear();
            }
            else if ((value == '\r' || value == '\n') && !isInQuotes)
            {
                if (value == '\r' &&
                    index + 1 < text.Length &&
                    text[index + 1] == '\n')
                {
                    index++;
                }

                currentRow.Add(currentValue.ToString());
                currentValue.Clear();
                rows.Add(currentRow);
                currentRow = [];
            }
            else
            {
                currentValue.Append(value);
            }
        }

        currentRow.Add(currentValue.ToString());
        rows.Add(currentRow);

        while (rows.Count > 0 &&
            rows[^1].Count == 1 &&
            rows[^1][0].Length == 0)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return rows;
    }

    private void ConfigurePageTabs(RenderedTable renderedTable)
    {
        _isUpdatingPageTabs = true;

        try
        {
            PageTabs.ItemsSource = null;

            PageTabs.ItemsSource = renderedTable.HasPages
                ? renderedTable.PageNames
                : ["Default"];
            PageTabs.SelectedIndex = renderedTable.SelectedPageIndex;
            UpdatePageRowVisibility(renderedTable);
        }
        finally
        {
            _isUpdatingPageTabs = false;
        }
    }

    private void PageTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingPageTabs ||
            _currentTable?.RenderedTable is not RenderedTable updatedTable ||
            PageTabs.SelectedIndex < 0 ||
            updatedTable.SelectedPageIndex == PageTabs.SelectedIndex)
        {
            return;
        }

        updatedTable.SelectedPageIndex = PageTabs.SelectedIndex;

        if (_originalTables.ElementAtOrDefault(_currentTable.SourceIndex)?.RenderedTable is RenderedTable originalTable)
        {
            originalTable.SelectedPageIndex = PageTabs.SelectedIndex;
        }

        ScheduleGridRefresh();
    }

    private void UpdatePageRowVisibility(RenderedTable? renderedTable)
    {
        if (PageTabsRow is null || PageTabs is null)
        {
            return;
        }

        bool hasMultiplePages = renderedTable?.PageCount > 1;

        PageTabsRow.Visibility = hasMultiplePages ? Visibility.Visible : Visibility.Collapsed;
        PageTabs.Visibility = hasMultiplePages ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ViewToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingViewToggles)
        {
            return;
        }

        if (ShowCurrentToggle.IsChecked != true && ShowUpdatedToggle.IsChecked != true)
        {
            _isUpdatingViewToggles = true;

            if (ReferenceEquals(sender, ShowCurrentToggle))
            {
                ShowUpdatedToggle.IsChecked = true;
            }
            else
            {
                ShowCurrentToggle.IsChecked = true;
            }

            _isUpdatingViewToggles = false;
        }

        UpdateViewVisibility();
    }

    private void UpdateViewVisibility()
    {
        if (CurrentViewPanel is null ||
            PendingViewPanel is null ||
            PendingHeaderRow is null ||
            PendingHeaderText is null ||
            ComparisonGridSplitter is null ||
            CurrentViewColumn is null ||
            ComparisonSplitterColumn is null ||
            PendingViewColumn is null ||
            ViewTabsPanel is null ||
            ShowCurrentToggle is null ||
            ShowUpdatedToggle is null)
        {
            return;
        }

        bool hasSelection = _currentTable is not null;
        bool hasPendingChanges = hasSelection && _currentTable!.HasChanges;
        bool showCurrent = hasPendingChanges && ShowCurrentToggle.IsChecked == true;
        bool showPending = hasSelection && (!hasPendingChanges || ShowUpdatedToggle.IsChecked == true);

        ViewTabsPanel.Visibility = hasPendingChanges ? Visibility.Visible : Visibility.Collapsed;
        PendingHeaderRow.Height = hasPendingChanges ? new GridLength(24) : new GridLength(0);
        PendingHeaderText.Visibility = hasPendingChanges ? Visibility.Visible : Visibility.Collapsed;
        CurrentViewPanel.Visibility = showCurrent ? Visibility.Visible : Visibility.Collapsed;
        PendingViewPanel.Visibility = showPending ? Visibility.Visible : Visibility.Collapsed;
        ComparisonGridSplitter.Visibility = showCurrent && showPending ? Visibility.Visible : Visibility.Collapsed;

        CurrentViewColumn.Width = showCurrent ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        ComparisonSplitterColumn.Width = showCurrent && showPending ? new GridLength(6) : new GridLength(0);
        PendingViewColumn.Width = showPending ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        Dispatcher.BeginInvoke(
            () => SynchronizeCurrentScrollToPending(),
            DispatcherPriority.Background);
    }

    private void ViewFileXml_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetLoadedDocument(out XDocument ghostDocument))
        {
            return;
        }

        var editor = new XmlEditorWindow("File XML", GetDocumentText(ghostDocument))
        {
            Owner = this,
            ValidateAndUpdate = UpdateWholeFileXml
        };

        editor.ShowDialog();
    }

    private void ViewTableXml_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetLoadedDocument(out XDocument ghostDocument) ||
            TablesList.SelectedItem is not TableDefinition table)
        {
            return;
        }

        XElement? tableElement = GetTableElement(ghostDocument, table);

        if (tableElement is null)
        {
            ShowTableNotFoundMessage();
            return;
        }

        var editor = new XmlEditorWindow($"Table XML - {table.Name}", tableElement.ToString())
        {
            Owner = this,
            ValidateAndUpdate = xml => UpdateTableXml(table, xml)
        };

        editor.ShowDialog();
    }

    private void TableProperties_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetLoadedDocument(out XDocument ghostDocument) ||
            TablesList.SelectedItem is not TableDefinition table)
        {
            return;
        }

        XElement? tableElement = GetTableElement(ghostDocument, table);

        if (tableElement is null)
        {
            ShowTableNotFoundMessage();
            return;
        }

        var propertiesWindow = new TablePropertiesWindow(table)
        {
            Owner = this
        };

        if (propertiesWindow.ShowDialog() != true || propertiesWindow.Result is null)
        {
            return;
        }

        ApplyTableProperties(table, tableElement, propertiesWindow.Result);
    }

    private void ApplyTableProperties(
        TableDefinition table,
        XElement tableElement,
        TablePropertiesResult properties)
    {
        TableDataSnapshot oldSnapshot = SnapshotTable(tableElement);

        tableElement.SetAttributeValue("name", properties.TableName);
        tableElement.Element("comment")?.Remove();
        tableElement.SetAttributeValue(
            "comment",
            string.IsNullOrWhiteSpace(properties.Comment) ? null : properties.Comment);
        tableElement.SetAttributeValue(
            "dataType",
            TableValueFormatter.IsDefaultDataType(properties.DataType) ? null : properties.DataType);
        tableElement.SetAttributeValue(
            "decimals",
            TableValueFormatter.SupportsDecimals(properties.DataType) && properties.Decimals is int decimals
                ? decimals.ToString(CultureInfo.InvariantCulture)
                : null);
        ReplaceKeySets(tableElement, "rowKeys", "rowSet", properties.RowSets);
        ReplaceKeySets(tableElement, "colKeys", "colSet", properties.ColumnSets);
        ReplacePageKeys(tableElement, properties.PageSearchType, properties.Pages);
        NormalizeTableData(tableElement, oldSnapshot, BuildStructureMap(properties));
        ReloadGhostState(table.SourceIndex);
    }

    private void RenameTable_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetLoadedDocument(out XDocument ghostDocument) ||
            TablesList.SelectedItem is not TableDefinition table)
        {
            return;
        }

        var renameWindow = new RenameTableWindow(table.Name)
        {
            Owner = this
        };

        if (renameWindow.ShowDialog() != true || renameWindow.TableName == table.Name)
        {
            return;
        }

        XElement? tableElement = GetTableElement(ghostDocument, table);

        if (tableElement is null)
        {
            ShowTableNotFoundMessage();
            return;
        }

        tableElement.SetAttributeValue("name", renameWindow.TableName);
        ReloadGhostState(table.SourceIndex);
    }

    private void AddTable_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetLoadedDocument(out XDocument ghostDocument))
        {
            return;
        }

        string defaultName = GetUniqueTableName("NewTable");
        XElement root = ghostDocument.Root ?? new XElement("tables");

        if (ghostDocument.Root is null)
        {
            ghostDocument.Add(root);
        }

        XElement tableElement = CreateDefaultTableElement(defaultName);
        root.Add(tableElement);
        int tableIndex = root.Elements("table").Count() - 1;
        TableDefinition table = TableXmlParser.LoadDocument(new XDocument(new XElement("tables", new XElement(tableElement))))[0];
        table.SourceIndex = tableIndex;

        var propertiesWindow = new TablePropertiesWindow(table)
        {
            Owner = this
        };

        if (propertiesWindow.ShowDialog() != true ||
            propertiesWindow.Result is null)
        {
            tableElement.Remove();
            ReloadGhostState(Math.Max(0, tableIndex - 1));
            return;
        }

        ApplyTableProperties(table, tableElement, propertiesWindow.Result);
    }

    private void RemoveTable_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedGhostTable(out TableDefinition table, out XElement tableElement))
        {
            return;
        }

        if (!Confirm($"Remove table '{table.Name}'?"))
        {
            return;
        }

        int nextSelectedIndex = Math.Max(0, table.SourceIndex - 1);
        tableElement.Remove();
        ReloadGhostState(nextSelectedIndex);
    }

    private void AddRowSet_Click(object sender, RoutedEventArgs e)
    {
        AddKeySet("rowKeys", "rowSet", "row set", "RowSet");
    }

    private void RemoveRowSet_Click(object sender, RoutedEventArgs e)
    {
        RemoveKeySet("rowKeys", "rowSet", "row set");
    }

    private void AddColumnSet_Click(object sender, RoutedEventArgs e)
    {
        AddKeySet("colKeys", "colSet", "column set", "ColSet");
    }

    private void RemoveColumnSet_Click(object sender, RoutedEventArgs e)
    {
        RemoveKeySet("colKeys", "colSet", "column set");
    }

    private void AddRowHeader_Click(object sender, RoutedEventArgs e)
    {
        AddHeaderKey("rowKeys", "rowSet", "row header");
    }

    private void RemoveRowHeader_Click(object sender, RoutedEventArgs e)
    {
        RemoveHeaderKey("rowKeys", "rowSet", "row header");
    }

    private void AddColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        AddHeaderKey("colKeys", "colSet", "column header");
    }

    private void RemoveColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        RemoveHeaderKey("colKeys", "colSet", "column header");
    }

    private void AddRowSetButton_Click(object sender, RoutedEventArgs e)
    {
        AddKeySet("rowKeys", "rowSet", "row set", "RowSet");
    }

    private void RemoveRowSetButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSelectedRowSetIndex(out int setIndex))
        {
            RemoveKeySetAt("rowKeys", "rowSet", "row set", setIndex);
            return;
        }

        RemoveKeySet("rowKeys", "rowSet", "row set");
    }

    private void AddRowHeaderButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSelectedRowSetIndex(out int setIndex))
        {
            AddHeaderKeyAt("rowKeys", "rowSet", "row header", setIndex);
            return;
        }

        AddHeaderKey("rowKeys", "rowSet", "row header");
    }

    private void RemoveRowHeaderButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSelectedHeaderTarget(TableHeaderTargetKind.RowHeader, out TableHeaderTarget target))
        {
            RemoveHeaderKeyAt("rowKeys", "rowSet", "row header", target.SetIndex, target.KeyIndex);
            return;
        }

        RemoveHeaderKey("rowKeys", "rowSet", "row header");
    }

    private void AddColumnSetButton_Click(object sender, RoutedEventArgs e)
    {
        AddKeySet("colKeys", "colSet", "column set", "ColSet");
    }

    private void RemoveColumnSetButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSelectedColumnSetIndex(out int setIndex))
        {
            RemoveKeySetAt("colKeys", "colSet", "column set", setIndex);
            return;
        }

        RemoveKeySet("colKeys", "colSet", "column set");
    }

    private void AddColumnHeaderButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSelectedColumnSetIndex(out int setIndex))
        {
            AddHeaderKeyAt("colKeys", "colSet", "column header", setIndex);
            return;
        }

        AddHeaderKey("colKeys", "colSet", "column header");
    }

    private void RemoveColumnHeaderButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetSelectedHeaderTarget(TableHeaderTargetKind.ColumnHeader, out TableHeaderTarget target))
        {
            RemoveHeaderKeyAt("colKeys", "colSet", "column header", target.SetIndex, target.KeyIndex);
            return;
        }

        RemoveHeaderKey("colKeys", "colSet", "column header");
    }

    private void RemoveSelectedPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTable?.RenderedTable?.HasPages == true && PageTabs.SelectedIndex >= 0)
        {
            RemovePageAt(PageTabs.SelectedIndex);
            return;
        }

        RemovePage_Click(sender, e);
    }

    private void AddPage_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedGhostTable(out TableDefinition table, out XElement tableElement))
        {
            return;
        }

        if (!TryPromptText(
            "Add Page",
            "Page name",
            GetUniqueKeyName(table.PageKeys, "Page"),
            "Add",
            "Page name cannot be blank.",
            out string pageName))
        {
            return;
        }

        TableDataSnapshot oldSnapshot = SnapshotTable(tableElement);
        XElement pageKeysElement = EnsurePageKeysElement(tableElement);
        pageKeysElement.Add(new XElement("key", pageName));
        NormalizeTableData(tableElement, oldSnapshot);
        ReloadGhostState(table.SourceIndex);
    }

    private void RemovePage_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedGhostTable(out TableDefinition table, out XElement tableElement))
        {
            return;
        }

        XElement? pageKeysElement = tableElement.Element("pageKeys");
        List<XElement> pageKeyElements = pageKeysElement?.Elements("key").ToList() ?? [];

        if (pageKeyElements.Count == 0)
        {
            ShowInformation("This table does not have pages to remove.");
            return;
        }

        if (!TryPromptChoice(
            "Remove Page",
            "Choose a page to remove",
            pageKeyElements.Select(x => x.Value.Trim()).ToList(),
            out int pageIndex))
        {
            return;
        }

        string pageName = pageKeyElements[pageIndex].Value.Trim();

        if (!Confirm($"Remove page '{pageName}'?"))
        {
            return;
        }

        TableDataSnapshot oldSnapshot = SnapshotTable(tableElement);
        pageKeyElements[pageIndex].Remove();

        if (!pageKeysElement!.Elements("key").Any())
        {
            pageKeysElement.Remove();
        }

        NormalizeTableData(tableElement, oldSnapshot);
        ReloadGhostState(table.SourceIndex);
    }

    private void EditPageAt(int pageIndex)
    {
        if (!TryGetSelectedGhostTable(out TableDefinition table, out XElement tableElement))
        {
            return;
        }

        List<XElement> pageKeyElements = tableElement
            .Element("pageKeys")?
            .Elements("key")
            .ToList() ?? [];

        if (pageIndex < 0 || pageIndex >= pageKeyElements.Count)
        {
            ShowInformation("The selected page was not found.");
            return;
        }

        string pageName = pageKeyElements[pageIndex].Value.Trim();

        if (!TryPromptText(
            "Edit Page",
            "Page name",
            pageName,
            "Update",
            "Page name cannot be blank.",
            out string newPageName))
        {
            return;
        }

        pageKeyElements[pageIndex].Value = newPageName;
        ReloadGhostState(table.SourceIndex);
    }

    private void RemovePageAt(int pageIndex)
    {
        if (!TryGetSelectedGhostTable(out TableDefinition table, out XElement tableElement))
        {
            return;
        }

        XElement? pageKeysElement = tableElement.Element("pageKeys");
        List<XElement> pageKeyElements = pageKeysElement?.Elements("key").ToList() ?? [];

        if (pageIndex < 0 || pageIndex >= pageKeyElements.Count)
        {
            ShowInformation("The selected page was not found.");
            return;
        }

        string pageName = pageKeyElements[pageIndex].Value.Trim();

        if (!Confirm($"Delete page '{pageName}'?"))
        {
            return;
        }

        TableDataSnapshot oldSnapshot = SnapshotTable(tableElement);
        pageKeyElements[pageIndex].Remove();

        if (!pageKeysElement!.Elements("key").Any())
        {
            pageKeysElement.Remove();
        }

        NormalizeTableData(tableElement, oldSnapshot);
        ReloadGhostState(table.SourceIndex);
    }

    private void EditPages_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedGhostTable(out TableDefinition table, out XElement tableElement))
        {
            return;
        }

        XElement? pageKeysElement = tableElement.Element("pageKeys");
        List<XElement> pageKeyElements = pageKeysElement?.Elements("key").ToList() ?? [];

        if (pageKeyElements.Count == 0)
        {
            ShowInformation("This table does not have pages to edit.");
            return;
        }

        var editor = new SetEditorWindow(
            "Edit Pages",
            "",
            pageKeyElements.Select(x => x.Value.Trim()).ToList(),
            "Pages",
            "Update",
            showName: false)
        {
            Owner = this
        };

        if (editor.ShowDialog() != true)
        {
            return;
        }

        if (editor.Values.Count != pageKeyElements.Count)
        {
            ShowInformation("Use Add Page or Delete Page to change the number of pages.");
            return;
        }

        for (int index = 0; index < pageKeyElements.Count; index++)
        {
            pageKeyElements[index].Value = editor.Values[index];
        }

        ReloadGhostState(table.SourceIndex);
    }

    private void AddKeySet(
        string keysElementName,
        string setElementName,
        string label,
        string defaultNamePrefix)
    {
        if (!TryGetSelectedGhostTable(out TableDefinition table, out XElement tableElement))
        {
            return;
        }

        if (!TryPromptText(
            $"Add {ToTitleCase(label)}",
            $"{ToTitleCase(label)} name",
            GetUniqueSetName(tableElement.Element(keysElementName), setElementName, defaultNamePrefix),
            "Add",
            $"{ToTitleCase(label)} name cannot be blank.",
            out string setName))
        {
            return;
        }

        if (!TryPromptText(
            $"Add {ToTitleCase(label)}",
            "First header value",
            "Default",
            "Add",
            "Header value cannot be blank.",
            out string firstKey))
        {
            return;
        }

        TableDataSnapshot oldSnapshot = SnapshotTable(tableElement);
        XElement keysElement = EnsureKeysElement(tableElement, keysElementName);
        keysElement.Add(
            new XElement(
                setElementName,
                new XAttribute("name", setName),
                new XAttribute("searchType", "eq"),
                new XElement("key", firstKey)));

        NormalizeTableData(tableElement, oldSnapshot);
        ReloadGhostState(table.SourceIndex);
    }

    private void EditKeySet(
        string keysElementName,
        string setElementName,
        string label,
        int setIndex)
    {
        if (!TryGetSelectedGhostTable(out TableDefinition table, out XElement tableElement))
        {
            return;
        }

        List<XElement> setElements = tableElement
            .Element(keysElementName)?
            .Elements(setElementName)
            .ToList() ?? [];

        if (setIndex < 0 || setIndex >= setElements.Count)
        {
            ShowInformation($"The selected {label} was not found.");
            return;
        }

        XElement setElement = setElements[setIndex];
        string originalName = setElement.Attribute("name")?.Value ?? "";
        List<string> originalKeys = setElement.Elements("key").Select(x => x.Value.Trim()).ToList();

        var editor = new SetEditorWindow(
            $"Edit {ToTitleCase(label)}",
            originalName,
            originalKeys,
            "Headers")
        {
            Owner = this
        };

        if (editor.ShowDialog() != true)
        {
            return;
        }

        List<string> editedKeys = editor.Values;

        if (editedKeys.SequenceEqual(originalKeys, StringComparer.Ordinal))
        {
            setElement.SetAttributeValue("name", editor.SetName);
            ReloadGhostState(table.SourceIndex);
            return;
        }

        TableDataSnapshot oldSnapshot = SnapshotTable(tableElement);
        setElement.RemoveNodes();

        foreach (string key in editedKeys)
        {
            setElement.Add(new XElement("key", key));
        }

        NormalizeTableData(tableElement, oldSnapshot);
        setElement.SetAttributeValue("name", editor.SetName);
        ReloadGhostState(table.SourceIndex);
    }

    private void RemoveKeySet(string keysElementName, string setElementName, string label)
    {
        if (!TryGetSelectedGhostTable(out TableDefinition table, out XElement tableElement))
        {
            return;
        }

        XElement? keysElement = tableElement.Element(keysElementName);
        List<XElement> setElements = keysElement?.Elements(setElementName).ToList() ?? [];

        if (setElements.Count == 0)
        {
            ShowInformation($"This table does not have a {label} to remove.");
            return;
        }

        if (!TryPromptChoice(
            $"Remove {ToTitleCase(label)}",
            $"Choose a {label} to remove",
            setElements.Select(FormatSetChoice).ToList(),
            out int setIndex))
        {
            return;
        }

        string setName = setElements[setIndex].Attribute("name")?.Value ?? label;

        if (!Confirm($"Remove {label} '{setName}'?"))
        {
            return;
        }

        TableDataSnapshot oldSnapshot = SnapshotTable(tableElement);
        setElements[setIndex].Remove();

        if (!keysElement!.Elements(setElementName).Any())
        {
            keysElement.Remove();
        }

        NormalizeTableData(tableElement, oldSnapshot);
        ReloadGhostState(table.SourceIndex);
    }

    private void RemoveKeySetAt(
        string keysElementName,
        string setElementName,
        string label,
        int setIndex)
    {
        if (!TryGetSelectedGhostTable(out TableDefinition table, out XElement tableElement))
        {
            return;
        }

        XElement? keysElement = tableElement.Element(keysElementName);
        List<XElement> setElements = keysElement?.Elements(setElementName).ToList() ?? [];

        if (setIndex < 0 || setIndex >= setElements.Count)
        {
            ShowInformation($"The selected {label} was not found.");
            return;
        }

        string setName = setElements[setIndex].Attribute("name")?.Value ?? label;

        if (!Confirm($"Delete {label} '{setName}'?"))
        {
            return;
        }

        TableDataSnapshot oldSnapshot = SnapshotTable(tableElement);
        setElements[setIndex].Remove();

        if (!keysElement!.Elements(setElementName).Any())
        {
            keysElement.Remove();
        }

        NormalizeTableData(tableElement, oldSnapshot);
        ReloadGhostState(table.SourceIndex);
    }

    private void AddHeaderKey(string keysElementName, string setElementName, string label)
    {
        if (!TryGetSelectedGhostTable(out TableDefinition table, out XElement tableElement))
        {
            return;
        }

        XElement? keysElement = tableElement.Element(keysElementName);
        List<XElement> setElements = keysElement?.Elements(setElementName).ToList() ?? [];

        if (setElements.Count == 0)
        {
            ShowInformation($"Add a {setElementName} before adding a {label}.");
            return;
        }

        if (!TryPromptChoice(
            $"Add {ToTitleCase(label)}",
            "Choose the set that should receive the new header",
            setElements.Select(FormatSetChoice).ToList(),
            out int setIndex))
        {
            return;
        }

        XElement selectedSet = setElements[setIndex];
        List<string> existingKeys = selectedSet.Elements("key").Select(x => x.Value.Trim()).ToList();

        if (!TryPromptText(
            $"Add {ToTitleCase(label)}",
            "Header value",
            GetUniqueKeyName(existingKeys, "Header"),
            "Add",
            "Header value cannot be blank.",
            out string keyName))
        {
            return;
        }

        TableDataSnapshot oldSnapshot = SnapshotTable(tableElement);
        selectedSet.Add(new XElement("key", keyName));
        NormalizeTableData(tableElement, oldSnapshot);
        ReloadGhostState(table.SourceIndex);
    }

    private void RemoveHeaderKey(string keysElementName, string setElementName, string label)
    {
        if (!TryGetSelectedGhostTable(out TableDefinition table, out XElement tableElement))
        {
            return;
        }

        XElement? keysElement = tableElement.Element(keysElementName);
        List<XElement> setElements = keysElement?.Elements(setElementName).ToList() ?? [];

        if (setElements.Count == 0)
        {
            ShowInformation($"This table does not have a {label} to remove.");
            return;
        }

        if (!TryPromptChoice(
            $"Remove {ToTitleCase(label)}",
            "Choose the set that contains the header",
            setElements.Select(FormatSetChoice).ToList(),
            out int setIndex))
        {
            return;
        }

        XElement selectedSet = setElements[setIndex];
        List<XElement> keyElements = selectedSet.Elements("key").ToList();

        if (keyElements.Count <= 1)
        {
            ShowInformation("A set must keep at least one header.");
            return;
        }

        if (!TryPromptChoice(
            $"Remove {ToTitleCase(label)}",
            "Choose the header to remove",
            keyElements.Select(x => x.Value.Trim()).ToList(),
            out int keyIndex))
        {
            return;
        }

        string keyName = keyElements[keyIndex].Value.Trim();

        if (!Confirm($"Remove {label} '{keyName}'?"))
        {
            return;
        }

        TableDataSnapshot oldSnapshot = SnapshotTable(tableElement);
        keyElements[keyIndex].Remove();
        NormalizeTableData(tableElement, oldSnapshot);
        ReloadGhostState(table.SourceIndex);
    }

    private void AddHeaderKeyAt(
        string keysElementName,
        string setElementName,
        string label,
        int setIndex)
    {
        if (!TryGetSelectedGhostTable(out TableDefinition table, out XElement tableElement))
        {
            return;
        }

        List<XElement> setElements = tableElement
            .Element(keysElementName)?
            .Elements(setElementName)
            .ToList() ?? [];

        if (setIndex < 0 || setIndex >= setElements.Count)
        {
            ShowInformation($"The selected {label} set was not found.");
            return;
        }

        XElement selectedSet = setElements[setIndex];
        List<string> existingKeys = selectedSet.Elements("key").Select(x => x.Value.Trim()).ToList();

        if (!TryPromptText(
            $"Add {ToTitleCase(label)}",
            "Header value",
            GetUniqueKeyName(existingKeys, "Header"),
            "Add",
            "Header value cannot be blank.",
            out string keyName))
        {
            return;
        }

        TableDataSnapshot oldSnapshot = SnapshotTable(tableElement);
        selectedSet.Add(new XElement("key", keyName));
        NormalizeTableData(tableElement, oldSnapshot);
        ReloadGhostState(table.SourceIndex);
    }

    private void RemoveHeaderKeyAt(
        string keysElementName,
        string setElementName,
        string label,
        int setIndex,
        int keyIndex)
    {
        if (!TryGetSelectedGhostTable(out TableDefinition table, out XElement tableElement))
        {
            return;
        }

        List<XElement> setElements = tableElement
            .Element(keysElementName)?
            .Elements(setElementName)
            .ToList() ?? [];

        if (setIndex < 0 || setIndex >= setElements.Count)
        {
            ShowInformation($"The selected {label} set was not found.");
            return;
        }

        XElement selectedSet = setElements[setIndex];
        List<XElement> keyElements = selectedSet.Elements("key").ToList();

        if (keyElements.Count <= 1)
        {
            ShowInformation("A set must keep at least one header.");
            return;
        }

        if (keyIndex < 0 || keyIndex >= keyElements.Count)
        {
            ShowInformation($"The selected {label} was not found.");
            return;
        }

        string keyName = keyElements[keyIndex].Value.Trim();

        if (!Confirm($"Delete {label} '{keyName}'?"))
        {
            return;
        }

        TableDataSnapshot oldSnapshot = SnapshotTable(tableElement);
        keyElements[keyIndex].Remove();
        NormalizeTableData(tableElement, oldSnapshot);
        ReloadGhostState(table.SourceIndex);
    }

    private bool TryGetSelectedGhostTable(out TableDefinition table, out XElement tableElement)
    {
        tableElement = new XElement("table");

        if (!TryGetLoadedDocument(out XDocument ghostDocument) ||
            TablesList.SelectedItem is not TableDefinition selectedTable)
        {
            table = new TableDefinition();
            return false;
        }

        XElement? selectedElement = GetTableElement(ghostDocument, selectedTable);

        if (selectedElement is null)
        {
            table = selectedTable;
            ShowTableNotFoundMessage();
            return false;
        }

        table = selectedTable;
        tableElement = selectedElement;
        return true;
    }

    private bool TryGetCurrentGridCell(
        out RenderedTable renderedTable,
        out int rowIndex,
        out int columnIndex)
    {
        renderedTable = null!;
        rowIndex = -1;
        columnIndex = -1;

        if (_currentTable?.RenderedTable is not RenderedTable currentRenderedTable)
        {
            return false;
        }

        if (RatingGrid.CurrentCell.Item is RenderedTableRow currentRow &&
            RatingGrid.CurrentCell.Column is not null)
        {
            renderedTable = currentRenderedTable;
            rowIndex = currentRow.RowIndex;
            columnIndex = RatingGrid.Columns.IndexOf(RatingGrid.CurrentCell.Column);
            return columnIndex >= 0;
        }

        if (TryGetFirstSelectedCell(out rowIndex, out columnIndex))
        {
            renderedTable = currentRenderedTable;
            return true;
        }

        return false;
    }

    private bool TryGetSelectedHeaderTarget(
        TableHeaderTargetKind kind,
        out TableHeaderTarget target)
    {
        target = new TableHeaderTarget(kind, -1, -1);

        return TryGetCurrentGridCell(out RenderedTable renderedTable, out int rowIndex, out int columnIndex) &&
            renderedTable.TryGetHeaderTarget(rowIndex, columnIndex, out target) &&
            target.Kind == kind;
    }

    private bool TryGetSelectedRowSetIndex(out int setIndex)
    {
        setIndex = -1;

        if (!TryGetCurrentGridCell(out RenderedTable renderedTable, out int rowIndex, out int columnIndex))
        {
            return false;
        }

        if (columnIndex >= 0 && columnIndex < renderedTable.RowHeaderColumnCount)
        {
            setIndex = columnIndex;
            return true;
        }

        if (renderedTable.TryGetHeaderTarget(rowIndex, columnIndex, out TableHeaderTarget target) &&
            target.Kind == TableHeaderTargetKind.RowSetName)
        {
            setIndex = target.SetIndex;
            return true;
        }

        return false;
    }

    private bool TryGetSelectedColumnSetIndex(out int setIndex)
    {
        setIndex = -1;

        if (!TryGetCurrentGridCell(out RenderedTable renderedTable, out int rowIndex, out int columnIndex))
        {
            return false;
        }

        if (rowIndex >= 0 && rowIndex < renderedTable.Table.ColSets.Count)
        {
            setIndex = rowIndex;
            return true;
        }

        if (renderedTable.TryGetHeaderTarget(rowIndex, columnIndex, out TableHeaderTarget target) &&
            target.Kind == TableHeaderTargetKind.ColumnSetName)
        {
            setIndex = target.SetIndex;
            return true;
        }

        return false;
    }

    private bool TryPromptText(
        string title,
        string prompt,
        string initialValue,
        string actionText,
        string blankErrorMessage,
        out string value)
    {
        var promptWindow = new RenameTableWindow(
            initialValue,
            title,
            prompt,
            actionText,
            blankErrorMessage)
        {
            Owner = this
        };

        if (promptWindow.ShowDialog() == true)
        {
            value = promptWindow.InputValue;
            return true;
        }

        value = "";
        return false;
    }

    private bool TryPromptChoice(
        string title,
        string prompt,
        IReadOnlyList<string> choices,
        out int selectedIndex)
    {
        if (choices.Count == 0)
        {
            selectedIndex = -1;
            return false;
        }

        var choiceWindow = new ChoicePromptWindow(title, prompt, choices)
        {
            Owner = this
        };

        if (choiceWindow.ShowDialog() == true)
        {
            selectedIndex = choiceWindow.SelectedIndex;
            return true;
        }

        selectedIndex = -1;
        return false;
    }

    private static bool Confirm(string message)
    {
        return MessageBox.Show(
            message,
            "Confirm",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private static void ShowInformation(string message)
    {
        MessageBox.Show(
            message,
            "Table Editor",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private string GetUniqueTableName(string baseName)
    {
        HashSet<string> tableNames = _tables
            .Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return GetUniqueName(tableNames, baseName);
    }

    private static string GetUniqueSetName(
        XElement? keysElement,
        string setElementName,
        string baseName)
    {
        HashSet<string> setNames = keysElement?
            .Elements(setElementName)
            .Select(x => x.Attribute("name")?.Value ?? "")
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        return GetUniqueName(setNames, baseName);
    }

    private static string GetUniqueKeyName(IEnumerable<string> existingKeys, string baseName)
    {
        return GetUniqueName(
            existingKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            baseName);
    }

    private static string GetUniqueName(HashSet<string> existingNames, string baseName)
    {
        if (!existingNames.Contains(baseName))
        {
            return baseName;
        }

        for (int index = 2; ; index++)
        {
            string candidate = $"{baseName}{index}";

            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static string FormatSetChoice(XElement setElement)
    {
        string name = setElement.Attribute("name")?.Value ?? "";
        int keyCount = setElement.Elements("key").Count();
        return $"{name} ({keyCount} headers)";
    }

    private static string ToTitleCase(string value)
    {
        return string.Join(
            " ",
            value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }

    private static XElement CreateDefaultTableElement(string tableName)
    {
        return new XElement(
            "table",
            new XAttribute("name", tableName),
            new XAttribute("delimiter", ","),
            new XElement(
                "rowKeys",
                new XElement(
                    "rowSet",
                    new XAttribute("name", "RowSet"),
                    new XAttribute("searchType", "eq"),
                    new XElement("key", "Default"))),
            new XElement(
                "colKeys",
                new XElement(
                    "colSet",
                    new XAttribute("name", "ColumnSet"),
                    new XAttribute("searchType", "eq"),
                    new XElement("key", "Default"))),
            new XElement(
                "data",
                new XElement("row", "")));
    }

    private static XElement EnsureKeysElement(XElement tableElement, string keysElementName)
    {
        XElement? existingElement = tableElement.Element(keysElementName);

        if (existingElement is not null)
        {
            return existingElement;
        }

        var keysElement = new XElement(keysElementName);
        XElement? dataElement = tableElement.Element("data");

        if (keysElementName == "rowKeys")
        {
            tableElement.AddFirst(keysElement);
        }
        else if (dataElement is not null)
        {
            dataElement.AddBeforeSelf(keysElement);
        }
        else
        {
            tableElement.Add(keysElement);
        }

        return keysElement;
    }

    private static XElement EnsurePageKeysElement(XElement tableElement)
    {
        XElement? existingElement = tableElement.Element("pageKeys");

        if (existingElement is not null)
        {
            return existingElement;
        }

        var pageKeysElement = new XElement("pageKeys", new XAttribute("searchType", "eq"));
        XElement? dataElement = tableElement.Element("data");

        if (dataElement is not null)
        {
            dataElement.AddBeforeSelf(pageKeysElement);
        }
        else
        {
            tableElement.Add(pageKeysElement);
        }

        return pageKeysElement;
    }

    private static void ReplaceKeySets(
        XElement tableElement,
        string keysElementName,
        string setElementName,
        IReadOnlyList<TablePropertiesKeySet> sets)
    {
        XElement? keysElement = tableElement.Element(keysElementName);

        if (sets.Count == 0)
        {
            keysElement?.Remove();
            return;
        }

        keysElement = EnsureKeysElement(tableElement, keysElementName);
        keysElement.RemoveNodes();

        foreach (TablePropertiesKeySet set in sets)
        {
            keysElement.Add(
                new XElement(
                    setElementName,
                    new XAttribute("name", set.Name),
                    new XAttribute("searchType", ToSearchTypeToken(set.SearchType)),
                    set.Keys.Select(key => new XElement("key", key.Value))));
        }
    }

    private static void ReplacePageKeys(
        XElement tableElement,
        string pageSearchType,
        IReadOnlyList<TablePropertiesKey> pages)
    {
        XElement? pageKeysElement = tableElement.Element("pageKeys");

        if (pages.Count == 0)
        {
            pageKeysElement?.Remove();
            return;
        }

        pageKeysElement = EnsurePageKeysElement(tableElement);
        pageKeysElement.SetAttributeValue("searchType", ToSearchTypeToken(pageSearchType));
        pageKeysElement.RemoveNodes();

        foreach (TablePropertiesKey page in pages)
        {
            pageKeysElement.Add(new XElement("key", page.Value));
        }
    }

    private static string ToSearchTypeToken(string? searchType)
    {
        return searchType switch
        {
            "=" => "eq",
            "<" => "lt",
            "<=" => "lte",
            ">" => "gt",
            ">=" => "gte",
            "Range" => "range",
            "Interpolate" => "interpolate",
            "Graduated" => "graduated",
            _ => "eq"
        };
    }

    private static TableStructureMap BuildStructureMap(TablePropertiesResult properties)
    {
        return new TableStructureMap(
            BuildDimensionStructureMap(properties.RowSets),
            BuildDimensionStructureMap(properties.ColumnSets),
            properties.Pages.Select(page => page.OriginalIndex).ToArray());
    }

    private static DimensionStructureMap BuildDimensionStructureMap(
        IReadOnlyList<TablePropertiesKeySet> sets)
    {
        return new DimensionStructureMap(
            sets.Select(set => set.OriginalIndex).ToArray(),
            sets
                .Select(set => set.Keys.Select(key => key.OriginalIndex).ToArray())
                .ToArray());
    }

    private string? UpdateWholeFileXml(string xml)
    {
        if (!TryParseDocument(xml, out XDocument? document, out string? error))
        {
            return error;
        }

        try
        {
            TableXmlParser.LoadDocument(document!);
        }
        catch (Exception ex)
        {
            return $"XML is well-formed, but the table file could not be loaded: {ex.Message}";
        }

        _ghostDocument = document;
        ReloadGhostState(TablesList.SelectedIndex);
        return null;
    }

    private string? UpdateTableXml(TableDefinition table, string xml)
    {
        if (!TryParseTableElement(xml, out XElement? updatedTable, out string? error))
        {
            return error;
        }

        XDocument testDocument = new(new XElement("tables", new XElement(updatedTable!)));

        try
        {
            TableXmlParser.LoadDocument(testDocument);
        }
        catch (Exception ex)
        {
            return $"XML is well-formed, but this table could not be loaded: {ex.Message}";
        }

        if (_ghostDocument is null)
        {
            return "No working XML document is loaded.";
        }

        XElement? tableElement = GetTableElement(_ghostDocument, table);

        if (tableElement is null)
        {
            return "The selected table was not found in the working XML file.";
        }

        tableElement.ReplaceWith(updatedTable);
        ReloadGhostState(table.SourceIndex);
        return null;
    }

    private void ReloadGhostState(int selectedIndex)
    {
        PersistGhostDocument();
        LoadGhostTables(selectedIndex);
    }

    private void UpdateGhostDataCell(
        TableDefinition table,
        int dataRowIndex,
        int valueColumnIndex,
        string value)
    {
        if (_ghostDocument is null)
        {
            return;
        }

        XElement? tableElement = GetTableElement(_ghostDocument, table);

        if (tableElement is null)
        {
            return;
        }

        XElement dataElement = tableElement.Element("data")
            ?? AddDataElement(tableElement);

        List<XElement> rowElements = dataElement.Elements("row").ToList();

        while (rowElements.Count <= dataRowIndex)
        {
            var rowElement = new XElement("row", "");
            dataElement.Add(rowElement);
            rowElements.Add(rowElement);
        }

        XElement targetRow = rowElements[dataRowIndex];
        List<string> values = targetRow.Value
            .Split(table.Delimiter)
            .Select(x => x.Trim())
            .ToList();

        while (values.Count <= valueColumnIndex)
        {
            values.Add("");
        }

        values[valueColumnIndex] = value;
        targetRow.Value = string.Join(table.Delimiter, values);
    }

    private static void ApplyHeaderValueToGhost(
        XElement tableElement,
        TableHeaderChangedEventArgs e)
    {
        TableDataSnapshot? oldSnapshot = e.Kind is TableHeaderTargetKind.RowHeader or TableHeaderTargetKind.ColumnHeader
            ? SnapshotTable(tableElement)
            : null;

        string keysElementName = e.Kind is TableHeaderTargetKind.RowSetName or TableHeaderTargetKind.RowHeader
            ? "rowKeys"
            : "colKeys";
        string setElementName = e.Kind is TableHeaderTargetKind.RowSetName or TableHeaderTargetKind.RowHeader
            ? "rowSet"
            : "colSet";

        XElement? setElement = tableElement
            .Element(keysElementName)?
            .Elements(setElementName)
            .ElementAtOrDefault(e.SetIndex);

        if (setElement is null)
        {
            return;
        }

        if (e.Kind is TableHeaderTargetKind.RowSetName or TableHeaderTargetKind.ColumnSetName)
        {
            setElement.SetAttributeValue("name", e.Value);
        }
        else
        {
            XElement? keyElement = setElement.Elements("key").ElementAtOrDefault(e.KeyIndex);

            if (keyElement is null)
            {
                return;
            }

            keyElement.Value = e.Value;
        }

        if (oldSnapshot is not null)
        {
            NormalizeTableData(tableElement, oldSnapshot);
        }
    }

    private static XElement AddDataElement(XElement tableElement)
    {
        var dataElement = new XElement("data");
        tableElement.Add(dataElement);
        return dataElement;
    }

    private static void NormalizeTableData(
        XElement tableElement,
        TableDataSnapshot oldSnapshot,
        TableStructureMap? structureMap = null)
    {
        TableDataSnapshot newSnapshot = SnapshotTable(tableElement);
        List<Dictionary<int, int>> oldRowCombos = BuildCombinations(oldSnapshot.RowSets);
        List<Dictionary<int, int>> newRowCombos = BuildCombinations(newSnapshot.RowSets);
        List<Dictionary<int, int>> oldColCombos = BuildCombinations(oldSnapshot.ColSets);
        List<Dictionary<int, int>> newColCombos = BuildCombinations(newSnapshot.ColSets);

        int[] pageMap = IsValidPageMap(structureMap?.PageMap, oldSnapshot, newSnapshot)
            ? structureMap!.PageMap
            : BuildPageMap(oldSnapshot, newSnapshot);
        int[] rowMap = BuildCombinationMap(
            oldSnapshot.RowSets,
            newSnapshot.RowSets,
            oldRowCombos,
            newRowCombos,
            structureMap?.RowMap);
        int[] colMap = BuildCombinationMap(
            oldSnapshot.ColSets,
            newSnapshot.ColSets,
            oldColCombos,
            newColCombos,
            structureMap?.ColumnMap);

        XElement dataElement = tableElement.Element("data") ?? AddDataElement(tableElement);
        dataElement.RemoveNodes();

        for (int pageIndex = 0; pageIndex < newSnapshot.Pages.Count; pageIndex++)
        {
            for (int rowIndex = 0; rowIndex < newRowCombos.Count; rowIndex++)
            {
                var values = new List<string>();
                int oldPageIndex = pageMap[pageIndex];
                int oldRowIndex = rowMap[rowIndex];

                for (int colIndex = 0; colIndex < newColCombos.Count; colIndex++)
                {
                    int oldColIndex = colMap[colIndex];

                    values.Add(GetOldValue(
                        oldSnapshot,
                        oldRowCombos.Count,
                        oldPageIndex,
                        oldRowIndex,
                        oldColIndex));
                }

                dataElement.Add(new XElement("row", string.Join(newSnapshot.Delimiter, values)));
            }
        }
    }

    private static string GetOldValue(
        TableDataSnapshot snapshot,
        int oldRowCount,
        int oldPageIndex,
        int oldRowIndex,
        int oldColIndex)
    {
        if (oldPageIndex < 0 ||
            oldRowIndex < 0 ||
            oldColIndex < 0)
        {
            return "";
        }

        int sourceRowIndex = (oldPageIndex * oldRowCount) + oldRowIndex;

        return sourceRowIndex >= 0 &&
            sourceRowIndex < snapshot.DataRows.Count &&
            oldColIndex < snapshot.DataRows[sourceRowIndex].Count
            ? snapshot.DataRows[sourceRowIndex][oldColIndex]
            : "";
    }

    private static TableDataSnapshot SnapshotTable(XElement tableElement)
    {
        string delimiter = tableElement.Attribute("delimiter")?.Value ?? ",";
        XElement? pageKeysElement = tableElement.Element("pageKeys");
        bool hasExplicitPages = pageKeysElement is not null;
        List<string> pages = pageKeysElement?
            .Elements("key")
            .Select(x => x.Value.Trim())
            .ToList() ?? [""];

        var dataRows = tableElement
            .Element("data")?
            .Elements("row")
            .Select(row => row.Value.Split(delimiter).Select(x => x.Trim()).ToList())
            .ToList() ?? [];

        return new TableDataSnapshot(
            delimiter,
            SnapshotKeySets(tableElement.Element("rowKeys"), "rowSet"),
            SnapshotKeySets(tableElement.Element("colKeys"), "colSet"),
            hasExplicitPages,
            pages,
            dataRows);
    }

    private static List<DimensionSnapshot> SnapshotKeySets(
        XElement? keysElement,
        string setElementName)
    {
        var dimensions = new List<DimensionSnapshot>();
        var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (XElement setElement in keysElement?.Elements(setElementName) ?? [])
        {
            string name = setElement.Attribute("name")?.Value ?? "";
            int nameOccurrence = nameCounts.TryGetValue(name, out int count) ? count : 0;
            nameCounts[name] = nameOccurrence + 1;

            var keyCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var keys = new List<KeySnapshot>();

            foreach (XElement keyElement in setElement.Elements("key"))
            {
                string keyValue = keyElement.Value.Trim();
                int keyOccurrence = keyCounts.TryGetValue(keyValue, out int keyCount) ? keyCount : 0;
                keyCounts[keyValue] = keyOccurrence + 1;
                keys.Add(new KeySnapshot(keyValue, keyOccurrence));
            }

            dimensions.Add(new DimensionSnapshot(
                name,
                nameOccurrence,
                keys));
        }

        return dimensions;
    }

    private static List<Dictionary<int, int>> BuildCombinations(
        List<DimensionSnapshot> dimensions)
    {
        var combinations = new List<Dictionary<int, int>>();

        if (dimensions.Count == 0)
        {
            combinations.Add([]);
            return combinations;
        }

        if (dimensions.Any(dimension => dimension.Keys.Count == 0))
        {
            return combinations;
        }

        BuildCombinationsRecursive(dimensions, 0, [], combinations);
        return combinations;
    }

    private static void BuildCombinationsRecursive(
        List<DimensionSnapshot> dimensions,
        int depth,
        Dictionary<int, int> current,
        List<Dictionary<int, int>> combinations)
    {
        if (depth == dimensions.Count)
        {
            combinations.Add(new Dictionary<int, int>(current));
            return;
        }

        DimensionSnapshot dimension = dimensions[depth];

        for (int keyIndex = 0; keyIndex < dimension.Keys.Count; keyIndex++)
        {
            current[depth] = keyIndex;
            BuildCombinationsRecursive(dimensions, depth + 1, current, combinations);
            current.Remove(depth);
        }
    }

    private static int[] BuildCombinationMap(
        List<DimensionSnapshot> oldDimensions,
        List<DimensionSnapshot> newDimensions,
        List<Dictionary<int, int>> oldCombinations,
        List<Dictionary<int, int>> newCombinations,
        DimensionStructureMap? structureMap = null)
    {
        int[] dimensionMap = IsValidDimensionStructureMap(structureMap, newDimensions)
            ? structureMap!.DimensionMap
            : BuildDimensionMap(oldDimensions, newDimensions);
        int[][] keyMaps = IsValidDimensionStructureMap(structureMap, newDimensions)
            ? structureMap!.KeyMaps
            : BuildKeyMaps(oldDimensions, newDimensions, dimensionMap);
        HashSet<int> mappedOldDimensions = dimensionMap
            .Where(oldDimensionIndex => oldDimensionIndex >= 0)
            .ToHashSet();
        var oldIndexBySignature = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int index = 0; index < oldCombinations.Count; index++)
        {
            string signature = BuildOldCombinationSignature(oldCombinations[index], mappedOldDimensions);
            oldIndexBySignature.TryAdd(signature, index);
        }

        var map = new int[newCombinations.Count];

        for (int index = 0; index < newCombinations.Count; index++)
        {
            map[index] = TryBuildNewCombinationSignature(newCombinations[index], dimensionMap, keyMaps, out string signature) &&
                oldIndexBySignature.TryGetValue(signature, out int oldIndex)
                ? oldIndex
                : -1;
        }

        return map;
    }

    private static bool IsValidPageMap(
        int[]? pageMap,
        TableDataSnapshot oldSnapshot,
        TableDataSnapshot newSnapshot)
    {
        if (pageMap is null ||
            pageMap.Length != newSnapshot.Pages.Count)
        {
            return false;
        }

        return oldSnapshot.HasExplicitPages ||
            !newSnapshot.HasExplicitPages ||
            pageMap.Length == 0 ||
            pageMap[0] >= 0;
    }

    private static bool IsValidDimensionStructureMap(
        DimensionStructureMap? structureMap,
        List<DimensionSnapshot> newDimensions)
    {
        if (structureMap is null ||
            structureMap.DimensionMap.Length != newDimensions.Count ||
            structureMap.KeyMaps.Length != newDimensions.Count)
        {
            return false;
        }

        for (int index = 0; index < newDimensions.Count; index++)
        {
            if (structureMap.KeyMaps[index].Length != newDimensions[index].Keys.Count)
            {
                return false;
            }
        }

        return true;
    }

    private static int[] BuildDimensionMap(
        List<DimensionSnapshot> oldDimensions,
        List<DimensionSnapshot> newDimensions)
    {
        var map = Enumerable.Repeat(-1, newDimensions.Count).ToArray();
        var usedOldIndexes = new HashSet<int>();

        for (int newIndex = 0; newIndex < newDimensions.Count; newIndex++)
        {
            DimensionSnapshot newDimension = newDimensions[newIndex];
            int oldIndex = -1;

            for (int candidateIndex = 0; candidateIndex < oldDimensions.Count; candidateIndex++)
            {
                DimensionSnapshot oldDimension = oldDimensions[candidateIndex];

                if (!usedOldIndexes.Contains(candidateIndex) &&
                    string.Equals(oldDimension.Name, newDimension.Name, StringComparison.OrdinalIgnoreCase) &&
                    oldDimension.NameOccurrence == newDimension.NameOccurrence)
                {
                    oldIndex = candidateIndex;
                    break;
                }
            }

            if (oldIndex >= 0)
            {
                map[newIndex] = oldIndex;
                usedOldIndexes.Add(oldIndex);
            }
        }

        for (int newIndex = 0; newIndex < newDimensions.Count; newIndex++)
        {
            if (map[newIndex] >= 0 ||
                newIndex >= oldDimensions.Count ||
                usedOldIndexes.Contains(newIndex))
            {
                continue;
            }

            map[newIndex] = newIndex;
            usedOldIndexes.Add(newIndex);
        }

        return map;
    }

    private static int[][] BuildKeyMaps(
        List<DimensionSnapshot> oldDimensions,
        List<DimensionSnapshot> newDimensions,
        int[] dimensionMap)
    {
        var keyMaps = new int[newDimensions.Count][];

        for (int newDimensionIndex = 0; newDimensionIndex < newDimensions.Count; newDimensionIndex++)
        {
            DimensionSnapshot newDimension = newDimensions[newDimensionIndex];
            int oldDimensionIndex = dimensionMap[newDimensionIndex];
            var keyMap = Enumerable.Repeat(-1, newDimension.Keys.Count).ToArray();

            if (oldDimensionIndex < 0 || oldDimensionIndex >= oldDimensions.Count)
            {
                keyMaps[newDimensionIndex] = keyMap;
                continue;
            }

            List<KeySnapshot> oldKeys = oldDimensions[oldDimensionIndex].Keys;
            var usedOldKeyIndexes = new HashSet<int>();

            for (int newKeyIndex = 0; newKeyIndex < newDimension.Keys.Count; newKeyIndex++)
            {
                KeySnapshot newKey = newDimension.Keys[newKeyIndex];
                int oldKeyIndex = -1;

                for (int candidateIndex = 0; candidateIndex < oldKeys.Count; candidateIndex++)
                {
                    KeySnapshot oldKey = oldKeys[candidateIndex];

                    if (!usedOldKeyIndexes.Contains(candidateIndex) &&
                        string.Equals(oldKey.Value, newKey.Value, StringComparison.OrdinalIgnoreCase) &&
                        oldKey.ValueOccurrence == newKey.ValueOccurrence)
                    {
                        oldKeyIndex = candidateIndex;
                        break;
                    }
                }

                if (oldKeyIndex >= 0)
                {
                    keyMap[newKeyIndex] = oldKeyIndex;
                    usedOldKeyIndexes.Add(oldKeyIndex);
                }
            }

            for (int newKeyIndex = 0; newKeyIndex < newDimension.Keys.Count; newKeyIndex++)
            {
                if (keyMap[newKeyIndex] >= 0 ||
                    newKeyIndex >= oldKeys.Count ||
                    usedOldKeyIndexes.Contains(newKeyIndex))
                {
                    continue;
                }

                keyMap[newKeyIndex] = newKeyIndex;
                usedOldKeyIndexes.Add(newKeyIndex);
            }

            keyMaps[newDimensionIndex] = keyMap;
        }

        return keyMaps;
    }

    private static string BuildOldCombinationSignature(
        Dictionary<int, int> combination,
        HashSet<int> mappedOldDimensions)
    {
        return string.Join(
            "\u001E",
            combination
                .Where(pair => mappedOldDimensions.Contains(pair.Key))
                .OrderBy(pair => pair.Key)
                .Select(pair => $"{pair.Key}\u001F{pair.Value}"));
    }

    private static bool TryBuildNewCombinationSignature(
        Dictionary<int, int> combination,
        int[] dimensionMap,
        int[][] keyMaps,
        out string signature)
    {
        var parts = new List<string>();

        for (int newDimensionIndex = 0; newDimensionIndex < dimensionMap.Length; newDimensionIndex++)
        {
            int oldDimensionIndex = dimensionMap[newDimensionIndex];

            if (oldDimensionIndex < 0)
            {
                continue;
            }

            if (!combination.TryGetValue(newDimensionIndex, out int newKeyIndex) ||
                newKeyIndex < 0 ||
                newKeyIndex >= keyMaps[newDimensionIndex].Length)
            {
                signature = "";
                return false;
            }

            int oldKeyIndex = keyMaps[newDimensionIndex][newKeyIndex];

            if (oldKeyIndex < 0)
            {
                signature = "";
                return false;
            }

            parts.Add($"{oldDimensionIndex}\u001F{oldKeyIndex}");
        }

        signature = string.Join(
            "\u001E",
            parts.OrderBy(part => part, StringComparer.Ordinal));
        return true;
    }

    private static int[] BuildPageMap(
        TableDataSnapshot oldSnapshot,
        TableDataSnapshot newSnapshot)
    {
        var map = new int[newSnapshot.Pages.Count];

        if (!oldSnapshot.HasExplicitPages && !newSnapshot.HasExplicitPages)
        {
            map[0] = 0;
            return map;
        }

        if (!oldSnapshot.HasExplicitPages && newSnapshot.HasExplicitPages)
        {
            for (int index = 0; index < map.Length; index++)
            {
                map[index] = index == 0 ? 0 : -1;
            }

            return map;
        }

        if (oldSnapshot.HasExplicitPages && !newSnapshot.HasExplicitPages)
        {
            map[0] = 0;
            return map;
        }

        List<KeySnapshot> oldPages = BuildPageSnapshots(oldSnapshot.Pages);
        List<KeySnapshot> newPages = BuildPageSnapshots(newSnapshot.Pages);
        var usedOldIndexes = new HashSet<int>();

        for (int newIndex = 0; newIndex < newPages.Count; newIndex++)
        {
            KeySnapshot newPage = newPages[newIndex];

            for (int oldIndex = 0; oldIndex < oldPages.Count; oldIndex++)
            {
                KeySnapshot oldPage = oldPages[oldIndex];

                if (!usedOldIndexes.Contains(oldIndex) &&
                    string.Equals(oldPage.Value, newPage.Value, StringComparison.OrdinalIgnoreCase) &&
                    oldPage.ValueOccurrence == newPage.ValueOccurrence)
                {
                    map[newIndex] = oldIndex;
                    usedOldIndexes.Add(oldIndex);
                    break;
                }
            }
        }

        for (int newIndex = 0; newIndex < newPages.Count; newIndex++)
        {
            if (map[newIndex] >= 0 ||
                newIndex >= oldPages.Count ||
                usedOldIndexes.Contains(newIndex))
            {
                continue;
            }

            map[newIndex] = newIndex;
            usedOldIndexes.Add(newIndex);
        }

        return map;
    }

    private static List<KeySnapshot> BuildPageSnapshots(List<string> pages)
    {
        var pageCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var pageSnapshots = new List<KeySnapshot>();

        foreach (string page in pages)
        {
            int occurrence = pageCounts.TryGetValue(page, out int count) ? count : 0;
            pageCounts[page] = occurrence + 1;
            pageSnapshots.Add(new KeySnapshot(page, occurrence));
        }

        return pageSnapshots;
    }

    private void ApplyDirtyStates()
    {
        foreach (TableDefinition table in _tables)
        {
            table.HasChanges = IsTableDirty(table.SourceIndex);
        }
    }

    private bool IsTableDirty(int sourceIndex)
    {
        if (_originalDocument is null || _ghostDocument is null)
        {
            return false;
        }

        XElement? originalTable = GetTableElement(_originalDocument, sourceIndex);
        XElement? ghostTable = GetTableElement(_ghostDocument, sourceIndex);

        return originalTable is null ||
            ghostTable is null ||
            !XNode.DeepEquals(originalTable, ghostTable);
    }

    private bool HasAnyChanges()
    {
        if (_originalDocument is null || _ghostDocument is null)
        {
            return false;
        }

        return !string.Equals(
            GetDocumentText(_originalDocument),
            GetDocumentText(_ghostDocument),
            StringComparison.Ordinal);
    }

    private static bool DocumentHasChanges(OpenDocumentTab document)
    {
        if (document.OriginalDocument is null || document.GhostDocument is null)
        {
            return false;
        }

        return !string.Equals(
            GetDocumentText(document.OriginalDocument),
            GetDocumentText(document.GhostDocument),
            StringComparison.Ordinal);
    }

    private void UpdateSaveState()
    {
        bool hasChanges = HasAnyChanges();
        bool hasDocument = _ghostDocument is not null;

        if (SaveButton is not null)
        {
            SaveButton.IsEnabled = hasChanges;
        }

        if (FileSaveMenuItem is not null)
        {
            FileSaveMenuItem.IsEnabled = hasChanges;
        }

        if (FileSaveAsMenuItem is not null)
        {
            FileSaveAsMenuItem.IsEnabled = hasDocument;
        }

        if (EditMetadataMenuItem is not null)
        {
            EditMetadataMenuItem.IsEnabled = hasDocument;
        }

        if (ExportTablesMenuItem is not null)
        {
            ExportTablesMenuItem.IsEnabled = hasDocument && _tables.Count > 0;
        }
    }

    private void UpdateTitle()
    {
        string dirtyMarker = HasAnyChanges() ? " *" : "";

        if (_currentFilePath is null)
        {
            Title = _ghostDocument is null
                ? "Table Editor"
                : $"Table Editor - {_activeDocument?.UntitledName ?? UntitledFileName}{dirtyMarker}";
            return;
        }

        Title = $"Table Editor - {Path.GetFileName(_currentFilePath)}{dirtyMarker}";
    }

    private void PersistGhostDocument()
    {
        if (_ghostDocument is null || _ghostFilePath is null)
        {
            return;
        }

        _ghostDocument.Save(_ghostFilePath, SaveOptions.DisableFormatting);
    }

    private static void PersistGhostDocument(OpenDocumentTab document)
    {
        if (document.GhostDocument is null || document.GhostFilePath is null)
        {
            return;
        }

        document.GhostDocument.Save(document.GhostFilePath, SaveOptions.DisableFormatting);
    }

    private static string CreateGhostFilePath(string filePath)
    {
        string fileName = $"{Path.GetFileNameWithoutExtension(filePath)}.{Guid.NewGuid():N}.ghost.xml";
        return Path.Combine(Path.GetTempPath(), fileName);
    }

    private static string GetDocumentText(XDocument document)
    {
        using var writer = new StringWriter();
        document.Save(writer, SaveOptions.DisableFormatting);
        return writer.ToString();
    }

    private static bool TryParseDocument(string xml, out XDocument? document, out string? error)
    {
        try
        {
            document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            error = null;
            return true;
        }
        catch (XmlException ex)
        {
            document = null;
            error = FormatXmlError(ex);
            return false;
        }
    }

    private static bool TryParseTableElement(string xml, out XElement? tableElement, out string? error)
    {
        try
        {
            tableElement = XElement.Parse(xml, LoadOptions.PreserveWhitespace);

            if (tableElement.Name != "table")
            {
                error = "Table XML must have a single <table> root element.";
                tableElement = null;
                return false;
            }

            error = null;
            return true;
        }
        catch (XmlException ex)
        {
            tableElement = null;
            error = FormatXmlError(ex);
            return false;
        }
    }

    private static string FormatXmlError(XmlException ex)
    {
        return $"XML is not well-formed at line {ex.LineNumber}, position {ex.LinePosition}: {ex.Message}";
    }

    private bool TryGetLoadedDocument(out XDocument ghostDocument)
    {
        if (_ghostDocument is not null)
        {
            ghostDocument = _ghostDocument;
            return true;
        }

        ghostDocument = new XDocument();
        MessageBox.Show(
            "Open or create an XML file first.",
            "No XML file",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }

    private static XElement? GetTableElement(XDocument document, TableDefinition table)
    {
        return GetTableElement(document, table.SourceIndex);
    }

    private static XElement? GetTableElement(XDocument document, int sourceIndex)
    {
        return document.Root?
            .Elements("table")
            .ElementAtOrDefault(sourceIndex);
    }

    private static T? FindParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T parent)
            {
                return parent;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private static T? FindChild<T>(DependencyObject? parent)
        where T : DependencyObject
    {
        if (parent is null)
        {
            return null;
        }

        int childCount = VisualTreeHelper.GetChildrenCount(parent);

        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);

            if (child is T typedChild)
            {
                return typedChild;
            }

            T? descendant = FindChild<T>(child);

            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private void TablesList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindParent<ListBoxItem>(e.OriginalSource as DependencyObject) is not ListBoxItem item)
        {
            TablesList.ContextMenu = null;
            return;
        }

        TablesList.ContextMenu = _tableContextMenu;
        item.IsSelected = true;
        item.Focus();
    }

    private void RatingGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_currentTable?.RenderedTable is not RenderedTable renderedTable ||
            FindParent<DataGridCell>(e.OriginalSource as DependencyObject) is not DataGridCell cell ||
            cell.DataContext is not RenderedTableRow row ||
            cell.Column is null)
        {
            return;
        }

        int columnIndex = RatingGrid.Columns.IndexOf(cell.Column);
        ContextMenu? contextMenu = null;

        if (row.RowIndex < renderedTable.Table.ColSets.Count)
        {
            contextMenu = CreateSetContextMenu(
                "colKeys",
                "colSet",
                "column set",
                "ColSet",
                row.RowIndex);
        }
        else if (columnIndex >= 0 && columnIndex < renderedTable.RowHeaderColumnCount)
        {
            contextMenu = CreateSetContextMenu(
                "rowKeys",
                "rowSet",
                "row set",
                "RowSet",
                columnIndex);
        }

        if (contextMenu is null)
        {
            return;
        }

        RatingGrid.CurrentCell = new DataGridCellInfo(row, cell.Column);
        cell.Focus();
        contextMenu.PlacementTarget = cell;
        contextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void PageTabs_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        int pageIndex = PageTabs.SelectedIndex;

        if (FindParent<ListBoxItem>(e.OriginalSource as DependencyObject) is ListBoxItem item)
        {
            item.IsSelected = true;
            item.Focus();
            pageIndex = PageTabs.ItemContainerGenerator.IndexFromContainer(item);
        }

        ContextMenu contextMenu = CreatePageContextMenu(pageIndex);
        contextMenu.PlacementTarget = PageTabs;
        contextMenu.IsOpen = true;
        e.Handled = true;
    }

    private ContextMenu CreateSetContextMenu(
        string keysElementName,
        string setElementName,
        string label,
        string defaultNamePrefix,
        int setIndex)
    {
        var contextMenu = new ContextMenu();

        contextMenu.Items.Add(CreateMenuItem(
            "Edit Set",
            (_, _) => EditKeySet(keysElementName, setElementName, label, setIndex)));
        contextMenu.Items.Add(CreateMenuItem(
            "Add Set",
            (_, _) => AddKeySet(keysElementName, setElementName, label, defaultNamePrefix)));
        contextMenu.Items.Add(CreateMenuItem(
            "Delete Set",
            (_, _) => RemoveKeySetAt(keysElementName, setElementName, label, setIndex)));

        return contextMenu;
    }

    private ContextMenu CreatePageContextMenu(int pageIndex)
    {
        var contextMenu = new ContextMenu();
        bool hasExplicitPages = _currentTable?.RenderedTable?.HasPages == true;

        MenuItem editItem = CreateMenuItem("Edit Page", (_, _) => EditPageAt(pageIndex));
        editItem.IsEnabled = hasExplicitPages && pageIndex >= 0;
        contextMenu.Items.Add(editItem);
        contextMenu.Items.Add(CreateMenuItem("Add Page", AddPage_Click));

        MenuItem deleteItem = CreateMenuItem("Delete Page", (_, _) => RemovePageAt(pageIndex));
        deleteItem.IsEnabled = hasExplicitPages && pageIndex >= 0;
        contextMenu.Items.Add(deleteItem);

        return contextMenu;
    }

    private static MenuItem CreateMenuItem(string header, RoutedEventHandler handler)
    {
        var menuItem = new MenuItem
        {
            Header = header
        };

        menuItem.Click += handler;
        return menuItem;
    }

    private static void ShowTableNotFoundMessage()
    {
        MessageBox.Show(
            "The selected table was not found in the working XML file.",
            "Table not found",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static DataGridLength GetColumnWidth(bool isRowHeader)
    {
        return isRowHeader
            ? new DataGridLength(190)
            : new DataGridLength(85);
    }

    private sealed record TableDataSnapshot(
        string Delimiter,
        List<DimensionSnapshot> RowSets,
        List<DimensionSnapshot> ColSets,
        bool HasExplicitPages,
        List<string> Pages,
        List<List<string>> DataRows);

    private sealed record DimensionSnapshot(
        string Name,
        int NameOccurrence,
        List<KeySnapshot> Keys);

    private sealed record KeySnapshot(
        string Value,
        int ValueOccurrence);

    private sealed record TableStructureMap(
        DimensionStructureMap RowMap,
        DimensionStructureMap ColumnMap,
        int[] PageMap);

    private sealed record DimensionStructureMap(
        int[] DimensionMap,
        int[][] KeyMaps);
}
