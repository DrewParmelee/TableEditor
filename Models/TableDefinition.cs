using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AOTableEditor.Models;

public sealed class TableDefinition : INotifyPropertyChanged
{
    private bool _hasChanges;

    public int SourceIndex { get; set; }
    public string Name { get; set; } = "";
    public string Comment { get; set; } = "";
    public string Delimiter { get; set; } = ",";
    public string DataType { get; set; } = "string";
    public int? Decimals { get; set; }
    public List<KeySetDefinition> RowSets { get; set; } = [];
    public List<KeySetDefinition> ColSets { get; set; } = [];
    public string PageSearchType { get; set; } = "=";
    public List<string> PageKeys { get; set; } = [];
    public List<List<string>> DataRows { get; set; } = [];

    public RenderedTable? RenderedTable { get; set; }

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

    public string DisplayName => HasChanges ? $"{Name} *" : Name;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
