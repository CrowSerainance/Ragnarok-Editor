using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ROMapOverlayEditor;

public partial class TownExportDialog : Window
{
    private readonly string _mapName;
    private readonly List<NpcPlacable> _npcs;
    private readonly List<TownNpcInfo>? _originalNpcs;

    public TownExportDialog(string mapName, List<NpcPlacable> npcs, List<TownNpcInfo>? originalNpcs)
    {
        InitializeComponent();
        _mapName = mapName;
        _npcs = npcs;
        _originalNpcs = originalNpcs;
        UpdatePreview();
    }

    private void FormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        int format = FormatCombo.SelectedIndex;
        OutputBox.Text = format switch
        {
            0 => TowninfoExporter.ExportToLuaFormat(_mapName, _npcs),
            1 => TowninfoExporter.ExportToRathenaFormat(_mapName, _npcs),
            2 when _originalNpcs != null => TowninfoExporter.GenerateChangelog(_mapName, _originalNpcs, _npcs),
            2 => "// No original data available for comparison.\n// Import town data first to enable changelog.",
            _ => ""
        };
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(OutputBox.Text);
            MessageBox.Show("Copied to clipboard.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Copy failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Lua files (*.lub)|*.lub|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"{_mapName}_export.lub"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                File.WriteAllText(dlg.FileName, OutputBox.Text);
                MessageBox.Show($"Saved to {dlg.FileName}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Save failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
