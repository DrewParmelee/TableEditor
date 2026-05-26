using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace AOTableEditor.Models;

public sealed class OpenDocumentTab : INotifyPropertyChanged
{
    private string? _filePath;
    private bool _hasChanges;
    private string _untitledName = "Untitled.xml";

    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (_filePath == value)
            {
                return;
            }

            _filePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string UntitledName
    {
        get => _untitledName;
        set
        {
            if (_untitledName == value)
            {
                return;
            }

            _untitledName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string? GhostFilePath { get; set; }
    public XDocument? OriginalDocument { get; set; }
    public XDocument? GhostDocument { get; set; }
    public List<TableDefinition> Tables { get; set; } = [];
    public List<TableDefinition> OriginalTables { get; set; } = [];
    public int SelectedTableIndex { get; set; }

    public bool HasChanges
    {
        get => _hasChanges;
        set
        {
            if (_hasChanges == value)
            {
                return;
            }

            _hasChanges = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string DisplayName
    {
        get
        {
            string name = FilePath is null
                ? UntitledName
                : Path.GetFileName(FilePath);

            return HasChanges ? $"{name} *" : name;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshDisplayName()
    {
        OnPropertyChanged(nameof(DisplayName));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
