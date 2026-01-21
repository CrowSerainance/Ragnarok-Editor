using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ROMapOverlayEditor.Grf;

namespace ROMapOverlayEditor;

public partial class GrfBrowserWindow : Window
{
    public string? SelectedGrfPath { get; private set; }
    public string? SelectedInternalPath { get; private set; }

    private GrfArchive? _grf;
    private string[] _allPaths = Array.Empty<string>();
    private readonly ObservableCollection<GrfListEntry> _filtered = new();

    public GrfBrowserWindow()
    {
        InitializeComponent();
        EntriesList.ItemsSource = _filtered;
        // Hide local Open button if we are using external logic, or reuse it?
        // We will repurpose Open button to call back to logic, or just local logic.
        // For simplicity, local open will just use GrfArchive.Open logic.
    }

    // New method to receive the validated archive
    public void LoadArchive(GrfArchive archive)
    {
        _grf = archive; // do not dispose, ownership shared or logic handles it
        // Ideally we shouldn't dispose it here if passed in, unless we took ownership
        // But MainWindow passes it in.
        
        ReloadFromCurrent();
    }

    private void ReloadFromCurrent()
    {
        if (_grf == null) return;
        try
        {
            _allPaths = _grf.Entries
                .Select(e => e.Path)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            TxtPath.Text = _grf.Path;
            TxtVersion.Text = $"Version {_grf.VersionHex} · {_allPaths.Length} files";
            RefreshList();
            UpdateOpenAsMapState();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to list GRF:\n{ex.Message}", "Error");
        }
    }

    private void BtnOpenGrf_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "GRF (*.grf)|*.grf|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        LoadGrf(dlg.FileName);
    }

    private void LoadGrf(string path)
    {
        try
        {
            // Use robust loader
            var arc = GrfArchive.Open(path);
            // Dispose old if any (assuming we own local opens)
            // But if _grf was passed from outside? 
            // Simple rule: if we overwrite _grf, we should dispose the old one IF we owned it.
            // For now, let's just dispose it.
            _grf?.Dispose();
            
            _grf = arc;
            ReloadFromCurrent();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open GRF:\n{ex.Message}", "Error");
        }
    }

    private void FilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TxtSearch != null) RefreshList();
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshList();
    }

    private void RefreshList()
    {
        _filtered.Clear();
        bool mapsOnly = FilterCombo?.SelectedIndex == 1;
        string search = (TxtSearch?.Text ?? "").Trim();

        foreach (var p in _allPaths)
        {
            if (mapsOnly && !IsMapBmp(p)) continue;
            if (search.Length > 0 && p.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
            _filtered.Add(new GrfListEntry(p, IsMapBmp(p)));
        }
    }

    private static bool IsMapBmp(string path)
    {
        if (!path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)) return false;
        return path.Contains("\\map\\", StringComparison.OrdinalIgnoreCase) || path.Contains("/map/", StringComparison.OrdinalIgnoreCase);
    }

    private void EntriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateOpenAsMapState();
        TryLoadPreview();
    }

    private void TryLoadPreview()
    {
        if (ImgPreview == null || TxtPreviewData == null || TxtContentPreview == null) return;
        
        ImgPreview.Source = null;
        TxtContentPreview.Text = "";
        
        ImgPreview.Visibility = Visibility.Collapsed;
        TxtContentPreview.Visibility = Visibility.Collapsed;
        TxtPreviewData.Visibility = Visibility.Visible;
        TxtPreviewData.Text = "No preview";

        if (EntriesList.SelectedItem is not GrfListEntry entry || _grf == null) return;

        string ext = System.IO.Path.GetExtension(entry.Path).ToLowerInvariant();
        bool isImage = ext == ".bmp" || ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga";
        bool isText = ext == ".txt" || ext == ".xml" || ext == ".lua" || ext == ".conf" || 
                      ext == ".ini" || ext == ".log" || ext == ".json";

        if (!isImage && !isText) 
        {
            // Try to show size/type info
            TxtPreviewData.Text = $"Binary file\n{ext}\nSize: {_grf.Entries.FirstOrDefault(x => x.Path == entry.Path)?.UncompressedSize ?? 0} bytes";
            return;
        }

        try
        {
            var data = _grf.Extract(entry.Path);
            if (data == null || data.Length == 0)
            {
                TxtPreviewData.Text = "Empty file";
                return;
            }

            if (isImage)
            {
                if (ext == ".tga")
                {
                    TxtPreviewData.Text = "TGA preview not supported";
                    return;
                }

                using var ms = new MemoryStream(data);
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze(); 

                ImgPreview.Source = bmp;
                ImgPreview.Visibility = Visibility.Visible;
                TxtPreviewData.Visibility = Visibility.Collapsed;
            }
            else if (isText)
            {
                // Detect encoding: UTF-8 BOM or fallback to 949
                string text;
                // Simple check for UTF8 BOM
                if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                {
                     text = System.Text.Encoding.UTF8.GetString(data);
                }
                else
                {
                     // Try 949 (Korean) as default for RO files
                     try {
                        text = System.Text.Encoding.GetEncoding(949).GetString(data);
                     } catch {
                        text = System.Text.Encoding.Default.GetString(data);
                     }
                }
                
                // Truncate if too huge for preview
                if (text.Length > 10000) text = text.Substring(0, 10000) + "\n... (truncated)";

                TxtContentPreview.Text = text;
                TxtContentPreview.Visibility = Visibility.Visible;
                TxtPreviewData.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            TxtPreviewData.Text = $"Preview failed:\n{ex.Message}";
        }
    }

    private void UpdateOpenAsMapState()
    {
        var sel = EntriesList?.SelectedItem as GrfListEntry;
        BtnOpenAsMap.IsEnabled = sel != null && sel.IsMap;
    }

    private void EntriesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (EntriesList.SelectedItem is GrfListEntry { IsMap: true } entry)
        {
            CommitOpenAsMap(entry.Path);
        }
    }

    private void BtnOpenAsMap_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is not GrfListEntry { IsMap: true } entry) return;
        CommitOpenAsMap(entry.Path);
    }

    private void CommitOpenAsMap(string internalPath)
    {
        if (_grf == null) return;
        SelectedGrfPath = _grf.Path;
        SelectedInternalPath = internalPath;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public void SetInitialPath(string? path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            LoadGrf(path);
    }
}

public sealed class GrfListEntry
{
    public string Path { get; }
    public bool IsMap { get; }
    public GrfListEntry(string path, bool isMap) { Path = path; IsMap = isMap; }
}
