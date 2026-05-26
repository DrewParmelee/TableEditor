using AOTableEditor.Models;
using AOTableEditor.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AOTableEditor;

public partial class TablePropertiesWindow : Window
{
    public static IReadOnlyList<string> SearchTypeOptions { get; } =
    [
        "=",
        "<",
        "<=",
        ">",
        ">=",
        "Range",
        "Interpolate",
        "Graduated"
    ];

    public static IReadOnlyList<string> DataTypeOptions => TableValueFormatter.DataTypeOptions;

    private readonly TablePropertiesEditorModel _model;

    public TablePropertiesWindow(TableDefinition table)
    {
        InitializeComponent();

        _model = TablePropertiesEditorModel.FromTable(table);
        DataContext = _model;

        if (_model.RowSets.Count > 0)
        {
            RowSetsList.SelectedIndex = 0;
        }

        if (_model.ColumnSets.Count > 0)
        {
            ColumnSetsList.SelectedIndex = 0;
        }

        if (_model.Pages.Count > 0)
        {
            PagesList.SelectedIndex = 0;
        }

        TableNameTextBox.SelectAll();
        TableNameTextBox.Focus();
    }

    public TablePropertiesResult? Result { get; private set; }

    private void AddRowSet_Click(object sender, RoutedEventArgs e)
    {
        AddSet(_model.RowSets, RowSetsList, "RowSet");
    }

    private void DeleteRowSet_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelected(RowSetsList, _model.RowSets);
    }

    private void MoveRowSetUp_Click(object sender, RoutedEventArgs e)
    {
        MoveSelected(RowSetsList, _model.RowSets, -1);
    }

    private void MoveRowSetDown_Click(object sender, RoutedEventArgs e)
    {
        MoveSelected(RowSetsList, _model.RowSets, 1);
    }

    private void AddRowKey_Click(object sender, RoutedEventArgs e)
    {
        AddKey(RowSetsList, RowKeysList);
    }

    private void DeleteRowKey_Click(object sender, RoutedEventArgs e)
    {
        DeleteKey(RowSetsList, RowKeysList);
    }

    private void MoveRowKeyUp_Click(object sender, RoutedEventArgs e)
    {
        MoveKey(RowSetsList, RowKeysList, -1);
    }

    private void MoveRowKeyDown_Click(object sender, RoutedEventArgs e)
    {
        MoveKey(RowSetsList, RowKeysList, 1);
    }

    private void AddColumnSet_Click(object sender, RoutedEventArgs e)
    {
        AddSet(_model.ColumnSets, ColumnSetsList, "ColumnSet");
    }

    private void DeleteColumnSet_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelected(ColumnSetsList, _model.ColumnSets);
    }

    private void MoveColumnSetUp_Click(object sender, RoutedEventArgs e)
    {
        MoveSelected(ColumnSetsList, _model.ColumnSets, -1);
    }

    private void MoveColumnSetDown_Click(object sender, RoutedEventArgs e)
    {
        MoveSelected(ColumnSetsList, _model.ColumnSets, 1);
    }

    private void AddColumnKey_Click(object sender, RoutedEventArgs e)
    {
        AddKey(ColumnSetsList, ColumnKeysList);
    }

    private void DeleteColumnKey_Click(object sender, RoutedEventArgs e)
    {
        DeleteKey(ColumnSetsList, ColumnKeysList);
    }

    private void MoveColumnKeyUp_Click(object sender, RoutedEventArgs e)
    {
        MoveKey(ColumnSetsList, ColumnKeysList, -1);
    }

    private void MoveColumnKeyDown_Click(object sender, RoutedEventArgs e)
    {
        MoveKey(ColumnSetsList, ColumnKeysList, 1);
    }

    private void AddPage_Click(object sender, RoutedEventArgs e)
    {
        _model.Pages.Add(new KeyEditorItem(GetUniqueName(_model.Pages.Select(x => x.Value), "Page"), -1));
        PagesList.SelectedIndex = _model.Pages.Count - 1;
    }

    private void DeletePage_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelected(PagesList, _model.Pages);
    }

    private void MovePageUp_Click(object sender, RoutedEventArgs e)
    {
        MoveSelected(PagesList, _model.Pages, -1);
    }

    private void MovePageDown_Click(object sender, RoutedEventArgs e)
    {
        MoveSelected(PagesList, _model.Pages, 1);
    }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildResult(out TablePropertiesResult? result, out string error))
        {
            ErrorText.Text = error;
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private void SetEditorItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
        {
            return;
        }

        if (FindParent<ComboBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (sender is FrameworkElement element &&
            element.DataContext is SetEditorItem item)
        {
            BeginInlineEdit(item, element);
            e.Handled = true;
        }
    }

    private void KeyEditorItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
        {
            return;
        }

        if (sender is FrameworkElement element &&
            element.DataContext is KeyEditorItem item)
        {
            BeginInlineEdit(item, element);
            e.Handled = true;
        }
    }

    private void InlineEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        EndInlineEdit((sender as FrameworkElement)?.DataContext);
    }

    private void InlineEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Escape)
        {
            return;
        }

        EndInlineEdit((sender as FrameworkElement)?.DataContext);
        e.Handled = true;
    }

    private void BeginInlineEdit(
        object item,
        FrameworkElement rowElement)
    {
        switch (item)
        {
            case SetEditorItem set:
                set.IsEditing = true;
                break;

            case KeyEditorItem key:
                key.IsEditing = true;
                break;
        }

        rowElement.Dispatcher.BeginInvoke(
            () =>
            {
                TextBox? editor = FindChild<TextBox>(rowElement);

                if (editor is null)
                {
                    return;
                }

                editor.Focus();
                editor.SelectAll();
            },
            DispatcherPriority.Background);
    }

    private static void EndInlineEdit(object? item)
    {
        switch (item)
        {
            case SetEditorItem set:
                set.IsEditing = false;
                break;

            case KeyEditorItem key:
                key.IsEditing = false;
                break;
        }
    }

    private bool TryBuildResult(out TablePropertiesResult? result, out string error)
    {
        string tableName = _model.TableName.Trim();

        if (string.IsNullOrWhiteSpace(tableName))
        {
            result = null;
            error = "Table name cannot be blank.";
            return false;
        }

        if (!TryBuildSetResults(_model.RowSets, "row set", out List<TablePropertiesKeySet> rowSets, out error) ||
            !TryBuildSetResults(_model.ColumnSets, "column set", out List<TablePropertiesKeySet> columnSets, out error))
        {
            result = null;
            return false;
        }

        string dataType = TableValueFormatter.NormalizeDataType(_model.DataType);
        int? decimals = null;

        if (TableValueFormatter.SupportsDecimals(dataType) &&
            !string.IsNullOrWhiteSpace(_model.DecimalsText))
        {
            if (!int.TryParse(_model.DecimalsText.Trim(), out int parsedDecimals) ||
                parsedDecimals < 0 ||
                parsedDecimals > 15)
            {
                result = null;
                error = "Decimals must be a whole number from 0 to 15.";
                return false;
            }

            decimals = parsedDecimals;
        }

        var pages = new List<TablePropertiesKey>();

        for (int index = 0; index < _model.Pages.Count; index++)
        {
            KeyEditorItem page = _model.Pages[index];
            string pageName = page.Value.Trim();

            if (string.IsNullOrWhiteSpace(pageName))
            {
                result = null;
                error = $"Page {index + 1} cannot be blank.";
                return false;
            }

            pages.Add(new TablePropertiesKey(pageName, page.OriginalIndex));
        }

        result = new TablePropertiesResult(
            tableName,
            _model.Comment.Trim(),
            dataType,
            decimals,
            rowSets,
            columnSets,
            _model.PageSearchType,
            pages);
        error = "";
        return true;
    }

    private static bool TryBuildSetResults(
        ObservableCollection<SetEditorItem> setItems,
        string label,
        out List<TablePropertiesKeySet> sets,
        out string error)
    {
        sets = [];

        for (int setIndex = 0; setIndex < setItems.Count; setIndex++)
        {
            SetEditorItem setItem = setItems[setIndex];
            string setName = setItem.Name.Trim();

            if (string.IsNullOrWhiteSpace(setName))
            {
                error = $"{ToTitleCase(label)} {setIndex + 1} name cannot be blank.";
                return false;
            }

            if (setItem.Keys.Count == 0)
            {
                error = $"{ToTitleCase(label)} '{setName}' must have at least one key.";
                return false;
            }

            var keys = new List<TablePropertiesKey>();

            for (int keyIndex = 0; keyIndex < setItem.Keys.Count; keyIndex++)
            {
                KeyEditorItem keyItem = setItem.Keys[keyIndex];
                string key = keyItem.Value.Trim();

                if (string.IsNullOrWhiteSpace(key))
                {
                    error = $"{ToTitleCase(label)} '{setName}' key {keyIndex + 1} cannot be blank.";
                    return false;
                }

                keys.Add(new TablePropertiesKey(key, keyItem.OriginalIndex));
            }

            sets.Add(new TablePropertiesKeySet(setName, setItem.SearchType, setItem.OriginalIndex, keys));
        }

        error = "";
        return true;
    }

    private static void AddSet(
        ObservableCollection<SetEditorItem> sets,
        ListBox setList,
        string baseName)
    {
        var set = new SetEditorItem(
            GetUniqueName(sets.Select(x => x.Name), baseName),
            "=",
            -1,
            [new KeyEditorItem("Default", -1)]);

        sets.Add(set);
        setList.SelectedIndex = sets.Count - 1;
    }

    private static void AddKey(ListBox setList, ListBox keyList)
    {
        if (setList.SelectedItem is not SetEditorItem set)
        {
            return;
        }

        set.Keys.Add(new KeyEditorItem(GetUniqueName(set.Keys.Select(x => x.Value), "Key"), -1));
        keyList.SelectedIndex = set.Keys.Count - 1;
    }

    private static void DeleteKey(ListBox setList, ListBox keyList)
    {
        if (setList.SelectedItem is not SetEditorItem set ||
            keyList.SelectedItem is not KeyEditorItem key)
        {
            return;
        }

        if (set.Keys.Count <= 1)
        {
            MessageBox.Show(
                "A set must keep at least one key.",
                "Table Properties",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        int selectedIndex = keyList.SelectedIndex;
        set.Keys.Remove(key);
        keyList.SelectedIndex = Math.Clamp(selectedIndex, 0, set.Keys.Count - 1);
    }

    private static void MoveKey(ListBox setList, ListBox keyList, int direction)
    {
        if (setList.SelectedItem is not SetEditorItem set)
        {
            return;
        }

        MoveSelected(keyList, set.Keys, direction);
    }

    private static void DeleteSelected<T>(ListBox listBox, ObservableCollection<T> items)
    {
        int selectedIndex = listBox.SelectedIndex;

        if (selectedIndex < 0 || selectedIndex >= items.Count)
        {
            return;
        }

        items.RemoveAt(selectedIndex);

        if (items.Count > 0)
        {
            listBox.SelectedIndex = Math.Clamp(selectedIndex, 0, items.Count - 1);
        }
    }

    private static void MoveSelected<T>(ListBox listBox, ObservableCollection<T> items, int direction)
    {
        int oldIndex = listBox.SelectedIndex;
        int newIndex = oldIndex + direction;

        if (oldIndex < 0 ||
            oldIndex >= items.Count ||
            newIndex < 0 ||
            newIndex >= items.Count)
        {
            return;
        }

        items.Move(oldIndex, newIndex);
        listBox.SelectedIndex = newIndex;
    }

    private static string GetUniqueName(IEnumerable<string> existingValues, string baseName)
    {
        HashSet<string> existing = existingValues
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!existing.Contains(baseName))
        {
            return baseName;
        }

        for (int index = 2; ; index++)
        {
            string candidate = $"{baseName}{index}";

            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static string ToTitleCase(string value)
    {
        return string.Join(
            " ",
            value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
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

            if (child is T typedChild &&
                typedChild is not ComboBox)
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

    public static string NormalizeSearchType(string? searchType)
    {
        return SearchTypeOptions.Contains(searchType)
            ? searchType!
            : "=";
    }
}

public sealed class TablePropertiesEditorModel : INotifyPropertyChanged
{
    private string _dataType = TableValueFormatter.DefaultDataType;
    private string _decimalsText = "";

    public string TableName { get; set; } = "";
    public string Comment { get; set; } = "";
    public string PageSearchType { get; set; } = "=";
    public string DataType
    {
        get => _dataType;
        set
        {
            string normalizedValue = TableValueFormatter.NormalizeDataType(value);

            if (_dataType == normalizedValue)
            {
                return;
            }

            _dataType = normalizedValue;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDecimalsEnabled));
        }
    }
    public string DecimalsText
    {
        get => _decimalsText;
        set
        {
            if (_decimalsText == value)
            {
                return;
            }

            _decimalsText = value;
            OnPropertyChanged();
        }
    }
    public bool IsDecimalsEnabled => TableValueFormatter.SupportsDecimals(DataType);
    public ObservableCollection<SetEditorItem> RowSets { get; } = [];
    public ObservableCollection<SetEditorItem> ColumnSets { get; } = [];
    public ObservableCollection<KeyEditorItem> Pages { get; } = [];
    public event PropertyChangedEventHandler? PropertyChanged;

    public static TablePropertiesEditorModel FromTable(TableDefinition table)
    {
        var model = new TablePropertiesEditorModel
        {
            TableName = table.Name,
            Comment = table.Comment,
            DataType = table.DataType,
            DecimalsText = table.Decimals?.ToString() ?? "",
            PageSearchType = TablePropertiesWindow.NormalizeSearchType(table.PageSearchType)
        };

        for (int index = 0; index < table.RowSets.Count; index++)
        {
            model.RowSets.Add(SetEditorItem.FromKeySet(table.RowSets[index], index));
        }

        for (int index = 0; index < table.ColSets.Count; index++)
        {
            model.ColumnSets.Add(SetEditorItem.FromKeySet(table.ColSets[index], index));
        }

        for (int index = 0; index < table.PageKeys.Count; index++)
        {
            model.Pages.Add(new KeyEditorItem(table.PageKeys[index], index));
        }

        return model;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class SetEditorItem : INotifyPropertyChanged
{
    private string _name;
    private string _searchType;
    private bool _isEditing;

    public SetEditorItem(string name, string searchType, int originalIndex, IEnumerable<KeyEditorItem> keys)
    {
        _name = name;
        _searchType = TablePropertiesWindow.NormalizeSearchType(searchType);
        OriginalIndex = originalIndex;

        foreach (KeyEditorItem key in keys)
        {
            Keys.Add(key);
        }
    }

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            OnPropertyChanged();
        }
    }

    public string SearchType
    {
        get => _searchType;
        set
        {
            string normalizedValue = TablePropertiesWindow.NormalizeSearchType(value);

            if (_searchType == normalizedValue)
            {
                return;
            }

            _searchType = normalizedValue;
            OnPropertyChanged();
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing == value)
            {
                return;
            }

            _isEditing = value;
            OnPropertyChanged();
        }
    }

    public int OriginalIndex { get; }
    public ObservableCollection<KeyEditorItem> Keys { get; } = [];
    public event PropertyChangedEventHandler? PropertyChanged;

    public static SetEditorItem FromKeySet(KeySetDefinition keySet, int originalIndex)
    {
        return new SetEditorItem(
            keySet.Name,
            keySet.SearchType,
            originalIndex,
            keySet.Keys.Select((key, keyIndex) => new KeyEditorItem(key, keyIndex)));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class KeyEditorItem : INotifyPropertyChanged
{
    private string _value;
    private bool _isEditing;

    public KeyEditorItem(string value, int originalIndex)
    {
        _value = value;
        OriginalIndex = originalIndex;
    }

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value)
            {
                return;
            }

            _value = value;
            OnPropertyChanged();
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing == value)
            {
                return;
            }

            _isEditing = value;
            OnPropertyChanged();
        }
    }

    public int OriginalIndex { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record TablePropertiesResult(
    string TableName,
    string Comment,
    string DataType,
    int? Decimals,
    List<TablePropertiesKeySet> RowSets,
    List<TablePropertiesKeySet> ColumnSets,
    string PageSearchType,
    List<TablePropertiesKey> Pages);

public sealed record TablePropertiesKeySet(
    string Name,
    string SearchType,
    int OriginalIndex,
    List<TablePropertiesKey> Keys);

public sealed record TablePropertiesKey(
    string Value,
    int OriginalIndex);
