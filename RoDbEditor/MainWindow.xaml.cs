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
    private string _originalItemScript = "";
    private string _originalMonsterDropsText = "";
    private string _originalNpcScript = "";

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
        if (CurrentListLabel == null)
        {
            // optionally log or defer the update
            return;
        }
        CurrentListLabel.Text = $"CURRENT LIST: {_currentCategory}";
    }

    private void CategoryTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryTabs == null || CategoryTabs.SelectedIndex < 0 || CategoryTabs.SelectedIndex > 4) return;
        var headers = new[] { "ITEMS", "MONSTERS", "NPCs", "MAPS", "QUESTS" };
        _currentCategory = headers[CategoryTabs.SelectedIndex];

        if (NpcMapFilterPanel != null)
            NpcMapFilterPanel.Visibility = _currentCategory == "NPCs" ? Visibility.Visible : Visibility.Collapsed;

        if (_currentCategory == "NPCs")
        {
            if (NpcMapCombo == null)
                return;

            try
            {
                NpcMapCombo.ItemsSource = null;
                var mapNames = App.NpcIndexService?.GetMapNames();
                NpcMapCombo.ItemsSource = mapNames;
                if (mapNames != null && mapNames.Any())
                    NpcMapCombo.SelectedIndex = 0;
                else
                    NpcMapCombo.SelectedIndex = -1;
            }
            catch
            {
                // Swallow exceptions to avoid breaking UI; leave combo empty.
                NpcMapCombo.ItemsSource = null;
                NpcMapCombo.SelectedIndex = -1;
            }
        }

        UpdateListLabel();
        RefreshList();
    }

    private void RefreshList()
    {
        if (SearchBox == null || AssetListBox == null)
            return;

        if (_currentCategory == "ITEMS")
        {
            var filter = SearchBox.Text?.Trim();
            var items = App.ItemDbService.Search(filter).ToList();
            AssetListBox.ItemsSource = null;
            AssetListBox.ItemsSource = items;
            AssetListBox.DisplayMemberPath = "DisplayName";
            return;
        }

        if (_currentCategory == "MONSTERS")
        {
            var filter = SearchBox.Text?.Trim();
            var mobs = App.MobDbService.Search(filter).ToList();
            AssetListBox.ItemsSource = null;
            AssetListBox.ItemsSource = mobs;
            AssetListBox.DisplayMemberPath = "DisplayName";
            return;
        }

        if (_currentCategory == "NPCs")
        {
            var map = (NpcMapCombo?.SelectedItem as string) ?? "";
            var npcs = App.NpcIndexService.GetNpcsOnMap(map).ToList();
            var filter = SearchBox.Text?.Trim();
            if (!string.IsNullOrEmpty(filter))
                npcs = npcs.Where(n => (n.Name?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                        (n.Map?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
            AssetListBox.ItemsSource = null;
            AssetListBox.ItemsSource = npcs;
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
            ItemDetailsPanel.Visibility = Visibility.Visible;
            MonsterDetailsPanel.Visibility = Visibility.Collapsed;
            ShowItemDetails(itemEntry);
            return;
        }

        if (AssetListBox.SelectedItem is MobEntry mobEntry)
        {
            ItemDetailsPanel.Visibility = Visibility.Collapsed;
            MonsterDetailsPanel.Visibility = Visibility.Visible;
            ShowMonsterDetails(mobEntry);
            return;
        }

        if (AssetListBox.SelectedItem is NpcScriptEntry npcEntry)
        {
            ItemDetailsPanel.Visibility = Visibility.Collapsed;
            MonsterDetailsPanel.Visibility = Visibility.Collapsed;
            NpcDetailsPanel.Visibility = Visibility.Visible;
            ShowNpcDetails(npcEntry);
            return;
        }

        if (AssetListBox.SelectedItem is AssetEntry entry)
        {
            ItemDetailsPanel.Visibility = Visibility.Visible;
            MonsterDetailsPanel.Visibility = Visibility.Collapsed;
            NpcDetailsPanel.Visibility = Visibility.Collapsed;
            ShowAssetDetails(entry);
            return;
        }

        ItemDetailsPanel.Visibility = Visibility.Visible;
        MonsterDetailsPanel.Visibility = Visibility.Collapsed;
        NpcDetailsPanel.Visibility = Visibility.Collapsed;
        ClearDetails();
    }

    private void ShowNpcDetails(NpcScriptEntry npc)
    {
        NpcDetailName.Text = "NAME: " + npc.DisplayName;
        NpcDetailMapPos.Text = "Map (X,Y): " + npc.Map + " (" + npc.X + ", " + npc.Y + ")";
        NpcDetailType.Text = "TYPE: " + npc.Type;
        NpcShopPanel.Visibility = npc.Type == NpcScriptType.Shop ? Visibility.Visible : Visibility.Collapsed;
        NpcWarpPanel.Visibility = npc.Type == NpcScriptType.Warp ? Visibility.Visible : Visibility.Collapsed;
        if (npc.Type == NpcScriptType.Shop)
        {
            NpcShopGrid.ItemsSource = null;
            NpcShopGrid.ItemsSource = npc.ShopItems;
        }
        if (npc.Type == NpcScriptType.Warp)
        {
            if (npc.WarpTarget != null)
            {
                NpcWarpMap.Text = npc.WarpTarget.Map;
                NpcWarpX.Text = npc.WarpTarget.X.ToString();
                NpcWarpY.Text = npc.WarpTarget.Y.ToString();
            }
            else
            {
                NpcWarpMap.Text = "";
                NpcWarpX.Text = "0";
                NpcWarpY.Text = "0";
            }
        }
        if (App.RagnarokScriptHighlighting != null)
            NpcScriptEditor.SyntaxHighlighting = App.RagnarokScriptHighlighting;
        _originalNpcScript = npc.Type == NpcScriptType.Script ? npc.ScriptBody : npc.RawLine;
        if (npc.Type == NpcScriptType.Script)
            NpcScriptEditor.Text = npc.ScriptBody;
        else
            NpcScriptEditor.Text = npc.RawLine;
        NpcDiffExpander.Visibility = Visibility.Visible;
        NpcDiffTextBox.Text = "";
        
        // Load NPC sprite (animated)
        CenterPreviewImage.Visibility = Visibility.Collapsed;
        SpriteViewer.Visibility = Visibility.Visible;
        
        var (actPath, sprPath) = App.SpriteLookupService.FindNpcSprite(npc.SpriteId);
        if (actPath != null && sprPath != null)
        {
            var (actData, sprData) = App.SpriteLookupService.GetSpriteData(actPath, sprPath);
            SpriteViewer.LoadFromData(actData, sprData);
            SpriteViewer.Play();
        }
        else
        {
            SpriteViewer.Stop();
            // Try fallback logic or clear
            SpriteViewer.LoadFromData(null, null);
        }
    }

    private void NpcMapCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_currentCategory == "NPCs") RefreshList();
    }

    private void NpcSaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (AssetListBox.SelectedItem is not NpcScriptEntry npc) return;
        if (npc.Type == NpcScriptType.Shop)
        {
            npc.ShopItems = NpcShopGrid.Items.Cast<ShopItemEntry>().ToList();
        }
        else if (npc.Type == NpcScriptType.Warp)
        {
            if (npc.WarpTarget == null) npc.WarpTarget = new WarpTarget();
            npc.WarpTarget.Map = NpcWarpMap.Text?.Trim() ?? "";
            int.TryParse(NpcWarpX.Text, out var x);
            int.TryParse(NpcWarpY.Text, out var y);
            npc.WarpTarget.X = x;
            npc.WarpTarget.Y = y;
        }
        else if (npc.Type == NpcScriptType.Script)
        {
            npc.ScriptBody = NpcScriptEditor.Text ?? "";
        }
        App.NpcIndexService.SaveNpc(npc);
        System.Windows.MessageBox.Show(this, "NPC saved.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void NpcCancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (AssetListBox.SelectedItem is NpcScriptEntry npc)
            ShowNpcDetails(npc);
    }

    private void ShowMonsterDetails(MobEntry mob)
    {
        MonsterDetailName.Text = "NAME: " + mob.DisplayName;
        MonsterDetailId.Text = "ID: " + mob.Id + " (" + mob.AegisName + ")";
        MonsterDetailLevelHp.Text = "Level: " + mob.Level + "  HP: " + mob.Hp;
        MonsterDropsGrid.ItemsSource = null;
        MonsterDropsGrid.ItemsSource = mob.Drops;
        MonsterMvpDropsGrid.ItemsSource = null;
        MonsterMvpDropsGrid.ItemsSource = mob.MvpDrops;
        _originalMonsterDropsText = SerializeMobDrops(mob);
        MonsterDiffExpander.Visibility = Visibility.Visible;
        MonsterDiffTextBox.Text = "";
        var spawns = App.SpawnParser.GetSpawnsForMob(mob.Id).ToList();
        MonsterSpawnListBox.ItemsSource = spawns;
        
        // Load animated sprite
        CenterPreviewImage.Visibility = Visibility.Collapsed;
        SpriteViewer.Visibility = Visibility.Visible;
        
        var (actPath, sprPath) = App.SpriteLookupService.FindMonsterSprite(mob.AegisName);
        var (actData, sprData) = App.SpriteLookupService.GetSpriteData(actPath, sprPath);
        SpriteViewer.LoadFromData(actData, sprData);
        SpriteViewer.Play();
    }

    private static string SerializeMobDrops(MobEntry mob)
    {
        var lines = new List<string>();
        foreach (var d in mob.Drops) lines.Add($"Drop: {d.Item} Rate={d.Rate} Steal={d.StealProtected}");
        foreach (var d in mob.MvpDrops) lines.Add($"MvpDrop: {d.Item} Rate={d.Rate}");
        return string.Join("\r\n", lines);
    }



    private void MonsterSaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (AssetListBox.SelectedItem is not MobEntry mob) return;
        mob.Drops = MonsterDropsGrid.Items.Cast<MobDropEntry>().ToList();
        mob.MvpDrops = MonsterMvpDropsGrid.Items.Cast<MobDropEntry>().ToList();
        App.MobDbService.SaveMob(mob);
        System.Windows.MessageBox.Show(this, "Monster saved.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void MonsterCancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (AssetListBox.SelectedItem is MobEntry mob)
        {
            MonsterDropsGrid.ItemsSource = null;
            MonsterDropsGrid.ItemsSource = mob.Drops;
            MonsterMvpDropsGrid.ItemsSource = null;
            MonsterMvpDropsGrid.ItemsSource = mob.MvpDrops;
        }
    }

    private void ShowItemDetails(ItemEntry item)
    {
        DetailName.Text = "NAME: " + item.DisplayName;
        DetailId.Text = "ID: " + item.Id;
        DetailType.Text = "TYPE: " + item.Type;
        DetailDescription.Text = App.ItemInfoDescriptions.TryGetValue(item.Id, out var desc) ? desc : "";
        DetailDescription.IsReadOnly = true;
        _originalItemScript = item.Script ?? "";
        DetailScript.Text = _originalItemScript;
        DetailScript.IsReadOnly = false;
        SaveButton.Visibility = Visibility.Visible;
        ItemDiffExpander.Visibility = Visibility.Visible;
        ItemDiffTextBox.Text = "";
        ItemDiffTextBox.Text = "";
        
        CenterPreviewImage.Visibility = Visibility.Visible;
        SpriteViewer.Visibility = Visibility.Collapsed;
        SpriteViewer.Stop();
        CenterPreviewImage.Source = LoadItemIcon(item);
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
        ItemDiffExpander.Visibility = Visibility.Collapsed;


        var path = entry.Path ?? "";
        
        // Determine type
        var ext = Path.GetExtension(path)?.ToLowerInvariant();
        
        if (ext == ".act" || ext == ".spr")
        {
            CenterPreviewImage.Visibility = Visibility.Collapsed;
            SpriteViewer.Visibility = Visibility.Visible;
            
            var actPath = ext == ".act" ? path : Path.ChangeExtension(path, ".act");
            var sprPath = ext == ".spr" ? path : Path.ChangeExtension(path, ".spr");
            
            var (actData, sprData) = App.SpriteLookupService.GetSpriteData(actPath, sprPath);
            SpriteViewer.LoadFromData(actData, sprData);
            SpriteViewer.Play();
            return;
        }

        CenterPreviewImage.Visibility = Visibility.Visible;
        SpriteViewer.Visibility = Visibility.Collapsed;
        SpriteViewer.Stop();

        var data = App.GrfService.GetData(path);
        if (data == null || data.Length == 0)
        {
            CenterPreviewImage.Source = null;
            return;
        }

        ext = Path.GetExtension(entry.Path)?.ToLowerInvariant();
        BitmapSource? preview = null;
        try
        {
            if (ext == ".bmp" || ext == ".png" || ext == ".tga" || ext == ".jpg")
            {
                var img = ImageProvider.GetImage(data, ext);
                if (img != null) preview = img.Cast<BitmapSource>();
            }
            else if (ext == ".gat")
            {
                var img = ImageProvider.GetImage(data, ".gat");
                if (img != null) preview = img.Cast<BitmapSource>();
            }

            if (preview != null) preview.Freeze();
        }
        catch { }

        CenterPreviewImage.Source = preview;
    }

    private void ClearDetails()
    {
        CenterPreviewImage.Source = null;
        CenterPreviewImage.Visibility = Visibility.Visible;
        SpriteViewer.Visibility = Visibility.Collapsed;
        SpriteViewer.Stop();
        
        DetailName.Text = "NAME: (select an asset)";
        DetailId.Text = "ID: —";
        DetailType.Text = "TYPE: —";
        DetailDescription.Text = "";
        DetailScript.Text = "{},{},{}";
        DetailDescription.IsReadOnly = true;
        DetailScript.IsReadOnly = true;
        SaveButton.Visibility = Visibility.Collapsed;
        ItemDiffExpander.Visibility = Visibility.Collapsed;
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

        int loadedCount = 0;
        foreach (var path in dlg.FileNames)
        {
            App.GrfService.AddGrfPath(path);
            loadedCount++;
        }

        // Clear sprite cache so it rebuilds with new GRF
        App.SpriteLookupService.ClearCache();
        RefreshList();

        // Show status
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"GRF files added: {loadedCount}");
        sb.AppendLine($"Total GRF paths: {App.GrfService.GrfPaths.Count}");
        sb.AppendLine($"GRF loaded: {App.GrfService.IsLoaded}");

        if (App.GrfService.IsLoaded)
        {
            // Try to get a file count
            try
            {
                var testFiles = App.GrfService.GetFiles("data", null, SearchOption.TopDirectoryOnly).Take(10).ToList();
                sb.AppendLine($"Sample files in data\\: {testFiles.Count} (showing first 10)");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Error reading GRF: {ex.Message}");
            }
        }

        System.Windows.MessageBox.Show(this, sb.ToString(), "RoDbEditor - GRF Loaded", MessageBoxButton.OK);
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

        // Build a status message with details
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Data folder: {path}");
        sb.AppendLine($"Items loaded: {App.ItemDbService.Items.Count}");
        sb.AppendLine($"Mobs loaded: {App.MobDbService.Mobs.Count}");

        if (!string.IsNullOrEmpty(App.ItemDbService.LastError))
            sb.AppendLine($"\nItem DB Error: {App.ItemDbService.LastError}");
        if (!string.IsNullOrEmpty(App.MobDbService.LastError))
            sb.AppendLine($"\nMob DB Error: {App.MobDbService.LastError}");

        System.Windows.MessageBox.Show(this, sb.ToString(), "RoDbEditor - Load Results", MessageBoxButton.OK);
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ClearDetails();
        AssetListBox.SelectedItem = null;
    }

    private void DiffExpander_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander exp)
        {
            if (exp == ItemDiffExpander)
            {
                var current = DetailScript.Text ?? "";
                ItemDiffTextBox.Text = SimpleDiff.HasChanges(_originalItemScript, current)
                    ? SimpleDiff.ToUnifiedDiff(_originalItemScript, current)
                    : " (no changes)";
            }
            else if (exp == MonsterDiffExpander && AssetListBox.SelectedItem is MobEntry mob)
            {
                var current = GetCurrentMonsterDropsText();
                MonsterDiffTextBox.Text = SimpleDiff.HasChanges(_originalMonsterDropsText, current)
                    ? SimpleDiff.ToUnifiedDiff(_originalMonsterDropsText, current)
                    : " (no changes)";
            }
            else if (exp == NpcDiffExpander)
            {
                var current = NpcScriptEditor.Text ?? "";
                NpcDiffTextBox.Text = SimpleDiff.HasChanges(_originalNpcScript, current)
                    ? SimpleDiff.ToUnifiedDiff(_originalNpcScript, current)
                    : " (no changes)";
            }
        }
    }

    private void DiffExpander_Collapsed(object sender, RoutedEventArgs e) { }

    private string GetCurrentMonsterDropsText()
    {
        var lines = new List<string>();
        foreach (MobDropEntry d in MonsterDropsGrid.Items.Cast<MobDropEntry>())
            lines.Add($"Drop: {d.Item} Rate={d.Rate} Steal={d.StealProtected}");
        foreach (MobDropEntry d in MonsterMvpDropsGrid.Items.Cast<MobDropEntry>())
            lines.Add($"MvpDrop: {d.Item} Rate={d.Rate}");
        return string.Join("\r\n", lines);
    }

    private void ItemExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (AssetListBox.SelectedItem is not ItemEntry item) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export item",
            Filter = "YAML|*.yml|All|*.*",
            FileName = $"item_{item.Id}_{item.AegisName}.yml"
        };
        if (dlg.ShowDialog(this) != true) return;
        var script = DetailScript.Text ?? item.Script ?? "";
        var yaml = $"# Item {item.Id} {item.DisplayName}\nId: {item.Id}\nAegisName: {item.AegisName}\nScript: {script}\n";
        File.WriteAllText(dlg.FileName, yaml);
        System.Windows.MessageBox.Show(this, "Exported.", "RoDbEditor", MessageBoxButton.OK);
    }

    private void MonsterExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (AssetListBox.SelectedItem is not MobEntry mob) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export monster",
            Filter = "YAML|*.yml|Text|*.txt|All|*.*",
            FileName = $"mob_{mob.Id}_{mob.AegisName}.yml"
        };
        if (dlg.ShowDialog(this) != true) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Mob {mob.Id} {mob.DisplayName}");
        sb.AppendLine($"Id: {mob.Id}");
        sb.AppendLine($"AegisName: {mob.AegisName}");
        sb.AppendLine("Drops:");
        foreach (MobDropEntry d in MonsterDropsGrid.Items.Cast<MobDropEntry>())
            sb.AppendLine($"  - Item: {d.Item}  Rate: {d.Rate}");
        sb.AppendLine("MvpDrops:");
        foreach (MobDropEntry d in MonsterMvpDropsGrid.Items.Cast<MobDropEntry>())
            sb.AppendLine($"  - Item: {d.Item}  Rate: {d.Rate}");
        File.WriteAllText(dlg.FileName, sb.ToString());
        System.Windows.MessageBox.Show(this, "Exported.", "RoDbEditor", MessageBoxButton.OK);
    }

    private void NpcExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (AssetListBox.SelectedItem is not NpcScriptEntry npc) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export NPC script",
            Filter = "Text|*.txt|All|*.*",
            FileName = $"npc_{npc.Name}.txt"
        };
        if (dlg.ShowDialog(this) != true) return;
        var content = NpcScriptEditor.Text ?? (npc.Type == NpcScriptType.Script ? npc.ScriptBody : npc.RawLine);
        File.WriteAllText(dlg.FileName, content);
        System.Windows.MessageBox.Show(this, "Exported.", "RoDbEditor", MessageBoxButton.OK);
    }
}

public class AssetEntry
{
    public string? Path { get; set; }
    public string? DisplayName { get; set; }
}
