using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace AOTableEditor;

public partial class ChoicePromptWindow : Window
{
    public ChoicePromptWindow(string title, string prompt, IReadOnlyList<string> choices)
    {
        InitializeComponent();

        Title = title;
        PromptLabel.Text = prompt;
        ChoicesList.ItemsSource = choices;

        if (choices.Count > 0)
        {
            ChoicesList.SelectedIndex = 0;
        }
    }

    public int SelectedIndex => ChoicesList.SelectedIndex;
    public string? SelectedValue => ChoicesList.SelectedItem as string;

    private void Choose_Click(object sender, RoutedEventArgs e)
    {
        if (ChoicesList.SelectedIndex < 0)
        {
            return;
        }

        DialogResult = true;
    }

    private void ChoicesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ChoicesList.SelectedIndex >= 0)
        {
            DialogResult = true;
        }
    }
}
