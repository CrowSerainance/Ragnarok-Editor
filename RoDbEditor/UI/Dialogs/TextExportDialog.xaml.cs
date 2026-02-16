using System;
using System.IO;
using System.Windows;

namespace RoDbEditor.UI.Dialogs;

public partial class TextExportDialog : Window
{
    private readonly string _content;
    private readonly string _defaultFileName;

    public TextExportDialog(Window owner, string title, string header, string content, string defaultFileName)
    {
        InitializeComponent();

        Owner = owner;
        Title = title;
        HeaderText.Text = header;

        _content = content ?? string.Empty;
        _defaultFileName = string.IsNullOrWhiteSpace(defaultFileName) ? "export.txt" : defaultFileName;

        ExportTextBox.Text = _content;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(_content);
            System.Windows.MessageBox.Show(this, "Copied to clipboard.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"Copy failed: {ex.Message}", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save blueprint export",
            Filter = "Text|*.txt|All|*.*",
            FileName = _defaultFileName
        };

        if (dlg.ShowDialog(this) != true) return;

        File.WriteAllText(dlg.FileName, _content);
        System.Windows.MessageBox.Show(this, "Saved.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
