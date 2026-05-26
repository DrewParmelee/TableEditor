using AOTableEditor.Models;
using AOTableEditor.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace AOTableEditor;

public partial class XmlFilePickerWindow : Window
{
    private const string AllFilterValue = "All";

    private readonly ObservableCollection<XmlTableFileItem> _files;
    private readonly ICollectionView _filesView;
    private readonly TableXmlFileSearchResult _searchResult;

    public XmlFilePickerWindow(string folderPath, IEnumerable<string>? hiddenFilePaths = null)
    {
        InitializeComponent();

        FolderText.Text = folderPath;
        FolderText.ToolTip = folderPath;

        HashSet<string> hiddenPaths = hiddenFilePaths?
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        _searchResult = TableMetadataService.FindTableXmlFiles(folderPath);
        _files = new ObservableCollection<XmlTableFileItem>(
            _searchResult.Files.Where(file => !hiddenPaths.Contains(Path.GetFullPath(file.FilePath))));
        _filesView = CollectionViewSource.GetDefaultView(_files);
        _filesView.Filter = FilterFile;
        FilesGrid.ItemsSource = _filesView;

        LineOfBusinessFilter.ItemsSource = BuildFilterOptions(TableMetadataService.LineOfBusinessOptions);
        StateFilter.ItemsSource = BuildFilterOptions(TableMetadataService.StateOptions);
        LineOfBusinessFilter.SelectedIndex = 0;
        StateFilter.SelectedIndex = 0;

        if (_files.Count > 0)
        {
            FilesGrid.SelectedIndex = 0;
        }

        UpdateStatus();
    }

    public IReadOnlyList<string> SelectedFilePaths { get; private set; } = [];
    public string? SelectedFilePath => SelectedFilePaths.FirstOrDefault();

    private void Filter_Changed(object sender, EventArgs e)
    {
        _filesView.Refresh();
        UpdateStatus();
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        FileFilterText.Text = "";
        LineOfBusinessFilter.SelectedIndex = 0;
        StateFilter.SelectedIndex = 0;
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        OpenSelectedFile();
    }

    private void FilesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FilesGrid.SelectedItem is XmlTableFileItem)
        {
            OpenSelectedFile();
        }
    }

    private bool FilterFile(object item)
    {
        if (item is not XmlTableFileItem file)
        {
            return false;
        }

        return MatchesText(file.FileName, FileFilterText.Text) &&
            MatchesOption(file.LineOfBusiness, LineOfBusinessFilter.SelectedItem as string) &&
            MatchesOption(file.State, StateFilter.SelectedItem as string);
    }

    private void OpenSelectedFile()
    {
        List<XmlTableFileItem> selectedFiles = FilesGrid.SelectedItems
            .OfType<XmlTableFileItem>()
            .ToList();

        if (selectedFiles.Count == 0)
        {
            return;
        }

        SelectedFilePaths = selectedFiles
            .Select(file => file.FilePath)
            .ToList();
        DialogResult = true;
    }

    private void UpdateStatus()
    {
        int visibleCount = _filesView.Cast<object>().Count();

        StatusText.Text = _files.Count == 0
            ? _searchResult.Files.Count == 0
                ? "No XML files with a <tables> root were found in this folder or its subfolders."
                : "All matching table XML files are already open."
            : _searchResult.HitLimit
                ? $"{visibleCount} of {_files.Count} table XML files shown. Search stopped after {_searchResult.Limit} matches."
                : $"{visibleCount} of {_files.Count} table XML files shown.";
    }

    private static IReadOnlyList<string> BuildFilterOptions(IEnumerable<string> options)
    {
        return [AllFilterValue, .. options];
    }

    private static bool MatchesText(string value, string filter)
    {
        return string.IsNullOrWhiteSpace(filter) ||
            value.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesOption(string value, string? filter)
    {
        return string.IsNullOrWhiteSpace(filter) ||
            filter == AllFilterValue ||
            string.Equals(value, filter, StringComparison.OrdinalIgnoreCase);
    }

}
