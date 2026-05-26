using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace AOTableEditor;

public partial class SetEditorWindow : Window
{
    private readonly bool _showName;

    public SetEditorWindow(
        string title,
        string name,
        IReadOnlyList<string> values,
        string valuesLabel = "Headers",
        string actionText = "Update",
        bool showName = true)
    {
        InitializeComponent();

        _showName = showName;
        Title = title;
        NameTextBox.Text = name;
        ValuesLabel.Text = valuesLabel;
        ValuesTextBox.Text = string.Join("\r\n", values);
        ActionButton.Content = actionText;

        if (!showName)
        {
            NameLabel.Visibility = Visibility.Collapsed;
            NameTextBox.Visibility = Visibility.Collapsed;
            NameLabelRow.Height = new GridLength(0);
            NameInputRow.Height = new GridLength(0);
            ValuesTextBox.Focus();
        }
        else
        {
            NameTextBox.SelectAll();
            NameTextBox.Focus();
        }
    }

    public string SetName => NameTextBox.Text.Trim();

    public List<string> Values => ValuesTextBox.Text
        .Replace("\r\n", "\n")
        .Replace('\r', '\n')
        .Split('\n')
        .Select(value => value.Trim())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .ToList();

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_showName && string.IsNullOrWhiteSpace(SetName))
        {
            ErrorText.Text = "Set name cannot be blank.";
            return;
        }

        if (Values.Count == 0)
        {
            ErrorText.Text = "Enter at least one value.";
            return;
        }

        DialogResult = true;
    }
}
