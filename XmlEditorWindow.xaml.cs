using System;
using System.Windows;

namespace AOTableEditor;

public partial class XmlEditorWindow : Window
{
    public XmlEditorWindow(string title, string xml)
    {
        InitializeComponent();

        Title = title;
        HeaderText.Text = title;
        XmlTextBox.Text = xml;
    }

    public Func<string, string?>? ValidateAndUpdate { get; set; }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if (ValidateAndUpdate is null)
        {
            DialogResult = true;
            return;
        }

        try
        {
            string? error = ValidateAndUpdate(XmlTextBox.Text);

            if (!string.IsNullOrWhiteSpace(error))
            {
                ErrorText.Text = error;
                return;
            }

            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }
}
