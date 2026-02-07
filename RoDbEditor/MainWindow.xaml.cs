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

public enum PreviewMode { None, Image, Sprite }

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
            UpdateSourceIndicators();
        };
    }

    private void UpdateSourceIndicators()
    {
        if (SourceIndicator1 == null || SourceIndicator2 == null || SourceIndicator3 == null)
            return;

        var grf = App.GrfService;
        if (grf != null && grf.IsLoaded && grf.GrfPaths.Count > 0)
        {
            var first = System.IO.Path.GetFileName(grf.GrfPaths[0]);
            var grfText = grf.GrfPaths.Count == 1
                ? $"GRF: {first}"
                : $"GRF: {first} (+{grf.GrfPaths.Count - 1} more)";
            if (App.FileSystemSpriteSource != null)
                grfText += $" | Assets: {App.FileSystemSpriteSource.CachedCount} sprites";
            SourceIndicator1.Text = grfText;
        }
        else if (App.FileSystemSpriteSource != null)
            SourceIndicator1.Text = $"Assets: {App.FileSystemSpriteSource.CachedCount} sprites";
        else
            SourceIndicator1.Text = "GRF: Not loaded";

        var dataPath = App.Config?.DataPath;
        if (!string.IsNullOrWhiteSpace(dataPath))
            SourceIndicator2.Text = "rAthena: " + dataPath;
        else
            SourceIndicator2.Text = "rAthena: Not set";

        var itemSvc = App.ItemDbService;
        if (itemSvc != null && itemSvc.Items.Count > 0)
            SourceIndicator3.Text = itemSvc.IsLoadedFromYaml
                ? $"Items: YAML (rAthena) ({itemSvc.Items.Count:N0})"
                : $"Items: iteminfo.lub ({itemSvc.Items.Count:N0})";
        else
            SourceIndicator3.Text = "Items: None";
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
                var items = App.ItemDbService != null ? App.ItemDbService.Search(filter).ToList() : new List<ItemEntry>();
                AssetListBox.ItemsSource = null;
                AssetListBox.ItemsSource = items;
                AssetListBox.DisplayMemberPath = "DisplayName";
                return;
            }

            if (_currentCategory == "MONSTERS")
            {
                var filter = SearchBox.Text?.Trim();
                var mobs = App.MobDbService != null ? App.MobDbService.Search(filter).ToList() : new List<MobEntry>();
                AssetListBox.ItemsSource = null;
                AssetListBox.ItemsSource = mobs;
                AssetListBox.DisplayMemberPath = "DisplayName";
                return;
            }

            if (_currentCategory == "NPCs")
            {
                var map = (NpcMapCombo?.SelectedItem as string) ?? "";
                if (App.NpcIndexService == null)
                    return;
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
    if (grf == null || !grf.IsLoaded)
    {
        AssetListBox.ItemsSource = null;
        AssetListBox.ItemsSource = _allAssets;
        AssetListBox.DisplayMemberPath = "DisplayName";
        return;
    }

    // Only MAPS + QUESTS use GRF file browsing; ITEMS and MONSTERS use their DB lists only
    string dir = "data";
    string[] patterns = _currentCategory switch
    {
        "MAPS" => new[] { "*.rsw", "*.gnd", "*.gat" },
        "QUESTS" => new[] { "*.lua", "*.lub", "*.txt" },
        _ => new[] { "*.*" }
    };

    try
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pat in patterns)
        {
            foreach (var path in grf.GetFiles(dir, pat, SearchOption.AllDirectories))
            {
                if (!seen.Add(path)) continue;

                var name = System.IO.Path.GetFileName(path);
                _allAssets.Add(new AssetEntry { Path = path, DisplayName = name });
            }
        }
    }
    catch
    {
        // ignore
    }

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

        // No selection: show the correct detail panel for current tab
        NpcDetailsPanel.Visibility = Visibility.Collapsed;
        if (_currentCategory == "MONSTERS")
        {
            ItemDetailsPanel.Visibility = Visibility.Collapsed;
            MonsterDetailsPanel.Visibility = Visibility.Visible;
        }
        else
        {
            ItemDetailsPanel.Visibility = Visibility.Visible;
            MonsterDetailsPanel.Visibility = Visibility.Collapsed;
        }
        ClearDetails();
    }

    private void SetPreviewMode(PreviewMode mode)
    {
        SpriteViewer.Stop();
        switch (mode)
        {
            case PreviewMode.Sprite:
                CenterPreviewImage.Visibility = Visibility.Collapsed;
                SpriteViewer.Visibility = Visibility.Visible;
                CenterPreviewImage.Source = null;
                break;
            case PreviewMode.Image:
                CenterPreviewImage.Visibility = Visibility.Visible;
                SpriteViewer.Visibility = Visibility.Collapsed;
                break;
            default:
                CenterPreviewImage.Visibility = Visibility.Visible;
                SpriteViewer.Visibility = Visibility.Collapsed;
                CenterPreviewImage.Source = null;
                break;
        }
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

        SetPreviewMode(PreviewMode.Sprite);
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

        SetPreviewMode(PreviewMode.Sprite);

        // Debug: Log sprite lookup attempt
        System.Diagnostics.Debug.WriteLine($"[ShowMonsterDetails] Looking for sprite: AegisName={mob.AegisName}");
        System.Diagnostics.Debug.WriteLine($"[ShowMonsterDetails] Sprite cache count: {App.SpriteLookupService?.CachedSpriteCount ?? 0}");

        if (App.SpriteLookupService == null)
        {
            System.Diagnostics.Debug.WriteLine("[ShowMonsterDetails] SpriteLookupService is NULL!");
            SpriteViewer.LoadFromData(null, null);
            return;
        }

        var (actPath, sprPath) = App.SpriteLookupService.FindMonsterSprite(mob.AegisName);
        System.Diagnostics.Debug.WriteLine($"[ShowMonsterDetails] Found paths: ACT={actPath ?? "NULL"}, SPR={sprPath ?? "NULL"}");

        var (actData, sprData) = App.SpriteLookupService.GetSpriteData(actPath, sprPath);
        System.Diagnostics.Debug.WriteLine($"[ShowMonsterDetails] Data sizes: ACT={actData?.Length ?? 0}, SPR={sprData?.Length ?? 0}");

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

        // Try loading item sprite animation first (from extracted assets or GRF)
        bool hasSpritePreview = false;
        if (App.SpriteLookupService != null)
        {
            // Items use 아이템 (item) sprite folder, search by ID or AegisName
            var (actPath, sprPath) = App.SpriteLookupService.FindMonsterSprite(item.AegisName);
            if (actPath == null && sprPath == null)
            {
                // Also try by numeric ID (item sprites are often named by ID)
                (actPath, sprPath) = App.SpriteLookupService.FindMonsterSprite(item.Id.ToString());
            }
            if (actPath != null || sprPath != null)
            {
                var (actData, sprData) = App.SpriteLookupService.GetSpriteData(actPath, sprPath);
                if (sprData != null && sprData.Length > 0)
                {
                    SetPreviewMode(PreviewMode.Sprite);
                    SpriteViewer.LoadFromData(actData, sprData);
                    SpriteViewer.Play();
                    hasSpritePreview = true;
                }
            }
        }

        // Fall back to static item icon
        if (!hasSpritePreview)
        {
            SetPreviewMode(PreviewMode.Image);
            CenterPreviewImage.Source = LoadItemIcon(item);
        }

        if (App.ItemPathService != null && ItemRelatedFilesListBox != null && ItemRelatedFilesExpander != null)
        {
            var related = App.ItemPathService.GetRelatedPaths(item);
            ItemRelatedFilesListBox.ItemsSource = related.Select(r => $"{r.Label}: {r.Path}").ToList();
            ItemRelatedFilesExpander.Visibility = related.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private BitmapSource? LoadItemIcon(ItemEntry item)
    {
        // Try extracted filesystem textures first
        if (App.FileSystemSpriteSource != null)
        {
            var iconPath = App.FileSystemSpriteSource.FindItemIcon(item.Id, item.AegisName);
            if (iconPath != null)
            {
                var iconData = App.FileSystemSpriteSource.GetTextureData(iconPath);
                if (iconData != null && iconData.Length > 0)
                {
                    var iconExt = Path.GetExtension(iconPath)?.ToLowerInvariant() ?? ".bmp";
                    try
                    {
                        var img = ImageProvider.GetImage(iconData, iconExt);
                        if (img != null)
                            return img.Cast<BitmapSource>();
                    }
                    catch { }
                }
            }
        }

        // Fall back to GRF
        var grf = App.GrfService;
        if (grf == null) return null;
        var paths = new[]
        {
            $"data\\texture\\effect\\{item.Id}.bmp",
            $"data\\texture\\effect\\{item.AegisName}.bmp",
            $"data\\texture\\effect\\item\\{item.Id}.bmp",
            $"data\\texture\\effect\\collection\\{item.Id}.bmp",
            $"data\\texture\\effect\\collection\\{item.AegisName}.bmp",
            $@"data\texture\유저인터페이스\item\{item.Id}.bmp",
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
        var path = entry.Path ?? "";
        var displayName = entry.DisplayName ?? path;
        if (App.ItemPathService != null && App.ItemPathService.IsItemRelatedPath(path))
        {
            var item = App.ItemPathService.TryGetItemForPath(path);
            if (item != null)
                displayName = $"{displayName} — Item: {item.DisplayName} (ID {item.Id})";
        }
        DetailName.Text = "NAME: " + displayName;
        DetailId.Text = "ID: —";
        DetailType.Text = "TYPE: —";
        DetailDescription.Text = "";
        DetailDescription.IsReadOnly = true;
        DetailScript.Text = "{},{},{}";
        DetailScript.IsReadOnly = true;
        SaveButton.Visibility = Visibility.Collapsed;
        ItemDiffExpander.Visibility = Visibility.Collapsed;


        var ext = Path.GetExtension(path)?.ToLowerInvariant();

        if (ext == ".act" || ext == ".spr")
        {
            SetPreviewMode(PreviewMode.Sprite);
            var actPath = ext == ".act" ? path : Path.ChangeExtension(path, ".act");
            var sprPath = ext == ".spr" ? path : Path.ChangeExtension(path, ".spr");
            var (actData, sprData) = App.SpriteLookupService.GetSpriteData(actPath, sprPath);
            SpriteViewer.LoadFromData(actData, sprData);
            SpriteViewer.Play();
            return;
        }

        SetPreviewMode(PreviewMode.Image);

        byte[]? data = null;
        string? loadExt = ext;

        if (ext == ".rsw")
        {
            var gatPath = Path.ChangeExtension(path, ".gat");
            data = App.GrfService.GetData(gatPath);
            loadExt = ".gat";
        }
        else
        {
            data = App.GrfService.GetData(path);
        }

        if (data == null || data.Length == 0)
        {
            CenterPreviewImage.Source = null;
            return;
        }

        BitmapSource? preview = null;
        try
        {
            if (loadExt == ".bmp" || loadExt == ".png" || loadExt == ".tga" || loadExt == ".jpg")
            {
                var img = ImageProvider.GetImage(data, loadExt);
                if (img != null) preview = img.Cast<BitmapSource>();
            }
            else if (loadExt == ".gat")
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
        SetPreviewMode(PreviewMode.None);
        if (ItemRelatedFilesListBox != null)
            ItemRelatedFilesListBox.ItemsSource = null;
        if (ItemRelatedFilesExpander != null)
            ItemRelatedFilesExpander.Visibility = Visibility.Collapsed;

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

        int added = 0;
        foreach (var path in dlg.FileNames)
        {
            if (App.GrfService.AddGrfPath(path))
                added++;
        }

        App.SpriteLookupService?.ClearCache();

        // Try parsing iteminfo directly for diagnostics
        string parseLog = "";
        try
        {
            var iteminfoPath = @"data\luafiles514\lua files\datainfo\iteminfo.lub";
            var iteminfoData = App.GrfService.GetData(iteminfoPath);
            if (iteminfoData != null && iteminfoData.Length > 0)
            {
                parseLog = $"iteminfo.lub data size: {iteminfoData.Length} bytes\n";
                var items = ItemInfoLubParser.ParseItemEntriesFromData(iteminfoData);
                parseLog += $"Parsed items from iteminfo.lub: {items.Count}\n";
                if (items.Count > 0)
                {
                    parseLog += $"First item: ID={items[0].Id}, Name={items[0].Name}\n";
                }
            }
            else
            {
                parseLog = "iteminfo.lub data: NULL or EMPTY\n";
            }
        }
        catch (Exception ex)
        {
            parseLog = $"iteminfo.lub parse error: {ex.Message}\n{ex.StackTrace?.Substring(0, Math.Min(500, ex.StackTrace?.Length ?? 0))}";
        }

        App.ReloadFromGrf();

        RefreshList();
        UpdateSourceIndicators();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"New sources added: {added}");
        sb.AppendLine($"Total sources: {App.GrfService.GrfPaths.Count}");
        sb.AppendLine($"GRF loaded: {App.GrfService.IsLoaded}");
        sb.AppendLine($"Items loaded: {App.ItemDbService.Items.Count}");
        sb.AppendLine($"Mobs loaded: {App.MobDbService.Mobs.Count}");
        sb.AppendLine($"Sprites cached: {App.SpriteLookupService?.CachedSpriteCount ?? 0}");
        sb.AppendLine($"Filesystem sprites: {App.FileSystemSpriteSource?.CachedCount ?? 0}");
        sb.AppendLine();
        sb.AppendLine("--- iteminfo.lub parse diagnostics ---");
        sb.AppendLine(parseLog);
        sb.AppendLine();
        sb.AppendLine(App.GrfService.BuildSanityReport());

        System.Windows.MessageBox.Show(this, sb.ToString(), "RoDbEditor - GRF Loaded", MessageBoxButton.OK);
    }


    private void MenuOpenExtractedAssets_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select extracted assets root folder (contains server variant subfolders)",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var path = dlg.SelectedPath;
        if (string.IsNullOrEmpty(path)) return;

        App.FileSystemSpriteSource = new FileSystemSpriteSource(path);
        App.SpriteLookupService.ClearCache();
        App.SpriteLookupService = new SpriteLookupService(App.GrfService, App.FileSystemSpriteSource);

        UpdateSourceIndicators();
        System.Windows.MessageBox.Show(this,
            $"Extracted assets loaded.\nSprites indexed: {App.FileSystemSpriteSource.CachedCount}",
            "RoDbEditor", MessageBoxButton.OK);
    }

    private void MenuOpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select server data folder (contains db/, npc/, system/)",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var path = dlg.SelectedPath;
        if (string.IsNullOrEmpty(path)) return;
        App.Config.DataPath = path;
        App.ReloadDataPath(path);
        RefreshList();
        UpdateSourceIndicators();
        System.Windows.MessageBox.Show(this, $"Data folder set.\nItems: {App.ItemDbService?.Items?.Count ?? 0}, Mobs: {App.MobDbService?.Mobs?.Count ?? 0}, NPCs: {App.NpcIndexService?.All?.Count ?? 0}.", "RoDbEditor", MessageBoxButton.OK);
    }

    // This duplicate code block for MAPS/QUESTS asset listing in RefreshList appears to be an accidental copy-paste.
    // It should be deleted. The correct MAPS/QUESTS handling is already earlier in RefreshList.
    // Removed duplicate MAPS/QUESTS asset listing code block.
    // Functionality is preserved by the earlier logic in RefreshList().

    private void CreateNewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCategory == "ITEMS")
        {
            CreateNewItem();
        }
        else if (_currentCategory == "MONSTERS")
        {
            CreateNewMonster();
        }
        else
        {
            System.Windows.MessageBox.Show(this, "Create New is available for ITEMS and MONSTERS.", "RoDbEditor", MessageBoxButton.OK);
        }
    }

    private void CreateNewItem()
    {
        var nextId = App.ItemDbService.GetNextCustomItemId();
        var item = new ItemEntry
        {
            Id = nextId,
            AegisName = $"Custom_Item_{nextId}",
            Name = $"Custom Item {nextId}",
            Type = "Etc",
            Buy = 0,
            Weight = 0,
            SourceFile = "item_db.yml",
            SourceIndex = -1 // Will be set on save (append)
        };

        App.ItemDbService.AddItem(item);
        RefreshList();

        // Select the new item in the list
        AssetListBox.SelectedItem = App.ItemDbService.Items.FirstOrDefault(i => i.Id == item.Id);
        ShowItemDetails(item);

        System.Windows.MessageBox.Show(this,
            $"New custom item created with ID {nextId}.\n\n" +
            "Edit the fields above and click SAVE to write it to\ndb/import/item_db.yml.",
            "RoDbEditor - New Item", MessageBoxButton.OK);
    }

    private void CreateNewMonster()
    {
        var nextId = App.MobDbService.GetNextCustomMobId();
        var mob = new MobEntry
        {
            Id = nextId,
            AegisName = $"CUSTOM_MOB_{nextId}",
            Name = $"Custom Monster {nextId}",
            Level = 1,
            Hp = 100,
            Attack = 10,
            Attack2 = 15,
            Defense = 5,
            MagicDefense = 5,
            Str = 1, Agi = 1, Vit = 1, Int = 1, Dex = 1, Luk = 1,
            AttackRange = 1, SkillRange = 1, ChaseRange = 1,
            Size = "Medium",
            Race = "Formless",
            Element = "Neutral",
            ElementLevel = 1,
            WalkSpeed = 200,
            Ai = "06",
            Class = "Normal",
            SourceFile = "mob_db.yml",
            SourceIndex = -1
        };

        App.MobDbService.AddMob(mob);
        RefreshList();

        AssetListBox.SelectedItem = App.MobDbService.Mobs.FirstOrDefault(m => m.Id == mob.Id);
        ShowMonsterDetails(mob);

        System.Windows.MessageBox.Show(this,
            $"New custom monster created with ID {nextId}.\n\n" +
            "Edit the fields above and click SAVE to write it to\ndb/import/mob_db.yml.",
            "RoDbEditor - New Monster", MessageBoxButton.OK);
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
