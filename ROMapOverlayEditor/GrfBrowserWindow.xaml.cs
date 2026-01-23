using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ROMapOverlayEditor.Sources;

namespace ROMapOverlayEditor;

public partial class GrfBrowserWindow : Window
{
    private GrfFileSource? _source;
    public string? SelectedGrfPath { get; private set; }
    public string? SelectedInternalPath { get; private set; }
    public string? SelectedPath => SelectedInternalPath;

    private void CommitSelection(string internalPath)
    {
        if (_source == null) return;
        SelectedGrfPath = _source.GrfPath;
        SelectedInternalPath = internalPath;
        DialogResult = true;
        Close();
    }

    private void EntriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateOpenAsMapState();
        TryLoadPreview();
    }

    private void UpdateOpenAsMapState()
    {
        var sel = EntriesList?.SelectedItem as GrfListEntry;
        BtnOpenAsMap.IsEnabled = sel != null;
    }

    private void EntriesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (EntriesList.SelectedItem is GrfListEntry entry)
        {
            CommitOpenAsMap(entry.Path);
        }
    }

    private void BtnOpenAsMap_Click(object sender, RoutedEventArgs e)
    {
        if (EntriesList.SelectedItem is not GrfListEntry entry) return;
        CommitOpenAsMap(entry.Path);
    }
    
    private bool _ownsSource;
    private string[] _allPaths = Array.Empty<string>();
    private readonly ObservableCollection<GrfListEntry> _filtered = new();

    private void CommitOpenAsMap(string internalPath)
    {
        if (_source == null) return;
        SelectedGrfPath = _source.GrfPath;
        SelectedInternalPath = internalPath;
        DialogResult = true;
        Close();
    }

    public GrfBrowserWindow()
    {
        InitializeComponent();
        EntriesList.ItemsSource = _filtered;
    }

    public GrfBrowserWindow(GrfFileSource source) : this()
    {
        LoadSource(source);
    }

    public void LoadSource(GrfFileSource source)
    {
        // Dispose previous source if we owned it
        if (_ownsSource && _source != null)
        {
            _source.Dispose();
        }

        _source = source;
        _ownsSource = false; // Caller owns it
        ReloadFromCurrent();
    }

    public void LoadGrf(string path)
    {
        try
        {
            var newSource = new GrfFileSource(path);

            // Dispose previous source if we owned it
            if (_ownsSource && _source != null)
            {
                _source.Dispose();
            }

            _source = newSource;
            _ownsSource = true; // We own it
            ReloadFromCurrent();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open GRF:\n{ex.Message}", "Error");
        }
    }

    private void ReloadFromCurrent()
    {
        if (_source == null) return;

        try
        {
            _allPaths = _source.EnumeratePaths()
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            TxtPath.Text = _source.GrfPath;
            TxtVersion.Text = $"Version 0x{_source.Version:X} - {_allPaths.Length} files";
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
        return path.Contains("/map/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("\\map\\", StringComparison.OrdinalIgnoreCase);
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

        if (EntriesList.SelectedItem is not GrfListEntry entry || _source == null) return;

        string ext = System.IO.Path.GetExtension(entry.Path).ToLowerInvariant();
        bool isImage = ext is ".bmp" or ".png" or ".jpg" or ".jpeg" or ".tga";
        bool isText = ext is ".txt" or ".xml" or ".lua" or ".lub" or ".conf" or ".ini" or ".log" or ".json";

        if (!isImage && !isText)
        {
            TxtPreviewData.Text = $"Binary file\n{ext}";
            return;
        }

        try
        {
            var data = _source.ReadAllBytes(entry.Path);
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
                string text;
                // Check for UTF8 BOM
                if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                {
                    text = System.Text.Encoding.UTF8.GetString(data);
                }
                // Check for Lua bytecode
                else if (data.Length >= 4 && data[0] == 0x1B && data[1] == (byte)'L' && data[2] == (byte)'u' && data[3] == (byte)'a')
                {
                    TxtPreviewData.Text = "Compiled Lua bytecode\n(cannot preview)";
                    return;
                }
                else
                {
                    // Try Korean EUC-KR encoding (common for RO files)
                    try
                    {
                        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                        text = System.Text.Encoding.GetEncoding(949).GetString(data);
                    }
                    catch
                    {
                        text = System.Text.Encoding.Default.GetString(data);
                    }
                }

                // Truncate if too large
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

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        // Dispose source if we own it
        if (_ownsSource && _source != null)
        {
            _source.Dispose();
            _source = null;
        }
    }
}

public sealed class GrfListEntry
{
    public string Path { get; }
    public bool IsMap { get; }
    public GrfListEntry(string path, bool isMap) { Path = path; IsMap = isMap; }
}
