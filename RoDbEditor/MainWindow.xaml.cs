using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using GRF.FileFormats.ActFormat;
using GRF.FileFormats.SprFormat;
using GRF.Image;
using Microsoft.Win32;
using RoDbEditor.Core;
using RoDbEditor.Models;
using RoDbEditor.Services;

namespace RoDbEditor;

public partial class MainWindow : Window
{
    private readonly List<AssetEntry> _allAssets = new();
    private string _currentCategory = "ITEMS";

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RefreshList();
            UpdateListLabel();
        };
    }

    private void UpdateListLabel()
    {
        CurrentListLabel.Text = $"CURRENT LIST: {_currentCategory}";
    }

    private void CategoryTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryTabs == null || CategoryTabs.SelectedIndex < 0 || CategoryTabs.SelectedIndex > 4) return;
        var headers = new[] { "ITEMS", "MONSTERS", "NPCs", "MAPS", "QUESTS" };
        _currentCategory = headers[CategoryTabs.SelectedIndex];
        UpdateListLabel();
        RefreshList();
    }

    private void RefreshList()
    {
        if (_currentCategory == "ITEMS")
        {
            var filter = SearchBox.Text?.Trim();
            var items = App.ItemDbService.Search(filter).ToList();
            AssetListBox.ItemsSource = null;
            AssetListBox.ItemsSource = items;
            AssetListBox.DisplayMemberPath = "DisplayName";
            return;
        }

        _allAssets.Clear();
        var grf = App.GrfService;
        if (grf?.Reader?.FileTable == null)
        {
            AssetListBox.ItemsSource = null;
            AssetListBox.ItemsSource = _allAssets;
            AssetListBox.DisplayMemberPath = "DisplayName";
            return;
        }

        string dir;
        string[] exts;
        switch (_currentCategory)
        {
            case "MONSTERS":
            case "NPCs":
                dir = "data\\sprite";
                exts = new[] { ".act", ".spr" };
                break;
            case "MAPS":
                dir = "data";
                exts = new[] { ".gat", ".rsw" };
                break;
            default:
                dir = "data";
                exts = new[] { ".txt", ".lua" };
                break;
        }

        try
        {
            var files = grf.GetFiles(dir).ToList();
            foreach (var path in files)
            {
                var ext = Path.GetExtension(path)?.ToLowerInvariant();
                if (ext == null || !exts.Contains(ext)) continue;
                var name = Path.GetFileName(path);
                _allAssets.Add(new AssetEntry { Path = path, DisplayName = name });
            }
        }
        catch { }

        _allAssets.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        AssetListBox.ItemsSource = null;
        AssetListBox.ItemsSource = _allAssets;
        AssetListBox.DisplayMemberPath = "DisplayName";
    }

    private void AssetListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AssetListBox.SelectedItem is ItemEntry itemEntry)
        {
            ShowItemDetails(itemEntry);
            return;
        }

        if (AssetListBox.SelectedItem is AssetEntry entry)
        {
            ShowAssetDetails(entry);
            return;
        }

        ClearDetails();
    }

    private void ShowItemDetails(ItemEntry item)
    {
        DetailName.Text = "NAME: " + item.DisplayName;
        DetailId.Text = "ID: " + item.Id;
        DetailType.Text = "TYPE: " + item.Type;
        DetailDescription.Text = App.ItemInfoDescriptions.TryGetValue(item.Id, out var desc) ? desc : "";
        DetailDescription.IsReadOnly = true;
        DetailScript.Text = item.Script ?? "";
        DetailScript.IsReadOnly = false;
        SaveButton.Visibility = Visibility.Visible;

        PreviewImage.Source = LoadItemIcon(item);
    }

    private BitmapSource? LoadItemIcon(ItemEntry item)
    {
        var grf = App.GrfService;
        if (grf == null) return null;
        var paths = new[]
        {
            $"data\\texture\\effect\\{item.Id}.bmp",
            $"data\\texture\\effect\\{item.AegisName}.bmp",
            $"data\\texture\\effect\\item\\{item.Id}.bmp"
        };
        foreach (var rel in paths)
        {
            var data = grf.GetData(rel);
            if (data == null || data.Length == 0) continue;
            var ext = Path.GetExtension(rel)?.ToLowerInvariant();
            try
            {
                var img = ImageProvider.GetImage(data, ext ?? ".bmp");
                if (img != null)
                    return img.Cast<BitmapSource>();
            }
            catch { }
        }
        return null;
    }

    private void ShowAssetDetails(AssetEntry entry)
    {
        DetailName.Text = "NAME: " + (entry.DisplayName ?? entry.Path);
        DetailId.Text = "ID: —";
        DetailType.Text = "TYPE: —";
        DetailDescription.Text = "";
        DetailDescription.IsReadOnly = true;
        DetailScript.Text = "{},{},{}";
        DetailScript.IsReadOnly = true;
        SaveButton.Visibility = Visibility.Collapsed;

        var path = entry.Path ?? "";
        var data = App.GrfService.GetData(path);
        if (data == null || data.Length == 0)
        {
            PreviewImage.Source = null;
            return;
        }

        var ext = Path.GetExtension(entry.Path)?.ToLowerInvariant();
        BitmapSource? preview = null;
        try
        {
            if (ext == ".bmp" || ext == ".png" || ext == ".tga" || ext == ".jpg")
            {
                var img = ImageProvider.GetImage(data, ext);
                if (img != null) preview = img.Cast<BitmapSource>();
            }
            else if (ext == ".spr")
            {
                var img = ImageProvider.GetImage(data, ".spr", firstImageOnly: true);
                if (img != null) preview = img.Cast<BitmapSource>();
            }
            else if (ext == ".act")
            {
                var sprPath = Path.ChangeExtension(path, ".spr");
                var sprData = App.GrfService.GetData(sprPath);
                if (sprData != null && sprData.Length > 0)
                {
                    try
                    {
                        using var act = new Act(data, sprData);
                        if (act.NumberOfActions > 0 && act[0].Frames.Count > 0)
                        {
                            var frame = act[0].Frames[0];
                            if (frame.Layers.Count > 0)
                            {
                                var layer = frame.Layers[0];
                                if (layer.SpriteIndex >= 0 && layer.SpriteIndex < act.Sprite.Images.Count)
                                    preview = act.Sprite.Images[layer.SpriteIndex].Cast<BitmapSource>();
                            }
                        }
                    }
                    catch
                    {
                        try
                        {
                            var spr = new Spr(sprData);
                            if (spr.Images.Count > 0)
                                preview = spr.Images[0].Cast<BitmapSource>();
                        }
                        catch { }
                    }
                }
            }
            else if (ext == ".gat")
            {
                var img = ImageProvider.GetImage(data, ".gat");
                if (img != null) preview = img.Cast<BitmapSource>();
            }

            if (preview != null) preview.Freeze();
        }
        catch { }

        PreviewImage.Source = preview;
    }

    private void ClearDetails()
    {
        PreviewImage.Source = null;
        DetailName.Text = "NAME: (select an asset)";
        DetailId.Text = "ID: —";
        DetailType.Text = "TYPE: —";
        DetailDescription.Text = "";
        DetailScript.Text = "{},{},{}";
        DetailDescription.IsReadOnly = true;
        DetailScript.IsReadOnly = true;
        SaveButton.Visibility = Visibility.Collapsed;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (AssetListBox.SelectedItem is not ItemEntry item) return;
        item.Script = DetailScript.Text?.Trim();
        App.ItemDbService.SaveItem(item);
        System.Windows.MessageBox.Show(this, "Item saved.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshList();
    }

    private void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            RefreshList();
    }

    private void MenuOpenGrf_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open GRF",
            Filter = "GRF files|*.grf;*.rgz;*.gpf|All files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog(this) != true) return;
        foreach (var path in dlg.FileNames)
            App.GrfService.AddGrfPath(path);
        RefreshList();
    }

    private void MenuOpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select server data folder (contains db/, system/)",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var path = dlg.SelectedPath;
        if (string.IsNullOrEmpty(path)) return;
        App.Config.DataPath = path;
        App.ReloadDataPath(path);
        RefreshList();
        System.Windows.MessageBox.Show(this, $"Loaded items from {path}. Items: {App.ItemDbService.Items.Count}.", "RoDbEditor", MessageBoxButton.OK);
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ClearDetails();
        AssetListBox.SelectedItem = null;
    }
}

public class AssetEntry
{
    public string? Path { get; set; }
    public string? DisplayName { get; set; }
}
