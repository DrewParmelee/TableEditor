using AOTableEditor.Models;
using AOTableEditor.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace AOTableEditor;

public partial class MetadataWindow : Window
{
    public static IReadOnlyList<string> LineOfBusinessChoices { get; } =
        ["", .. TableMetadataService.LineOfBusinessOptions];

    public static IReadOnlyList<string> StateChoices { get; } =
        ["", .. TableMetadataService.StateOptions];

    private readonly MetadataEditorModel _model;

    public MetadataWindow(TableFileMetadata metadata)
    {
        InitializeComponent();

        _model = new MetadataEditorModel(metadata);
        DataContext = _model;
    }

    public TableFileMetadata? Result { get; private set; }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        Result = new TableFileMetadata(
            _model.LineOfBusiness,
            _model.State,
            _model.NewBusinessEffectiveDate,
            _model.RenewalEffectiveDate);
        DialogResult = true;
    }
}

public sealed class MetadataEditorModel : INotifyPropertyChanged
{
    private string _lineOfBusiness;
    private string _state;
    private DateTime? _newBusinessEffectiveDate;
    private DateTime? _renewalEffectiveDate;

    public MetadataEditorModel(TableFileMetadata metadata)
    {
        _lineOfBusiness = MetadataWindow.LineOfBusinessChoices.Contains(metadata.LineOfBusiness)
            ? metadata.LineOfBusiness
            : "";
        _state = MetadataWindow.StateChoices.Contains(metadata.State)
            ? metadata.State
            : "";
        _newBusinessEffectiveDate = metadata.NewBusinessEffectiveDate;
        _renewalEffectiveDate = metadata.RenewalEffectiveDate;
    }

    public string LineOfBusiness
    {
        get => _lineOfBusiness;
        set
        {
            if (_lineOfBusiness == value)
            {
                return;
            }

            _lineOfBusiness = value;
            OnPropertyChanged();
        }
    }

    public string State
    {
        get => _state;
        set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            OnPropertyChanged();
        }
    }

    public DateTime? NewBusinessEffectiveDate
    {
        get => _newBusinessEffectiveDate;
        set
        {
            if (_newBusinessEffectiveDate == value)
            {
                return;
            }

            _newBusinessEffectiveDate = value;
            OnPropertyChanged();
        }
    }

    public DateTime? RenewalEffectiveDate
    {
        get => _renewalEffectiveDate;
        set
        {
            if (_renewalEffectiveDate == value)
            {
                return;
            }

            _renewalEffectiveDate = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
