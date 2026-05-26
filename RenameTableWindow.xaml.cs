using System.Windows;

namespace AOTableEditor;

public partial class RenameTableWindow : Window
{
    private readonly string _blankErrorMessage;

    public RenameTableWindow(
        string currentName,
        string title = "Rename Table",
        string prompt = "Table name",
        string actionText = "Rename",
        string blankErrorMessage = "Table name cannot be blank.")
    {
        InitializeComponent();

        _blankErrorMessage = blankErrorMessage;
        Title = title;
        PromptLabel.Text = prompt;
        ActionButton.Content = actionText;
        NameTextBox.Text = currentName;
        NameTextBox.SelectAll();
        NameTextBox.Focus();
    }

    public string TableName => NameTextBox.Text.Trim();
    public string InputValue => TableName;

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TableName))
        {
            ErrorText.Text = _blankErrorMessage;
            return;
        }

        DialogResult = true;
    }
}
