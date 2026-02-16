using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GRF.FileFormats.ActFormat;
using GRF.FileFormats.SprFormat;
using GRF.Image;
using Microsoft.Win32;
using RoDbEditor.Core;
using RoDbEditor.Data;
using RoDbEditor.Models;
using RoDbEditor.Services;
using RoDbEditor.Services.Analysis;
using RoDbEditor.Services.Blueprint;
using RoDbEditor.Services.Export;
using RoDbEditor.UI;
using RoDbEditor.UI.Dialogs;

namespace RoDbEditor;

public enum PreviewMode { None, Image, Sprite }

public partial class MainWindow : Window
{
    private sealed class AssignmentTargetOption
    {
        public object? Payload { get; init; }
        public string Key { get; init; } = "";
        public string Display { get; init; } = "";
        public override string ToString() => Display;
    }

    private readonly List<AssetEntry> _allAssets = new();
    private string _currentCategory = "ITEMS";
    private string _originalItemScript = "";
    private readonly List<(string bonusType, int value)> _itemBonusList = new();
    private ItemEntry? _currentItemForEdit;
    private string _originalMonsterDropsText = "";
    private string _originalNpcScript = "";
    private MobEntry? _currentMob;
    private MobEntry? _currentMobSnapshot;
    private Models.ExtractedAssetEntry? _currentExtractedAsset;
    private TextMarkerService? _markerService;
    private WorkspaceIndex? _lastWorkspaceIndex;
    private readonly OperationsLogService _operationsLog = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RefreshList();
            UpdateListLabel();
            UpdateSourceIndicators();
            if (AssignEntityTypeCombo != null)
                AssignEntityTypeCombo.SelectedIndex = 0;
            SpriteViewer.SetBackgroundMode(Core.Controls.SpriteAnimationViewer.ViewerBackgroundMode.Checkered);
            if (!string.IsNullOrEmpty(App.Config?.DataPath))
                 SetupFileWatcher(App.Config.DataPath);

            RefreshOperationsList();

            // Initialize TextMarkerService for NpcScriptEditor
            if (NpcScriptEditor?.Document != null)
            {
                _markerService = new TextMarkerService(NpcScriptEditor.Document);
                NpcScriptEditor.TextArea.TextView.BackgroundRenderers.Add(_markerService);
                NpcScriptEditor.TextArea.TextView.LineTransformers.Add(_markerService);
            }
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
        if (_currentCategory == "EXTRACTED_ASSETS")
            CurrentListLabel.Text = "CURRENT LIST: Extracted Assets";
        else
            CurrentListLabel.Text = $"CURRENT LIST: {_currentCategory}";
    }

    private void CategoryTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryTabs == null || CategoryTabs.SelectedIndex < 0 || CategoryTabs.SelectedIndex > 5) return;
        var headers = new[] { "ITEMS", "MONSTERS", "NPCs", "MAPS", "QUESTS", "EXTRACTED_ASSETS" };
        _currentCategory = headers[CategoryTabs.SelectedIndex];

        if (NpcMapFilterPanel != null)
            NpcMapFilterPanel.Visibility = _currentCategory == "NPCs" ? Visibility.Visible : Visibility.Collapsed;

        if (ExtractedAssetSubCategoryPanel != null)
            ExtractedAssetSubCategoryPanel.Visibility = _currentCategory == "EXTRACTED_ASSETS" ? Visibility.Visible : Visibility.Collapsed;
        if (ExtractedAssignmentPanel != null)
            ExtractedAssignmentPanel.Visibility = _currentCategory == "EXTRACTED_ASSETS" ? Visibility.Visible : Visibility.Collapsed;

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
        UpdateExtractedPreviewPanels();
        RefreshList();
    }

        private void RefreshListButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshList();
        }

        private void RefreshList()
        {
            if (SearchBox == null || AssetListBox == null)
                return;

            if (_currentCategory == "EXTRACTED_ASSETS")
            {
                var filter = SearchBox.Text?.Trim();
                var assets = App.ExtractedAssetService?.Search("", filter)
                    ?? (IReadOnlyList<Models.ExtractedAssetEntry>)Array.Empty<Models.ExtractedAssetEntry>();
                assets = ApplyExtractedModeFilter(assets).ToList();
                AssetListBox.ItemsSource = null;
                AssetListBox.ItemsSource = assets;
                AssetListBox.DisplayMemberPath = "DisplayName";
                return;
            }

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
        if (ExtractedAssetPropertiesPanel != null)
            ExtractedAssetPropertiesPanel.Visibility = Visibility.Collapsed;

        if (AssetListBox.SelectedItem is ItemEntry itemEntry)
        {
            ItemDetailsPanel.Visibility = Visibility.Visible;
            MonsterDetailsPanel.Visibility = Visibility.Collapsed;
            NpcDetailsPanel.Visibility = Visibility.Collapsed;
            ShowItemDetails(itemEntry);
            return;
        }

        if (AssetListBox.SelectedItem is MobEntry mobEntry)
        {
            ItemDetailsPanel.Visibility = Visibility.Collapsed;
            MonsterDetailsPanel.Visibility = Visibility.Visible;
            NpcDetailsPanel.Visibility = Visibility.Collapsed;
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

    if (AssetListBox.SelectedItem is Models.ExtractedAssetEntry extractedEntry)
    {
        ItemDetailsPanel.Visibility = Visibility.Collapsed;
        MonsterDetailsPanel.Visibility = Visibility.Collapsed;
        NpcDetailsPanel.Visibility = Visibility.Collapsed;
        if (ExtractedAssetPropertiesPanel != null)
            ExtractedAssetPropertiesPanel.Visibility = Visibility.Visible;
        ShowExtractedAssetDetails(extractedEntry);
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

    private void UpdateExtractedPreviewPanels()
    {
        if (StaticPreviewPanel == null || SpritePreviewPanel == null || CenterPreviewGrid == null)
            return;

        if (_currentCategory == "EXTRACTED_ASSETS")
        {
            // Remove bottom pane: collapse SpritePreviewPanel and zero out its rows so BMP/PAL viewer expands to bottom
            SpritePreviewPanel.Visibility = Visibility.Collapsed;
            SpritePreviewPanel.Height = 0;
            if (CenterPreviewGrid.RowDefinitions.Count >= 3)
            {
                CenterPreviewGrid.RowDefinitions[1].Height = new GridLength(0);
                CenterPreviewGrid.RowDefinitions[2].Height = new GridLength(0);
            }
            StaticPreviewPanel.Visibility = Visibility.Visible;
        }
        else
        {
            // Restore layout for ITEMS/MONSTERS/NPCs
            if (CenterPreviewGrid.RowDefinitions.Count >= 3)
            {
                CenterPreviewGrid.RowDefinitions[1].Height = new GridLength(8);
                CenterPreviewGrid.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
            }
            SpritePreviewPanel.Height = double.NaN; // Auto
            StaticPreviewPanel.Visibility = Visibility.Visible;
        }
    }

    private void SetPreviewMode(PreviewMode mode)
    {
        if (SpriteViewer == null || CenterPreviewImage == null)
            return;

        SpriteViewer.Stop();
        if (_currentCategory == "EXTRACTED_ASSETS")
        {
            if (StaticPreviewPanel == null || SpritePreviewPanel == null)
                return;

            // For extracted assets: static BMP/PAL preview only — SpritePreviewPanel removed from layout
            StaticPreviewPanel.Visibility = Visibility.Visible;
            SpritePreviewPanel.Visibility = Visibility.Collapsed;
            if (mode == PreviewMode.None)
                CenterPreviewImage.Source = null;
            return;
        }

        switch (mode)
        {
            case PreviewMode.Sprite:
                StaticPreviewPanel.Visibility = Visibility.Collapsed;
                SpritePreviewPanel.Visibility = Visibility.Visible;
                CenterPreviewImage.Source = null;
                break;
            case PreviewMode.Image:
                StaticPreviewPanel.Visibility = Visibility.Visible;
                SpritePreviewPanel.Visibility = Visibility.Collapsed;
                break;
            default:
                StaticPreviewPanel.Visibility = Visibility.Visible;
                SpritePreviewPanel.Visibility = Visibility.Collapsed;
                CenterPreviewImage.Source = null;
                break;
        }
    }

    private void ShowNpcDetails(NpcScriptEntry npc)
    {
        if (ExtractedAssignmentPanel != null)
            ExtractedAssignmentPanel.Visibility = Visibility.Collapsed;
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

    private static MobEntry CloneMob(MobEntry source)
    {
        var copy = new MobEntry
        {
            Id = source.Id,
            AegisName = source.AegisName ?? "",
            Name = source.Name ?? "",
            Level = source.Level,
            Hp = source.Hp,
            Sp = source.Sp,
            BaseExp = source.BaseExp,
            JobExp = source.JobExp,
            MvpExp = source.MvpExp,
            Attack = source.Attack,
            Attack2 = source.Attack2,
            Defense = source.Defense,
            MagicDefense = source.MagicDefense,
            Str = source.Str,
            Agi = source.Agi,
            Vit = source.Vit,
            Int = source.Int,
            Dex = source.Dex,
            Luk = source.Luk,
            AttackRange = source.AttackRange,
            SkillRange = source.SkillRange,
            ChaseRange = source.ChaseRange,
            Size = source.Size ?? "Medium",
            Race = source.Race ?? "Formless",
            Element = source.Element ?? "Neutral",
            ElementLevel = source.ElementLevel,
            WalkSpeed = source.WalkSpeed,
            AttackDelay = source.AttackDelay,
            AttackMotion = source.AttackMotion,
            DamageMotion = source.DamageMotion,
            Ai = source.Ai ?? "06",
            Class = source.Class ?? "Normal",
            SourceFile = source.SourceFile,
            SourceIndex = source.SourceIndex
        };
        foreach (var d in source.Drops)
            copy.Drops.Add(new MobDropEntry { Item = d.Item, Rate = d.Rate, StealProtected = d.StealProtected, Index = d.Index });
        foreach (var d in source.MvpDrops)
            copy.MvpDrops.Add(new MobDropEntry { Item = d.Item, Rate = d.Rate, StealProtected = d.StealProtected, Index = d.Index });
        copy.Modes = new Dictionary<string, bool>(source.Modes ?? new Dictionary<string, bool>(), StringComparer.OrdinalIgnoreCase);
        return copy;
    }

    private void ShowMonsterDetails(MobEntry mob)
    {
        if (ExtractedAssignmentPanel != null)
            ExtractedAssignmentPanel.Visibility = Visibility.Collapsed;
        _currentMob = mob;
        _currentMobSnapshot = CloneMob(mob);
        var vm = new ViewModels.MobDetailsViewModel(
            mob,
            App.AttrFixTableService,
            App.SpawnParser,
            App.MapIndexService,
            App.MobSkillDbService,
            App.SkillDbService,
            App.MobDbService);
        MonsterDetailsPanel.DataContext = vm;
        _originalMonsterDropsText = SerializeMobDrops(mob);
        MonsterDiffExpander.Visibility = Visibility.Visible;
        MonsterDiffTextBox.Text = "";

        SetPreviewMode(PreviewMode.Sprite);

        // Effective sprite: mob_avail override or AegisName
        var effectiveSprite = App.MobAvailService?.Get(mob.AegisName)?.Sprite ?? mob.AegisName;
        System.Diagnostics.Debug.WriteLine($"[ShowMonsterDetails] Looking for sprite: AegisName={mob.AegisName}, effectiveSprite={effectiveSprite}");
        System.Diagnostics.Debug.WriteLine($"[ShowMonsterDetails] Sprite cache count: {App.SpriteLookupService?.CachedSpriteCount ?? 0}");

        if (App.SpriteLookupService == null)
        {
            System.Diagnostics.Debug.WriteLine("[ShowMonsterDetails] SpriteLookupService is NULL!");
            SpriteViewer.LoadFromData(null, null);
            return;
        }

        var (actPath, sprPath) = App.SpriteLookupService.FindMonsterSprite(effectiveSprite);
        System.Diagnostics.Debug.WriteLine($"[ShowMonsterDetails] Found paths: ACT={actPath ?? "NULL"}, SPR={sprPath ?? "NULL"}");

        var (actData, sprData) = App.SpriteLookupService.GetSpriteData(actPath, sprPath);
        System.Diagnostics.Debug.WriteLine($"[ShowMonsterDetails] Data sizes: ACT={actData?.Length ?? 0}, SPR={sprData?.Length ?? 0}");

        SpriteViewer.LoadFromData(actData, sprData);
        SpriteViewer.Play();

        PopulateMonsterSkillsAndSlaves(mob);
    }

    private void PopulateMonsterSkillsAndSlaves(MobEntry mob)
    {
        try
        {
            if (MonsterSkillsGrid == null || MonsterSlavesGrid == null)
                return;

            if (App.MobSkillPanelService == null)
            {
                MonsterSkillsGrid.ItemsSource = null;
                MonsterSlavesGrid.ItemsSource = null;
                return;
            }

            MonsterSkillsGrid.ItemsSource = App.MobSkillPanelService.BuildMonsterSkills(mob);
            MonsterSlavesGrid.ItemsSource = App.MobSkillPanelService.BuildSlaves(mob);
        }
        catch
        {
            // UI should never crash due to db parsing issues
        }
    }

    // ── Mob Skill Editor Handlers ──────────────────────────────────────

    private void MobSkillAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMob == null || App.MobSkillWriteService == null) return;

        var dlg = new Dialogs.MobSkillEditDialog();
        dlg.SetMobContext(_currentMob.Id, _currentMob.Name);
        dlg.Owner = this;

        if (dlg.ShowDialog() == true && dlg.Result != null)
        {
            App.MobSkillWriteService.AppendSkillRow(dlg.Result);
            ReloadMobSkillsAndRefreshDisplay();
        }
    }

    private void MobSkillEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMob == null || App.MobSkillWriteService == null) return;

        var uiRow = MonsterSkillsGrid.SelectedItem as MonsterSkillRow;
        if (uiRow == null)
        {
            System.Windows.MessageBox.Show("Select a skill row to edit.", "Edit Skill", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dbRow = uiRow.SourceRow;
        if (dbRow == null)
        {
            System.Windows.MessageBox.Show("Could not find the underlying database row.", "Edit Skill", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new Dialogs.MobSkillEditDialog();
        dlg.SetMobContext(_currentMob.Id, _currentMob.Name);
        dlg.LoadFromRow(dbRow);
        dlg.Owner = this;

        if (dlg.ShowDialog() == true && dlg.Result != null)
        {
            App.MobSkillWriteService.UpdateSkillRow(dbRow, dlg.Result);
            ReloadMobSkillsAndRefreshDisplay();
        }
    }

    private void MobSkillDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMob == null || App.MobSkillWriteService == null) return;

        var uiRow = MonsterSkillsGrid.SelectedItem as MonsterSkillRow;
        if (uiRow == null)
        {
            System.Windows.MessageBox.Show("Select a skill row to delete.", "Delete Skill", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dbRow = uiRow.SourceRow;
        if (dbRow == null)
        {
            System.Windows.MessageBox.Show("Could not find the underlying database row.", "Delete Skill", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = System.Windows.MessageBox.Show(
            $"Delete skill {uiRow.Skill} (Lv{uiRow.SkillLv}) in state '{uiRow.State}'?\n\nSource: {uiRow.Source}",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            App.MobSkillWriteService.DeleteSkillRow(dbRow);
            ReloadMobSkillsAndRefreshDisplay();
        }
    }

    private void MobSkillOpenFile_Click(object sender, RoutedEventArgs e)
    {
        // 1) Acquire selected row.
        if (MonsterSkillsGrid.SelectedItem is not RoDbEditor.Models.MonsterSkillRow uiRow)
        {
            System.Windows.MessageBox.Show("Select a Monster Skill row first.", "Open Source", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 2) Resolve backing MobSkillDbRow via SourceRow
        string path;
        int line;

        if (uiRow.SourceRow != null && !string.IsNullOrWhiteSpace(uiRow.SourceRow.SourceFile))
        {
            path = uiRow.SourceRow.SourceFile;
            line = uiRow.SourceRow.SourceLine;
        }
        else
        {
            // Fallback: parse uiRow.Source
            var parsed = TryParseSource(uiRow.Source);
            path = parsed.path;
            line = parsed.line;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            System.Windows.MessageBox.Show($"No valid source info on this row: {uiRow.Source}", "Open Source", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 3) Normalize path relative to DataPath if needed
        path = ResolveMobSkillPath(path);

        if (!File.Exists(path))
        {
            System.Windows.MessageBox.Show($"Source file not found:\n{path}", "Open Source", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // 4) Try VS Code goto first (best UX)
        if (TryOpenVsCodeGoto(path, line))
            return;

        // 5) Fallback: Notepad (no goto) + clipboard hint
        try
        {
            System.Windows.Clipboard.SetText($"{path}:{line}");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });

            System.Windows.MessageBox.Show("Opened in Notepad (cannot jump to line). Path:Line copied to clipboard for VS Code goto.",
                "Open Source", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to open source:\n{ex.Message}", "Open Source", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static (string path, int line) TryParseSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return ("", 0);

        // Common format: "mob_skill_db.txt:123" OR "C:\...\mob_skill_db.txt:123"
        var s = source.Trim();

        // If there are multiple ':' (Windows drive), split from the end.
        var lastColon = s.LastIndexOf(':');
        if (lastColon < 0) return (s, 0);

        var left = s.Substring(0, lastColon);
        var right = s.Substring(lastColon + 1);

        if (!int.TryParse(right, out var line) || line <= 0) return (left, 0);
        return (left, line);
    }

    private string ResolveMobSkillPath(string raw)
    {
        // If raw already absolute, return.
        if (Path.IsPathRooted(raw)) return raw;

        // Otherwise interpret as relative under DataPath (rAthena).
        var dataPath = App.Config?.DataPath;
        if (string.IsNullOrWhiteSpace(dataPath)) return raw;

        // Try common mob skill locations:
        // - db/import/mob_skill_db.txt
        // - db/re/mob_skill_db.txt
        // - db/pre-re/mob_skill_db.txt
        // - db/mob_skill_db.txt
        var candidates = new[]
        {
            Path.Combine(dataPath, "db", "import", raw),
            Path.Combine(dataPath, "db", "re", raw),
            Path.Combine(dataPath, "db", "pre-re", raw),
            Path.Combine(dataPath, "db", raw)
        };

        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        // fallback: just combine with dataPath
        return Path.Combine(dataPath, raw);
    }

    private static bool TryOpenVsCodeGoto(string path, int line)
    {
        try
        {
            // Launch VS Code directly; if "code" is not on PATH,
            // Process.Start throws Win32Exception → catch returns false → Notepad fallback.
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "code",
                Arguments = $"--goto \"{path}:{line}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            System.Diagnostics.Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reload mob_skill_db from disk and refresh the current monster's skill/slave display.
    /// </summary>
    private void ReloadMobSkillsAndRefreshDisplay()
    {
        if (_currentMob == null) return;

        // Reload the mob skill database from disk
        if (!string.IsNullOrEmpty(App.Config?.DataPath))
        {
            App.MobSkillDbService?.LoadFromDataPath(App.Config.DataPath);

            // Rebuild the panel service (it holds references to the refreshed data)
            if (App.MobSkillDbService != null && App.SkillDbMiniService != null && App.MobDbService != null)
            {
                // MobSkillPanelService reads from MobSkillDbService on each call,
                // so we just need to refresh the display
            }
        }

        PopulateMonsterSkillsAndSlaves(_currentMob);
    }

    // ── Mob Slave Editor Handlers ───────────────────────────────────────

    private void MobSlaveAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMob == null || App.MobSkillWriteService == null) return;

        // Pre-fill a new NPC_SUMMONSLAVE row
        var newRow = new MobSkillDbRow
        {
            MobId = _currentMob.Id,
            SkillId = 196, // NPC_SUMMONSLAVE
            SkillLv = 1,
            State = "idle",
            Dummy = $"{_currentMob.Name}@NPC_SUMMONSLAVE",
            Rate = 10000,
            Target = "self",
            ConditionType = "slavele",
            ConditionValue = 1
        };

        var dlg = new Dialogs.MobSlaveEditDialog();
        dlg.SetMobContext(_currentMob.Id, _currentMob.Name);
        dlg.LoadFromRow(newRow);
        dlg.Owner = this;

        if (dlg.ShowDialog() == true && dlg.Result != null)
        {
            App.MobSkillWriteService.AppendSkillRow(dlg.Result);
            EnsureSlaveSupportSkills(dlg.Result);
            ReloadMobSkillsAndRefreshDisplay();
        }
    }

    private void MobSlaveEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMob == null || App.MobSkillWriteService == null) return;

        var slaveRow = MonsterSlavesGrid.SelectedItem as MonsterSlaveRow;
        if (slaveRow == null)
        {
            System.Windows.MessageBox.Show("Select a slave row to edit.", "Edit Slave", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sourceRow = slaveRow.SourceRow;
        if (sourceRow == null)
        {
            System.Windows.MessageBox.Show("Could not find the underlying database row.", "Edit Slave", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new Dialogs.MobSlaveEditDialog();
        dlg.SetMobContext(_currentMob.Id, _currentMob.Name);
        dlg.LoadFromRow(sourceRow);
        dlg.Owner = this;

        if (dlg.ShowDialog() == true && dlg.Result != null)
        {
            App.MobSkillWriteService.UpdateSkillRow(sourceRow, dlg.Result);
            EnsureSlaveSupportSkills(dlg.Result);
            ReloadMobSkillsAndRefreshDisplay();
        }
    }

    private void EnsureSlaveSupportSkills(MobSkillDbRow seedRow)
    {
        if (App.MobSkillWriteService == null || App.MobSkillDbService == null || string.IsNullOrWhiteSpace(App.Config?.DataPath))
            return;

        App.MobSkillDbService.LoadFromDataPath(App.Config.DataPath);
        var existing = App.MobSkillDbService.GetRowsForMob(seedRow.MobId).ToList();

        foreach (var state in new[] { "idle", "attack", "chase" })
        {
            var hasState = existing.Any(r =>
                r.SkillId == 196 &&
                string.Equals(r.State, state, StringComparison.OrdinalIgnoreCase) &&
                SameSlaveSet(r, seedRow));

            if (hasState)
                continue;

            var summonRow = BuildDerivedSlaveRow(seedRow, 196, state);
            App.MobSkillWriteService.AppendSkillRow(summonRow, $"Auto-added missing NPC_SUMMONSLAVE state '{state}'");
            existing.Add(summonRow);
        }

        var hasCallSlave = existing.Any(r => r.SkillId == 352);
        if (!hasCallSlave)
        {
            var recallRow = BuildDerivedSlaveRow(seedRow, 352, "chase");
            recallRow.Dummy = $"{_currentMob?.Name ?? ("Mob" + seedRow.MobId)}@NPC_CALLSLAVE";
            recallRow.Val1 = 0;
            recallRow.Val2 = 0;
            recallRow.Val3 = 0;
            recallRow.Val4 = 0;
            recallRow.Val5 = 0;
            recallRow.ConditionType = "always";
            recallRow.ConditionValue = 0;
            recallRow.SkillLv = 1;
            App.MobSkillWriteService.AppendSkillRow(recallRow, "Auto-added NPC_CALLSLAVE recall support");
        }
    }

    private static bool SameSlaveSet(MobSkillDbRow a, MobSkillDbRow b)
    {
        return a.Val1 == b.Val1 &&
               a.Val2 == b.Val2 &&
               a.Val3 == b.Val3 &&
               a.Val4 == b.Val4 &&
               a.Val5 == b.Val5;
    }

    private MobSkillDbRow BuildDerivedSlaveRow(MobSkillDbRow seed, int skillId, string state)
    {
        var mobName = _currentMob?.Name ?? $"Mob{seed.MobId}";
        var skillName = skillId == 196 ? "NPC_SUMMONSLAVE" : "NPC_CALLSLAVE";
        return new MobSkillDbRow
        {
            MobId = seed.MobId,
            Dummy = $"{mobName}@{skillName}",
            SkillId = skillId,
            SkillLv = Math.Max(1, seed.SkillLv),
            State = state,
            Rate = seed.Rate > 0 ? seed.Rate : 10000,
            CastTimeMs = 0,
            DelayMs = 0,
            Cancelable = 0,
            Target = "self",
            ConditionType = seed.ConditionType,
            ConditionValue = seed.ConditionValue,
            Val1 = seed.Val1,
            Val2 = seed.Val2,
            Val3 = seed.Val3,
            Val4 = seed.Val4,
            Val5 = seed.Val5,
            Emotion = 0,
            Chat = ""
        };
    }

    private void MobSlaveDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMob == null || App.MobSkillWriteService == null || App.MobSkillDbService == null) return;

        var slaveRow = MonsterSlavesGrid.SelectedItem as MonsterSlaveRow;
        if (slaveRow == null)
        {
            System.Windows.MessageBox.Show("Select a slave row to delete.", "Delete Slave", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sourceRow = slaveRow.SourceRow;
        if (sourceRow == null)
        {
            System.Windows.MessageBox.Show("Could not find the underlying database row.", "Delete Slave", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Collect ALL related rows: NPC_SUMMONSLAVE (idle/attack/chase) for this slave set.
        // Also delete NPC_CALLSLAVE (recall) only if this is the last slave set for the mob.
        var allForMob = App.MobSkillDbService.GetRowsForMob(_currentMob.Id).ToList();
        var toDelete = new List<MobSkillDbRow>();

        foreach (var r in allForMob)
        {
            if (r.SkillId == 196 && SameSlaveSet(r, sourceRow))
                toDelete.Add(r);
        }

        var otherSummons = allForMob.Count(r =>
            r.SkillId == 196 && !SameSlaveSet(r, sourceRow));
        if (otherSummons == 0)
        {
            foreach (var r in allForMob)
            {
                if (r.SkillId == 352)
                    toDelete.Add(r);
            }
        }

        var result = System.Windows.MessageBox.Show(
            $"Delete slave {slaveRow.MobName} (x{slaveRow.Count}) and its recall skill?\n\n" +
            $"This will remove {toDelete.Count} mob_skill_db row(s):\n" +
            $"• All NPC_SUMMONSLAVE states (idle/attack/chase) for this slave set\n" +
            $"• NPC_CALLSLAVE recall support",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            var deleted = App.MobSkillWriteService.DeleteSkillRows(toDelete);
            ReloadMobSkillsAndRefreshDisplay();
            if (deleted > 0)
                System.Windows.MessageBox.Show($"Removed {deleted} mob skill row(s).", "Delete Slave", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void MobSlaveOpenFile_Click(object sender, RoutedEventArgs e)
    {
        var slaveRow = MonsterSlavesGrid.SelectedItem as MonsterSlaveRow;
        if (slaveRow == null)
        {
            System.Windows.MessageBox.Show("Select a slave row first.", "Open Source", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sourceRow = slaveRow.SourceRow;
        if (sourceRow == null || string.IsNullOrWhiteSpace(sourceRow.SourceFile))
        {
            System.Windows.MessageBox.Show("Source file info not available for this row.", "Open Source", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var path = ResolveMobSkillPath(sourceRow.SourceFile);
        var line = sourceRow.SourceLine;

        if (!File.Exists(path))
        {
            System.Windows.MessageBox.Show($"Source file not found:\n{path}", "Open Source", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (TryOpenVsCodeGoto(path, line))
            return;

        try
        {
            System.Windows.Clipboard.SetText($"{path}:{line}");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
            System.Windows.MessageBox.Show("Opened in Notepad (cannot jump to line). Path:Line copied to clipboard.",
                "Open Source", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to open source:\n{ex.Message}", "Open Source", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── End Mob Skill / Slave Editor ────────────────────────────────────

    private static string SerializeMobDrops(MobEntry mob)
    {
        var lines = new List<string>();
        foreach (var d in mob.Drops) lines.Add($"Drop: {d.Item} Rate={d.Rate} Steal={d.StealProtected}");
        foreach (var d in mob.MvpDrops) lines.Add($"MvpDrop: {d.Item} Rate={d.Rate}");
        return string.Join("\r\n", lines);
    }



    private void MonsterSaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMob == null) return;
        if (MonsterDetailsPanel.DataContext is ViewModels.MobDetailsViewModel vm)
        {
            vm.PushTo(_currentMob);
        }
        var path = App.MobDbService.GetImportMobDbPath();
        byte[]? snapshot = null;
        if (!string.IsNullOrEmpty(path) && File.Exists(path) && _currentMob.SourceIndex >= 0)
            snapshot = File.ReadAllBytes(path);

        var result = App.MobDbService.SaveMob(_currentMob);
        if (result != null)
        {
            if (result.IsUpdate)
                _operationsLog.RecordUpdated(OperationEntityKind.Mob, _currentMob.Id, _currentMob.AegisName, _currentMob.Name ?? "", result.Path, result.BodyIndex, snapshot);
            else
                _operationsLog.RecordAdded(OperationEntityKind.Mob, _currentMob.Id, _currentMob.AegisName, _currentMob.Name ?? "", result.Path, result.BodyIndex);
            RefreshOperationsList();
        }
        _currentMobSnapshot = CloneMob(_currentMob);
        _originalMonsterDropsText = SerializeMobDrops(_currentMob);
        try { App.MobInfoLuaWriter?.WriteEntry(_currentMob); }
        catch (Exception luaEx)
        {
            System.Windows.MessageBox.Show(this,
                "Monster saved to YAML but failed to update mobinfo_custom.lua:\n" + luaEx.Message,
                "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        System.Windows.MessageBox.Show(this, "Monster saved.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void MonsterCancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMob == null || _currentMobSnapshot == null) return;
        CloneMobInto(_currentMobSnapshot, _currentMob);
        ShowMonsterDetails(_currentMob);
    }

    private static void CloneMobInto(MobEntry source, MobEntry target)
    {
        target.AegisName = source.AegisName ?? "";
        target.Name = source.Name ?? "";
        target.Level = source.Level;
        target.Hp = source.Hp;
        target.Sp = source.Sp;
        target.BaseExp = source.BaseExp;
        target.JobExp = source.JobExp;
        target.MvpExp = source.MvpExp;
        target.Attack = source.Attack;
        target.Attack2 = source.Attack2;
        target.Defense = source.Defense;
        target.MagicDefense = source.MagicDefense;
        target.Str = source.Str;
        target.Agi = source.Agi;
        target.Vit = source.Vit;
        target.Int = source.Int;
        target.Dex = source.Dex;
        target.Luk = source.Luk;
        target.AttackRange = source.AttackRange;
        target.SkillRange = source.SkillRange;
        target.ChaseRange = source.ChaseRange;
        target.Size = source.Size ?? "Medium";
        target.Race = source.Race ?? "Formless";
        target.Element = source.Element ?? "Neutral";
        target.ElementLevel = source.ElementLevel;
        target.WalkSpeed = source.WalkSpeed;
        target.AttackDelay = source.AttackDelay;
        target.AttackMotion = source.AttackMotion;
        target.DamageMotion = source.DamageMotion;
        target.Ai = source.Ai ?? "06";
        target.Class = source.Class ?? "Normal";
        target.Drops.Clear();
        foreach (var d in source.Drops)
            target.Drops.Add(new MobDropEntry { Item = d.Item, Rate = d.Rate, StealProtected = d.StealProtected, Index = d.Index });
        target.MvpDrops.Clear();
        foreach (var d in source.MvpDrops)
            target.MvpDrops.Add(new MobDropEntry { Item = d.Item, Rate = d.Rate, StealProtected = d.StealProtected, Index = d.Index });
        target.Modes.Clear();
        if (source.Modes != null)
            foreach (var kv in source.Modes)
                target.Modes[kv.Key] = kv.Value;
    }

    private void ShowItemDetails(ItemEntry item)
    {
        _currentItemForEdit = item;
        if (ExtractedAssignmentPanel != null)
            ExtractedAssignmentPanel.Visibility = Visibility.Collapsed;

        ItemEditId.Text = item.Id.ToString();
        ItemEditAegisName.Text = item.AegisName ?? "";
        ItemEditName.Text = item.Name ?? "";
        SetComboByContent(ItemEditType, item.Type ?? "Etc");
        PopulateItemEditSubTypes();
        SetComboByContent(ItemEditSubType, item.SubType ?? "");
        ItemEditBuy.Text = item.Buy?.ToString() ?? "";
        ItemEditSell.Text = item.Sell?.ToString() ?? "";
        ItemEditWeight.Text = item.Weight?.ToString() ?? "";
        ItemEditAttack.Text = item.Attack?.ToString() ?? "";
        ItemEditMagicAttack.Text = item.MagicAttack?.ToString() ?? "";
        ItemEditDefense.Text = item.Defense?.ToString() ?? "";
        ItemEditRange.Text = item.Range?.ToString() ?? "";
        ItemEditSlots.Text = item.Slots?.ToString() ?? "";
        ItemEditEquipLevelMin.Text = item.EquipLevelMin?.ToString() ?? "";
        ItemEditEquipLevelMax.Text = item.EquipLevelMax?.ToString() ?? "";
        ItemEditWeaponLevel.Text = item.WeaponLevel?.ToString() ?? "";
        ItemEditArmorLevel.Text = item.ArmorLevel?.ToString() ?? "";
        SetComboByContent(ItemEditGender, item.Gender ?? "Both");
        ItemEditView.Text = item.View?.ToString() ?? "";
        ItemEditAliasName.Text = item.AliasName ?? "";
        ItemEditRefineable.IsChecked = item.Refineable;
        ItemEditGradable.IsChecked = item.Gradable;

        PopulateItemEditJobCheckboxes(item.Jobs ?? new Dictionary<string, bool>());
        PopulateItemEditLocationCheckboxes(item.Locations ?? new Dictionary<string, bool>());

        DetailDescription.Text = !string.IsNullOrEmpty(item.Description) ? item.Description : (App.ItemInfoDescriptions.TryGetValue(item.Id, out var desc) ? desc : "");
        _originalItemScript = item.Script ?? "";
        if (ItemEditBonusCombo != null)
            ItemEditBonusCombo.ItemsSource = BonusEffectRegistry.All;
        _itemBonusList.Clear();
        _itemBonusList.AddRange(BonusEffectRegistry.ParseScript(item.Script));
        RefreshItemBonusListUI();
        ItemEditScript.Text = item.Script ?? "";
        ItemEditEquipScript.Text = item.EquipScript ?? "";
        ItemEditUnEquipScript.Text = item.UnEquipScript ?? "";

        SaveButton.Visibility = Visibility.Visible;
        SaveButton.Content = "SAVE";
        if (ItemDeleteButton != null)
            ItemDeleteButton.Visibility = Visibility.Visible;
        if (ExtractAllRelatedButton != null)
            ExtractAllRelatedButton.Visibility = Visibility.Collapsed;
        if (OpenInGrfEditorButton != null)
            OpenInGrfEditorButton.Visibility = Visibility.Collapsed;
        ItemDiffExpander.Visibility = Visibility.Visible;
        ItemDiffTextBox.Text = "";

        LoadItemPreview(item);

        if (App.ItemPathService != null && ItemRelatedFilesListBox != null && ItemRelatedFilesExpander != null)
        {
            var related = App.ItemPathService.GetRelatedPaths(item);
            ItemRelatedFilesListBox.ItemsSource = related.Select(r => $"{r.Label}: {r.Path}").ToList();
            ItemRelatedFilesExpander.Visibility = related.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        PopulateItemReferences(item.Id, item.AegisName ?? "");
    }

    private static void SetComboByContent(System.Windows.Controls.ComboBox? combo, string content)
    {
        if (combo == null || string.IsNullOrEmpty(content)) return;
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem cbi && (cbi.Content?.ToString() ?? "").Equals(content, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        combo.Text = content;
    }

    private void PopulateItemEditJobCheckboxes(Dictionary<string, bool> jobs)
    {
        if (ItemEditJobsPanel == null) return;
        ItemEditJobsPanel.Children.Clear();
        foreach (var job in _jobNames)
        {
            var cb = new System.Windows.Controls.CheckBox { Content = job, Margin = new Thickness(0, 2, 12, 2) };
            cb.IsChecked = jobs != null && jobs.TryGetValue(job, out var v) && v;
            ItemEditJobsPanel.Children.Add(cb);
        }
    }

    private void PopulateItemEditLocationCheckboxes(Dictionary<string, bool> locations)
    {
        if (ItemEditLocationsPanel == null) return;
        ItemEditLocationsPanel.Children.Clear();
        foreach (var loc in _locationNames)
        {
            var cb = new System.Windows.Controls.CheckBox { Content = loc, Margin = new Thickness(0, 2, 12, 2) };
            cb.IsChecked = locations != null && locations.TryGetValue(loc, out var v) && v;
            ItemEditLocationsPanel.Children.Add(cb);
        }
    }

    private void LoadItemPreview(ItemEntry item)
    {
        bool hasSpritePreview = false;
        if (App.SpriteLookupService != null)
        {
            var resourceName = GetItemResourceNameForIcon(item);
            var (actPath, sprPath) = App.SpriteLookupService.FindMonsterSprite(resourceName);
            if (actPath == null && sprPath == null)
                (actPath, sprPath) = App.SpriteLookupService.FindMonsterSprite(item.AegisName);
            if (actPath == null && sprPath == null)
                (actPath, sprPath) = App.SpriteLookupService.FindMonsterSprite(item.Id.ToString());
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
        if (!hasSpritePreview)
        {
            SetPreviewMode(PreviewMode.Image);
            CenterPreviewImage.Source = LoadItemIcon(item);
        }
    }

    private static string GetItemResourceNameForIcon(ItemEntry item)
    {
        if (item == null) return "";
        if (App.ClientItemInfoService != null && App.ClientItemInfoService.TryGet(item.Id, out var entry) && entry != null)
        {
            var r = entry.IdentifiedResourceName ?? entry.UnidentifiedResourceName;
            if (!string.IsNullOrWhiteSpace(r)) return r;
        }
        return string.IsNullOrWhiteSpace(item.ResourceName) ? (item.AegisName ?? "") : item.ResourceName;
    }

    private BitmapSource? LoadItemIcon(ItemEntry item)
    {
        var resourceName = GetItemResourceNameForIcon(item);
        // Try extracted filesystem textures first
        if (App.FileSystemSpriteSource != null)
        {
            var iconPath = App.FileSystemSpriteSource.FindItemIcon(item.Id, resourceName);
            if (iconPath == null)
                iconPath = App.FileSystemSpriteSource.FindItemIcon(item.Id, item.AegisName);
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
            $"data\\texture\\effect\\{resourceName}.bmp",
            $"data\\texture\\effect\\item\\{item.Id}.bmp",
            $"data\\texture\\effect\\collection\\{item.Id}.bmp",
            $"data\\texture\\effect\\collection\\{item.AegisName}.bmp",
            $"data\\texture\\effect\\collection\\{resourceName}.bmp",
            $@"data\texture\유저인터페이스\item\{item.Id}.bmp",
            $@"data\texture\유저인터페이스\item\{resourceName}.bmp",
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
        _currentItemForEdit = null;
        var path = entry.Path ?? "";
        var displayName = entry.DisplayName ?? path;
        if (App.ItemPathService != null && App.ItemPathService.IsItemRelatedPath(path))
        {
            var item = App.ItemPathService.TryGetItemForPath(path);
            if (item != null)
                displayName = $"{displayName} — Item: {item.DisplayName} (ID {item.Id})";
        }
        ItemEditName.Text = displayName;
        ItemEditId.Text = "—";
        ItemEditType.SelectedIndex = -1;
        ItemEditType.Text = "—";
        ItemEditSubType.Items.Clear();
        ItemEditSubType.Text = "";
        DetailDescription.Text = "";
        ItemEditScript.Text = "{},{},{}";
        SaveButton.Visibility = Visibility.Collapsed;
        SaveButton.Content = "SAVE";
        if (ItemDeleteButton != null)
            ItemDeleteButton.Visibility = Visibility.Collapsed;
        if (ExtractAllRelatedButton != null)
            ExtractAllRelatedButton.Visibility = Visibility.Collapsed;
        if (OpenInGrfEditorButton != null)
            OpenInGrfEditorButton.Visibility = Visibility.Collapsed;
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

    private void ShowExtractedAssetDetails(Models.ExtractedAssetEntry entry)
    {
        _currentExtractedAsset = entry;

        // Populate new panel metadata
        if (ExtractedBaseName != null) ExtractedBaseName.Text = "Base: " + entry.BaseName;
        if (ExtractedFolder != null) ExtractedFolder.Text = "Folder: " + entry.DataRelativeFolder;
        if (ExtractedExtensions != null) ExtractedExtensions.Text = "Extensions: " + entry.ExtensionsSummary;
        if (ExtractedCategory != null) ExtractedCategory.Text = "Suggested: " + entry.SuggestedCategory;

        // Populate related files in new panel
        if (ExtractedRelatedFilesListBox != null && ExtractedRelatedFilesExpander != null)
        {
            var sourceList = (entry.SourcePaths ?? new List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ExtractedRelatedFilesListBox.ItemsSource = sourceList;
            ExtractedRelatedFilesExpander.Header = $"Related files ({sourceList.Count})";
            ExtractedRelatedFilesExpander.IsExpanded = sourceList.Count > 0;
        }

        // Populate Assign-to combo with items/monsters/NPCs based on entity type
        PopulateExtractedAssignTargets();

        // Auto-select entity type from SuggestedCategory
        if (ExtractedEntityTypeCombo != null)
        {
            var suggestedIndex = entry.SuggestedCategory switch
            {
                "MONSTERS" => 1,
                "NPCs" => 2,
                _ => 0
            };
            ExtractedEntityTypeCombo.SelectedIndex = suggestedIndex;
        }

        // Show entity-specific properties
        UpdateExtractedEntityProperties(entry);

        // Preview (static only)
        CenterPreviewImage.Source = null;
        var hasStatic = TryPreviewRelatedFile(entry.PreviewPath);
        if (!hasStatic)
            TryPreviewRelatedFile(entry.SprPath);
    }

    private static string BuildExtractedTextPreview(Models.ExtractedAssetEntry entry)
    {
        var textExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".lua", ".lub", ".json", ".xml", ".csv", ".log", ".ini", ".conf", ".yml", ".yaml"
        };

        foreach (var path in entry.SourcePaths ?? new List<string>())
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;

            var ext = Path.GetExtension(path);
            if (!textExts.Contains(ext))
                continue;

            try
            {
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length == 0)
                    continue;

                // If it looks binary, just show metadata for now.
                if (bytes.Take(Math.Min(bytes.Length, 256)).Any(b => b == 0))
                    return $"Binary-like file: {Path.GetFileName(path)} ({bytes.Length} bytes)";

                var text = File.ReadAllText(path);
                if (text.Length > 4000)
                    text = text[..4000] + Environment.NewLine + "... (truncated)";
                return text;
            }
            catch
            {
                // Try next candidate.
            }
        }

        return "";
    }

    private BitmapSource? LoadBitmapFromFile(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;

            var bytes = File.ReadAllBytes(filePath);
            if (bytes.Length == 0)
                return null;

            var ext = Path.GetExtension(filePath)?.ToLowerInvariant() ?? ".bmp";
            var image = ImageProvider.GetImage(bytes, ext);
            return image?.Cast<BitmapSource>();
        }
        catch
        {
            return null;
        }
    }

    private bool TryPreviewRelatedFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        var splitExtracted = _currentCategory == "EXTRACTED_ASSETS";
        var ext = Path.GetExtension(filePath)?.ToLowerInvariant() ?? "";
        if (ext == ".spr" || ext == ".act")
        {
            var actPath = ext == ".act" ? filePath : Path.ChangeExtension(filePath, ".act");
            var sprPath = ext == ".spr" ? filePath : Path.ChangeExtension(filePath, ".spr");

            if (AssetListBox.SelectedItem is Models.ExtractedAssetEntry selected)
            {
                actPath = !string.IsNullOrWhiteSpace(selected.ActPath) ? selected.ActPath : actPath;
                sprPath = !string.IsNullOrWhiteSpace(selected.SprPath) ? selected.SprPath : sprPath;
            }

            var (actData, sprData) = App.SpriteLookupService.GetSpriteData(actPath, sprPath);
            if (sprData == null || sprData.Length == 0)
                return false;

            SetPreviewMode(PreviewMode.Sprite);
            SpriteViewer.LoadFromData(actData, sprData);
            SpriteViewer.Play();
            if (!SpriteViewer.LastLoadSucceeded)
                return false;
            if (splitExtracted)
                SpritePreviewPanel.Visibility = Visibility.Visible;
            return true;
        }

        if (ext == ".pal")
        {
            var swatch = LoadPalettePreviewFromFile(filePath);
            if (swatch == null)
                return false;
            SetPreviewMode(PreviewMode.Image);
            CenterPreviewImage.Source = swatch;
            if (splitExtracted)
                StaticPreviewPanel.Visibility = Visibility.Visible;
            return true;
        }

        var preview = LoadBitmapFromFile(filePath);
        if (preview == null)
            return false;
        SetPreviewMode(PreviewMode.Image);
        CenterPreviewImage.Source = preview;
        if (splitExtracted)
            StaticPreviewPanel.Visibility = Visibility.Visible;
        return true;
    }

    private BitmapSource? LoadPalettePreviewFromFile(string filePath)
    {
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            if (bytes == null || bytes.Length == 0)
                return null;
            return BuildPaletteSwatchBitmap(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? BuildPaletteSwatchBitmap(byte[] paletteBytes)
    {
        if (paletteBytes == null || paletteBytes.Length == 0)
            return null;

        const int cols = 16;
        const int rows = 16;
        const int cell = 10;
        var width = cols * cell;
        var height = rows * cell;
        var stride = width * 4;
        var pixels = new byte[height * stride];

        for (int i = 0; i < cols * rows; i++)
        {
            byte r;
            byte g;
            byte b;
            byte a = 255;

            if (paletteBytes.Length >= 1024)
            {
                var o = i * 4;
                b = paletteBytes[o];
                g = paletteBytes[o + 1];
                r = paletteBytes[o + 2];
                a = paletteBytes[o + 3];
            }
            else if (paletteBytes.Length >= 768)
            {
                var o = i * 3;
                r = paletteBytes[o];
                g = paletteBytes[o + 1];
                b = paletteBytes[o + 2];
            }
            else
            {
                break;
            }

            var col = i % cols;
            var row = i / cols;
            var startX = col * cell;
            var startY = row * cell;

            for (var y = 0; y < cell; y++)
            {
                var py = startY + y;
                for (var x = 0; x < cell; x++)
                {
                    var px = startX + x;
                    var idx = py * stride + (px * 4);
                    pixels[idx] = b;
                    pixels[idx + 1] = g;
                    pixels[idx + 2] = r;
                    pixels[idx + 3] = a;
                }
            }
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    private void ClearDetails()
    {
        _currentExtractedAsset = null;
        _currentItemForEdit = null;
        SetPreviewMode(PreviewMode.None);
        if (ItemRelatedFilesListBox != null)
            ItemRelatedFilesListBox.ItemsSource = null;
        if (ItemRelatedFilesExpander != null)
            ItemRelatedFilesExpander.Visibility = Visibility.Collapsed;

        ItemEditId.Text = "";
        ItemEditAegisName.Text = "";
        ItemEditName.Text = "";
        ItemEditType.SelectedIndex = -1;
        ItemEditSubType.Items.Clear();
        ItemEditSubType.Text = "";
        ItemEditBuy.Text = "";
        ItemEditSell.Text = "";
        ItemEditWeight.Text = "";
        ItemEditAttack.Text = "";
        ItemEditMagicAttack.Text = "";
        ItemEditDefense.Text = "";
        ItemEditRange.Text = "";
        ItemEditSlots.Text = "";
        ItemEditEquipLevelMin.Text = "";
        ItemEditEquipLevelMax.Text = "";
        ItemEditWeaponLevel.Text = "";
        ItemEditArmorLevel.Text = "";
        ItemEditGender.SelectedIndex = 0;
        ItemEditView.Text = "";
        ItemEditAliasName.Text = "";
        ItemEditRefineable.IsChecked = false;
        ItemEditGradable.IsChecked = false;
        ItemEditJobsPanel?.Children.Clear();
        ItemEditLocationsPanel?.Children.Clear();
        DetailDescription.Text = "";
        _itemBonusList.Clear();
        RefreshItemBonusListUI();
        ItemEditScript.Text = "";
        ItemEditEquipScript.Text = "";
        ItemEditUnEquipScript.Text = "";

        SaveButton.Visibility = Visibility.Collapsed;
        SaveButton.Content = "SAVE";
        if (ItemDeleteButton != null)
            ItemDeleteButton.Visibility = Visibility.Collapsed;
        if (ExtractAllRelatedButton != null)
            ExtractAllRelatedButton.Visibility = Visibility.Collapsed;
        if (OpenInGrfEditorButton != null)
            OpenInGrfEditorButton.Visibility = Visibility.Collapsed;
        if (ExtractedAssignmentPanel != null)
            ExtractedAssignmentPanel.Visibility = Visibility.Collapsed;
        ItemDiffExpander.Visibility = Visibility.Collapsed;
    }

    private void ItemDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (AssetListBox.SelectedItem is not ItemEntry item)
        {
            System.Windows.MessageBox.Show(this, "Select an item to delete.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var path = App.ItemDbService?.GetSaveTargetPath(item);
        if (string.IsNullOrEmpty(path) || !path.Contains("import", StringComparison.OrdinalIgnoreCase))
        {
            System.Windows.MessageBox.Show(this, "Cannot delete: this item is from the base database. Only custom items in db/import/ can be deleted.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (item.SourceIndex < 0)
        {
            System.Windows.MessageBox.Show(this, "Cannot delete: item source index unknown.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var confirm = System.Windows.MessageBox.Show(this,
            $"Delete item {item.Id} ({item.AegisName})?\n\nThis will remove it from db/import/item_db.yml.",
            "RoDbEditor", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            if (!(App.ItemDbService?.RemoveEntryAt(path, item.SourceIndex) ?? false))
            {
                System.Windows.MessageBox.Show(this, "Delete failed.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            var match = _operationsLog.Records.FirstOrDefault(r =>
                r.EntityKind == OperationEntityKind.Item && r.Id == item.Id && r.FilePath == path);
            if (match != null)
                _operationsLog.RemoveRecord(match);
            ReloadDataAfterFileChange();
            RefreshOperationsList();
            RefreshList();
            ClearDetails();
            AssetListBox.SelectedItem = null;
            System.Windows.MessageBox.Show(this, "Item deleted.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, "Delete failed: " + ex.Message, "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MonsterDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMob == null)
        {
            System.Windows.MessageBox.Show(this, "Select a monster to delete.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (string.IsNullOrEmpty(App.MobDbService?.GetImportMobDbPath()))
        {
            System.Windows.MessageBox.Show(this, "Cannot delete: no import mob_db.yml found.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var confirm = System.Windows.MessageBox.Show(this,
            $"Delete monster {_currentMob.Id} ({_currentMob.AegisName})?\n\nThis will remove it from db/import/mob_db.yml (if present).",
            "RoDbEditor", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            if (!(App.MobDbService?.DeleteMobFromImportById(_currentMob.Id) ?? false))
            {
                System.Windows.MessageBox.Show(this, "Cannot delete: this monster is not in db/import/mob_db.yml. Only custom monsters can be deleted.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var path = App.MobDbService.GetImportMobDbPath();
            var match = _operationsLog.Records.FirstOrDefault(r =>
                r.EntityKind == OperationEntityKind.Mob && r.Id == _currentMob.Id && r.FilePath == path);
            if (match != null)
                _operationsLog.RemoveRecord(match);
            ReloadDataAfterFileChange();
            RefreshOperationsList();
            RefreshList();
            _currentMob = null;
            _currentMobSnapshot = null;
            MonsterDetailsPanel.Visibility = Visibility.Collapsed;
            ItemDetailsPanel.Visibility = Visibility.Visible;
            ClearDetails();
            AssetListBox.SelectedItem = null;
            System.Windows.MessageBox.Show(this, "Monster deleted.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, "Delete failed: " + ex.Message, "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (AssetListBox.SelectedItem is ItemEntry selectedItem)
        {
            SaveItemFromEditForm(selectedItem);
            return;
        }

        if (AssetListBox.SelectedItem is Models.ExtractedAssetEntry extracted)
        {
            SaveExtractedAssetSelection(extracted);
        }
    }

    private List<string> ValidateItemEntry(ItemEntry item)
    {
        var warnings = new List<string>();

        // Name length (ITEM_NAME_LENGTH = 50, max 49 usable)
        if (!string.IsNullOrEmpty(item.AegisName) && item.AegisName.Length > 49)
            warnings.Add($"AegisName is {item.AegisName.Length} chars (max 49). rAthena will truncate it.");
        if (!string.IsNullOrEmpty(item.Name) && item.Name.Length > 49)
            warnings.Add($"Name is {item.Name.Length} chars (max 49). rAthena will truncate it.");

        // SubType cleanup
        if (!string.IsNullOrEmpty(item.SubType) &&
            item.Type != "Weapon" && item.Type != "Ammo" && item.Type != "Card")
        {
            warnings.Add($"SubType '{item.SubType}' cleared — only valid for Weapon/Ammo/Card.");
            item.SubType = null;
        }

        // Numeric range checks
        if (item.Slots.HasValue && (item.Slots.Value < 0 || item.Slots.Value > 4))
            warnings.Add($"Slots={item.Slots.Value} out of range (0-4).");
        if (item.WeaponLevel.HasValue && (item.WeaponLevel.Value < 0 || item.WeaponLevel.Value > 5))
            warnings.Add($"WeaponLevel={item.WeaponLevel.Value} out of range (0-5).");
        if (item.ArmorLevel.HasValue && (item.ArmorLevel.Value < 0 || item.ArmorLevel.Value > 2))
            warnings.Add($"ArmorLevel={item.ArmorLevel.Value} out of range (0-2).");
        if (item.Defense.HasValue && (item.Defense.Value < 0 || item.Defense.Value > 32767))
            warnings.Add($"Defense={item.Defense.Value} out of range (0-32767).");
        if (item.EquipLevelMin.HasValue && (item.EquipLevelMin.Value < 0 || item.EquipLevelMin.Value > 275))
            warnings.Add($"EquipLevelMin={item.EquipLevelMin.Value} out of range (0-275).");
        if (item.EquipLevelMax.HasValue && (item.EquipLevelMax.Value < 0 || item.EquipLevelMax.Value > 275))
            warnings.Add($"EquipLevelMax={item.EquipLevelMax.Value} out of range (0-275).");
        if (item.EquipLevelMin.HasValue && item.EquipLevelMax.HasValue &&
            item.EquipLevelMax.Value > 0 && item.EquipLevelMax.Value < item.EquipLevelMin.Value)
            warnings.Add($"EquipLevelMax ({item.EquipLevelMax.Value}) < EquipLevelMin ({item.EquipLevelMin.Value}).");

        // Price exploit: buy/124 < sell/75 → rAthena forces sell to 1
        if (item.Buy.HasValue && item.Sell.HasValue && item.Sell.Value > 0 && item.Buy.Value > 0)
        {
            if ((double)item.Buy.Value / 124.0 < (double)item.Sell.Value / 75.0)
                warnings.Add($"Price exploit: Buy({item.Buy.Value})/124 < Sell({item.Sell.Value})/75. rAthena will force Sell=1.");
        }

        // Type-specific field conflicts
        bool isWeapon = item.Type == "Weapon";
        bool isAmmo = item.Type == "Ammo";
        bool isArmor = item.Type == "Armor";
        bool isShadow = item.Type == "ShadowGear";

        if (item.Attack.HasValue && item.Attack.Value > 0 && !isWeapon && !isAmmo)
        { warnings.Add("Attack cleared — only valid for Weapon/Ammo."); item.Attack = null; }
        if (item.MagicAttack.HasValue && item.MagicAttack.Value > 0 && !isWeapon)
        { warnings.Add("MagicAttack cleared — only valid for Weapon."); item.MagicAttack = null; }
        if (item.Defense.HasValue && item.Defense.Value > 0 && !isArmor && !isShadow)
        { warnings.Add("Defense cleared — only valid for Armor/ShadowGear."); item.Defense = null; }
        if (item.Range.HasValue && item.Range.Value > 0 && !isWeapon)
        { warnings.Add("Range cleared — only valid for Weapon."); item.Range = null; }
        if (item.WeaponLevel.HasValue && item.WeaponLevel.Value > 0 && !isWeapon)
        { warnings.Add("WeaponLevel cleared — only valid for Weapon."); item.WeaponLevel = null; }
        if (item.ArmorLevel.HasValue && item.ArmorLevel.Value > 0 && !isArmor)
        { warnings.Add("ArmorLevel cleared — only valid for Armor."); item.ArmorLevel = null; }

        // Musical=Male, Whip=Female
        if (isWeapon && item.SubType == "Musical" && item.Gender != "Male")
        { warnings.Add("Musical instruments forced to Gender=Male."); item.Gender = "Male"; }
        if (isWeapon && item.SubType == "Whip" && item.Gender != "Female")
        { warnings.Add("Whips forced to Gender=Female."); item.Gender = "Female"; }

        return warnings;
    }

    private void SaveItemFromEditForm(ItemEntry item)
    {
        if (App.ItemDbService == null) return;

        item.AegisName = ItemEditAegisName?.Text?.Trim() ?? "";
        item.Name = ItemEditName?.Text?.Trim() ?? "";
        item.Type = (ItemEditType?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? ItemEditType?.Text ?? "Etc";
        item.SubType = (ItemEditSubType?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? ItemEditSubType?.Text;
        item.Buy = int.TryParse(ItemEditBuy?.Text, out var buy) ? buy : (int?)null;
        item.Sell = int.TryParse(ItemEditSell?.Text, out var sell) ? sell : (int?)null;
        item.Weight = int.TryParse(ItemEditWeight?.Text, out var wt) ? wt : (int?)null;
        item.Attack = int.TryParse(ItemEditAttack?.Text, out var atk) ? atk : (int?)null;
        item.MagicAttack = int.TryParse(ItemEditMagicAttack?.Text, out var ma) ? ma : (int?)null;
        item.Defense = int.TryParse(ItemEditDefense?.Text, out var def) ? def : (int?)null;
        item.Range = int.TryParse(ItemEditRange?.Text, out var r) ? r : (int?)null;
        item.Slots = int.TryParse(ItemEditSlots?.Text, out var slots) ? slots : (int?)null;
        item.EquipLevelMin = int.TryParse(ItemEditEquipLevelMin?.Text, out var elmin) ? elmin : (int?)null;
        item.EquipLevelMax = int.TryParse(ItemEditEquipLevelMax?.Text, out var elmax) ? elmax : (int?)null;
        item.WeaponLevel = int.TryParse(ItemEditWeaponLevel?.Text, out var wl) ? wl : (int?)null;
        item.ArmorLevel = int.TryParse(ItemEditArmorLevel?.Text, out var al) ? al : (int?)null;
        item.Gender = (ItemEditGender?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Both";
        item.View = int.TryParse(ItemEditView?.Text, out var v) ? v : (int?)null;
        item.AliasName = ItemEditAliasName?.Text?.Trim();
        item.Refineable = ItemEditRefineable?.IsChecked == true;
        item.Gradable = ItemEditGradable?.IsChecked == true;
        item.Script = string.IsNullOrWhiteSpace(ItemEditScript?.Text) ? null : ItemEditScript.Text.Trim();
        item.EquipScript = string.IsNullOrWhiteSpace(ItemEditEquipScript?.Text) ? null : ItemEditEquipScript.Text.Trim();
        item.UnEquipScript = string.IsNullOrWhiteSpace(ItemEditUnEquipScript?.Text) ? null : ItemEditUnEquipScript.Text.Trim();
        item.Description = string.IsNullOrWhiteSpace(DetailDescription?.Text) ? null : DetailDescription.Text.Trim();

        item.Jobs = new Dictionary<string, bool>();
        if (ItemEditJobsPanel != null)
        {
            foreach (var child in ItemEditJobsPanel.Children)
            {
                if (child is System.Windows.Controls.CheckBox cb && cb.IsChecked == true)
                    item.Jobs[cb.Content?.ToString() ?? ""] = true;
            }
        }
        item.Locations = new Dictionary<string, bool>();
        if (ItemEditLocationsPanel != null)
        {
            foreach (var child in ItemEditLocationsPanel.Children)
            {
                if (child is System.Windows.Controls.CheckBox cb && cb.IsChecked == true)
                    item.Locations[cb.Content?.ToString() ?? ""] = true;
            }
        }

        // --- rAthena Validation ---
        var validationWarnings = ValidateItemEntry(item);
        if (validationWarnings.Count > 0)
        {
            var msg = "Issues found and auto-corrected:\n\n"
                + string.Join("\n", validationWarnings.Select((w, i) => $"  {i + 1}. {w}"))
                + "\n\nContinue saving?";
            var result = System.Windows.MessageBox.Show(this, msg, "rAthena Validation",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
        }

        try
        {
            var path = App.ItemDbService.GetSaveTargetPath(item);
            byte[]? snapshot = null;
            if (!string.IsNullOrEmpty(path) && File.Exists(path) && item.SourceIndex >= 0)
                snapshot = File.ReadAllBytes(path);

            var result = App.ItemDbService.SaveItem(item);
            if (result != null)
            {
                if (result.IsUpdate)
                    _operationsLog.RecordUpdated(OperationEntityKind.Item, item.Id, item.AegisName, item.Name ?? "", result.Path, result.BodyIndex, snapshot);
                else
                    _operationsLog.RecordAdded(OperationEntityKind.Item, item.Id, item.AegisName, item.Name ?? "", result.Path, result.BodyIndex);
                RefreshOperationsList();
            }
            try { App.ItemInfoLuaWriter?.WriteEntry(item); }
            catch (Exception luaEx)
            {
                System.Windows.MessageBox.Show(this,
                    "Item saved to YAML but failed to update itemInfo_C.lua:\n" + luaEx.Message,
                    "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            try
            {
                if (IsHeadgearWithView(item))
                    App.AccessoryIdWriter?.WriteEntry(item);
                App.ClientAssetWriter?.EnsureItemIcon(item);
                App.ClientAssetWriter?.EnsureCollectionIcon(item);
            }
            catch (Exception assetEx)
            {
                System.Windows.MessageBox.Show(this, "Client assets (accessoryid/icon): " + assetEx.Message,
                    "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            System.Windows.MessageBox.Show(this, "Item saved.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, "Error saving item: " + ex.Message, "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveExtractedAssetSelection(Models.ExtractedAssetEntry entry)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select destination root folder",
            UseDescriptionForTitle = true,
            SelectedPath = GetClientRootForAssignment() ?? @"F:\MMORPG\RAGNAROK ONLINE\client"
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        var destinationRoot = dlg.SelectedPath;
        if (string.IsNullOrWhiteSpace(destinationRoot))
            return;

        var pairingMode = GetSelectedExtractedPairingMode();
        try
        {
            var batch = (AssetListBox.ItemsSource as IEnumerable<Models.ExtractedAssetEntry>)?.ToList()
                        ?? new List<Models.ExtractedAssetEntry>();
            if (batch.Count == 0)
                batch.Add(entry);

            var totalCopied = 0;
            var manifests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in batch)
            {
                var result = App.ExtractedAssetService.SavePairedFilesPreserveLayout(asset, destinationRoot, pairingMode);
                totalCopied += result.CopiedCount;
                if (!string.IsNullOrWhiteSpace(result.ManifestPath))
                    manifests.Add(result.ManifestPath);
            }

            System.Windows.MessageBox.Show(this,
                $"Saved {totalCopied} file(s).{Environment.NewLine}" +
                $"Entries processed: {batch.Count}{Environment.NewLine}" +
                $"Mode: {pairingMode}{Environment.NewLine}" +
                $"Root: {destinationRoot}{Environment.NewLine}" +
                $"Manifest: {manifests.FirstOrDefault() ?? "(none)"}",
                "RoDbEditor",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this,
                "Failed to save paired files:" + Environment.NewLine + ex.Message,
                "RoDbEditor",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private string GetSelectedExtractedPairingMode()
    {
        if (ExtractedPairingModeCombo?.SelectedItem is ComboBoxItem item &&
            item.Content is string s && !string.IsNullOrWhiteSpace(s))
        {
            return s.Trim();
        }

        return "SORT_FILE_TYPE";
    }

    private IEnumerable<Models.ExtractedAssetEntry> ApplyExtractedModeFilter(IEnumerable<Models.ExtractedAssetEntry> assets)
    {
        var mode = GetSelectedExtractedPairingMode();
        var key = (mode ?? "").Trim().ToUpperInvariant();
        if (key.Contains("ACT_SPR_ONLY"))
        {
            return assets.Where(a => (a.SourcePaths ?? new List<string>())
                .Any(p =>
                {
                    var e = Path.GetExtension(p);
                    return e.Equals(".act", StringComparison.OrdinalIgnoreCase) ||
                           e.Equals(".spr", StringComparison.OrdinalIgnoreCase);
                }));
        }

        if (key.Contains("PAL_ONLY"))
        {
            return assets.Where(a => (a.SourcePaths ?? new List<string>())
                .Any(p => Path.GetExtension(p).Equals(".pal", StringComparison.OrdinalIgnoreCase)));
        }

        return assets;
    }

    private void ExtractedPairingModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_currentCategory == "EXTRACTED_ASSETS")
            RefreshList();
    }

    private void ItemRelatedFilesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var path = TryGetSelectedRelatedFilePath();
        if (!string.IsNullOrWhiteSpace(path))
            TryPreviewRelatedFile(path);
    }

    private void ItemRelatedFilesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var path = TryGetSelectedRelatedFilePath();
        if (!string.IsNullOrWhiteSpace(path))
            TryPreviewRelatedFile(path);
    }

    private string? TryGetSelectedRelatedFilePath()
    {
        string? selected = null;
        if (_currentExtractedAsset != null && ExtractedRelatedFilesListBox?.SelectedItem is string extSel)
            selected = extSel;
        else if (ItemRelatedFilesListBox?.SelectedItem is string itemSel)
            selected = itemSel;

        if (string.IsNullOrWhiteSpace(selected))
            return null;

        if (File.Exists(selected))
            return selected;

        var marker = ": ";
        var index = selected.IndexOf(marker, StringComparison.Ordinal);
        if (index <= 0 || index + marker.Length >= selected.Length)
            return null;

        var candidate = selected[(index + marker.Length)..].Trim();
        if (File.Exists(candidate))
            return candidate;

        return null;
    }

    private void AssignEntityTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PopulateAssignmentTargets();
        UpdateAssignmentDestinationHint();
    }

    private void PopulateAssignmentTargets()
    {
        if (AssignTargetCombo == null)
            return;

        var options = new List<AssignmentTargetOption>();
        var selectedType = GetSelectedAssignmentEntityType();
        switch (selectedType)
        {
            case SpriteAssignmentEntityType.Npc:
                options.AddRange(App.NpcIndexService.All.Select(n => new AssignmentTargetOption
                {
                    Payload = n,
                    Key = string.IsNullOrWhiteSpace(n.SpriteId) ? n.Name : n.SpriteId,
                    Display = $"{n.Name} [{n.SpriteId}] ({Path.GetFileName(n.FilePath)})"
                }));
                break;
            case SpriteAssignmentEntityType.Monster:
                options.AddRange(App.MobDbService.Mobs.Select(m => new AssignmentTargetOption
                {
                    Payload = m,
                    Key = m.AegisName,
                    Display = $"{m.Id} - {m.DisplayName} [{m.AegisName}]"
                }));
                break;
            default:
                options.AddRange(App.ItemDbService.Items.Select(i => new AssignmentTargetOption
                {
                    Payload = i,
                    Key = i.AegisName,
                    Display = $"{i.Id} - {i.DisplayName} [{i.AegisName}]"
                }));
                break;
        }

        AssignTargetCombo.ItemsSource = options.OrderBy(o => o.Display, StringComparer.OrdinalIgnoreCase).Take(2000).ToList();
        AssignTargetCombo.DisplayMemberPath = nameof(AssignmentTargetOption.Display);
        AssignTargetCombo.SelectedValuePath = nameof(AssignmentTargetOption.Key);
        if (AssignTargetCombo.Items.Count > 0 && AssignTargetCombo.SelectedIndex < 0)
            AssignTargetCombo.SelectedIndex = 0;
    }

    private void UpdateAssignmentDestinationHint()
    {
        if (AssignDestinationHint == null)
            return;

        var selectedType = GetSelectedAssignmentEntityType();
        var folder = selectedType switch
        {
            SpriteAssignmentEntityType.Npc => @"data\sprite\npc",
            SpriteAssignmentEntityType.Monster => @"data\sprite\monster",
            _ => @"data\sprite\item"
        };
        AssignDestinationHint.Text = $"Destination: {folder}";
    }

    private SpriteAssignmentEntityType GetSelectedAssignmentEntityType()
    {
        if (AssignEntityTypeCombo?.SelectedItem is ComboBoxItem item &&
            item.Content is string raw)
        {
            var key = raw.Trim().ToUpperInvariant();
            if (key.Contains("NPC")) return SpriteAssignmentEntityType.Npc;
            if (key.Contains("MONSTER")) return SpriteAssignmentEntityType.Monster;
            return SpriteAssignmentEntityType.Item;
        }

        return SpriteAssignmentEntityType.Npc;
    }

    private static string BuildSpriteKeyFromAssetBase(string? baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName))
            return "CUSTOM_SPRITE";

        var chars = baseName
            .Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_')
            .ToArray();
        var normalized = new string(chars).Trim('_');
        if (string.IsNullOrWhiteSpace(normalized))
            normalized = "CUSTOM_SPRITE";
        return normalized.ToUpperInvariant();
    }

    private string BuildUniqueMonsterAegis(string baseKey)
    {
        var key = baseKey;
        var i = 1;
        while (App.MobDbService.Mobs.Any(m => string.Equals(m.AegisName, key, StringComparison.OrdinalIgnoreCase)))
        {
            key = $"{baseKey}_{i}";
            i++;
        }
        return key;
    }

    private void AssignSpriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentExtractedAsset == null)
        {
            System.Windows.MessageBox.Show(this, "Select an extracted asset first.", "RoDbEditor",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sprPath = _currentExtractedAsset.SprPath;
        var actPath = _currentExtractedAsset.ActPath;
        if (string.IsNullOrWhiteSpace(sprPath) || !File.Exists(sprPath))
        {
            System.Windows.MessageBox.Show(this, "Selected extracted entry does not contain a valid SPR file.", "RoDbEditor",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var entityType = GetSelectedAssignmentEntityType();
        var selectedTarget = AssignTargetCombo?.SelectedItem as AssignmentTargetOption;
        var fallbackKey = BuildSpriteKeyFromAssetBase(_currentExtractedAsset.BaseName);
        var targetKey = selectedTarget?.Key;
        if (string.IsNullOrWhiteSpace(targetKey))
            targetKey = fallbackKey;

        if (entityType == SpriteAssignmentEntityType.Monster)
            targetKey = BuildUniqueMonsterAegis(BuildSpriteKeyFromAssetBase(targetKey));

        var related = new List<string>();
        if (AssignIncludeRelatedCheckBox?.IsChecked == true)
        {
            related = (_currentExtractedAsset.SourcePaths ?? new List<string>())
                .Where(p =>
                {
                    var ext = Path.GetExtension(p).ToLowerInvariant();
                    return ext is ".wav" or ".bmp" or ".png" or ".tga" or ".jpg" or ".jpeg" or ".pal";
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            // Also include .bmp/.png etc. with same base name from other folders (e.g. texture/effect)
            var crossFolderTextures = App.ExtractedAssetService?.FindTextureFilesForBase(_currentExtractedAsset.BaseName) ?? Array.Empty<string>();
            foreach (var p in crossFolderTextures)
            {
                if (!string.IsNullOrWhiteSpace(p) && File.Exists(p) && !related.Contains(p, StringComparer.OrdinalIgnoreCase))
                    related.Add(p);
            }
        }

        var isHeadgear = entityType == SpriteAssignmentEntityType.Item && selectedTarget?.Payload is ItemEntry itemEntry
            && itemEntry.Locations != null
            && itemEntry.Locations.Keys.Any(k => k.StartsWith("Head_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("Costume_Head_", StringComparison.OrdinalIgnoreCase));

        var clientRoot = GetClientRootForAssignment() ?? @"F:\MMORPG\RAGNAROK ONLINE\client";
        var targetGrfPath = App.Config?.TargetGrfPath ?? Path.Combine(clientRoot, App.Config?.TargetGrfFileName ?? "custom.grf");

        var safeBaseForPaths = string.Join("", targetKey.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (string.IsNullOrWhiteSpace(safeBaseForPaths)) safeBaseForPaths = "custom_sprite";

        var showGrfEditorOfferAfterSuccess = false;
        if (entityType == SpriteAssignmentEntityType.Item && !HasTextureInRelatedPaths(related))
        {
            var noBmpMsg = "The .spr and .act you chose has no .bmp with them. Items need an icon for the inventory." +
                Environment.NewLine + Environment.NewLine +
                "We can proceed with .spr and .act only. You will need to add the .bmp manually. Drop it into these GRF paths:" +
                Environment.NewLine + Environment.NewLine +
                "• data\\texture\\유저인터페이스\\item\\" + safeBaseForPaths + ".bmp" +
                Environment.NewLine +
                "• data\\texture\\유저인터페이스\\collection\\" + safeBaseForPaths + ".bmp" +
                Environment.NewLine + Environment.NewLine +
                "I can open GRF Editor with your target GRF so you can transfer the file.";
            var noBmpResult = System.Windows.MessageBox.Show(this, noBmpMsg, "No .bmp found for this item",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (noBmpResult == MessageBoxResult.Cancel)
                return;
            showGrfEditorOfferAfterSuccess = true;
        }

        var req = new SpriteAssignmentRequest
        {
            EntityType = entityType,
            TargetKey = targetKey,
            SourceActPath = actPath ?? "",
            SourceSprPath = sprPath,
            RelatedPaths = related,
            ClientRootPath = clientRoot,
            TargetGrfPath = targetGrfPath,
            IsHeadgear = isHeadgear
        };

        var result = App.SpriteAssignmentService.ExecuteAssignment(req);
        if (!result.Success)
        {
            System.Windows.MessageBox.Show(this,
                "Assignment failed:" + Environment.NewLine + string.Join(Environment.NewLine, result.Errors),
                "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string designation = "Asset files added to GRF.";
        if (entityType == SpriteAssignmentEntityType.Npc && selectedTarget?.Payload is NpcScriptEntry npc)
        {
            designation = App.EntityDesignationService.ApplyNpcSprite(npc, targetKey);
        }
        else if (entityType == SpriteAssignmentEntityType.Monster && selectedTarget?.Payload is MobEntry mob)
        {
            var custom = App.EntityDesignationService.CreateOrUpdateCustomMonsterFrom(mob, targetKey);
            designation = $"Custom monster created: {custom.Id} ({custom.AegisName}) in db/import/mob_db.yml";
        }
        else if (entityType == SpriteAssignmentEntityType.Item && selectedTarget?.Payload is ItemEntry assignedItem)
        {
            assignedItem.ResourceName = targetKey;
            try
            {
                var saveResult = App.ItemDbService?.SaveItem(assignedItem);
                if (saveResult != null)
                    designation = $"Item {assignedItem.Id} ({assignedItem.AegisName}): ResourceName set to '{targetKey}' and saved to db/import/item_db.yml.";
                else
                    designation = $"Item {assignedItem.Id} ({assignedItem.AegisName}): ResourceName set to '{targetKey}' (in memory; save to item_db manually if needed).";
            }
            catch
            {
                designation = $"Item {assignedItem.Id}: ResourceName set to '{targetKey}' in memory.";
            }
            RefreshList();
        }

        var warningText = result.Warnings.Count > 0
            ? Environment.NewLine + "Warnings:" + Environment.NewLine + string.Join(Environment.NewLine, result.Warnings)
            : "";
        System.Windows.MessageBox.Show(this,
            $"Assignment complete.{Environment.NewLine}" +
            $"Entity: {entityType}{Environment.NewLine}" +
            $"Target key: {targetKey}{Environment.NewLine}" +
            $"Added to GRF: {result.CopiedFiles.Count} files{Environment.NewLine}" +
            $"{designation}{Environment.NewLine}" +
            $"Manifest: {result.ManifestPath}{warningText}",
            "RoDbEditor",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        if (showGrfEditorOfferAfterSuccess)
        {
            var launchResult = System.Windows.MessageBox.Show(this,
                "Added .spr and .act to GRF. Would you like me to open GRF Editor so you can add the .bmp manually?",
                "Open GRF Editor?", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (launchResult == MessageBoxResult.Yes)
            {
                var editorPath = FindGrfEditorExecutable();
                if (!string.IsNullOrEmpty(editorPath) && File.Exists(targetGrfPath))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = editorPath,
                            UseShellExecute = true,
                            Arguments = $"\"{targetGrfPath}\""
                        });
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show(this, "Failed to launch GRF Editor: " + ex.Message,
                            "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    System.Windows.MessageBox.Show(this,
                        "GRF Editor.exe not found, or target GRF does not exist.",
                        "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }

    private void OpenInGrfEditorButton_Click(object sender, RoutedEventArgs e)
    {
        if (AssetListBox.SelectedItem is not Models.ExtractedAssetEntry entry)
        {
            System.Windows.MessageBox.Show(this, "Select an extracted asset first.", "RoDbEditor",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var editorPath = FindGrfEditorExecutable();
        if (string.IsNullOrEmpty(editorPath))
        {
            System.Windows.MessageBox.Show(this,
                "GRF Editor.exe not found." + Environment.NewLine +
                "Expected path example:" + Environment.NewLine +
                @"F:\MMORPG\RAGNAROK ONLINE\EDITORS\GRF EDITOR\GRF Editor.exe",
                "RoDbEditor",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var target = TryGetSelectedRelatedFilePath()
                     ?? entry.SprPath
                     ?? entry.ActPath
                     ?? entry.PreviewPath
                     ?? entry.SourcePaths.FirstOrDefault();

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = editorPath,
                UseShellExecute = true,
                Arguments = !string.IsNullOrWhiteSpace(target) ? $"\"{target}\"" : ""
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this,
                "Failed to launch GRF Editor:" + Environment.NewLine + ex.Message,
                "RoDbEditor",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string? FindGrfEditorExecutable()
    {
        var candidates = new[]
        {
            @"F:\MMORPG\RAGNAROK ONLINE\EDITORS\GRF EDITOR\GRF Editor.exe",
            @"F:\MMORPG\RAGNAROK ONLINE\EDITORS\GRF EDITOR\GRFEditor-main\GRF Editor.exe",
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GRF Editor.exe"),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static bool HasTextureInRelatedPaths(IEnumerable<string>? paths)
    {
        var textureExts = new[] { ".bmp", ".png", ".tga", ".jpg", ".jpeg", ".gif" };
        return paths?.Any(p => textureExts.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)) ?? false;
    }

    private static bool IsHeadgearWithView(ItemEntry? item)
    {
        if (item == null || !item.View.HasValue || item.View.Value <= 0)
            return false;

        if (item.Locations == null || item.Locations.Count == 0)
            return false;

        return item.Locations.Any(kv =>
            kv.Value &&
            (kv.Key.StartsWith("Head_", StringComparison.OrdinalIgnoreCase) ||
             kv.Key.StartsWith("Costume_Head_", StringComparison.OrdinalIgnoreCase)));
    }

    private void ExtractAllRelatedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCategory != "EXTRACTED_ASSETS")
            return;

        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select destination root folder for all related extracted files",
            UseDescriptionForTitle = true,
            SelectedPath = GetClientRootForAssignment() ?? @"F:\MMORPG\RAGNAROK ONLINE\client"
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        var destinationRoot = dlg.SelectedPath;
        if (string.IsNullOrWhiteSpace(destinationRoot))
            return;

        var extractedService = App.ExtractedAssetService;
        if (extractedService == null)
        {
            System.Windows.MessageBox.Show(this, "Extracted asset service is not available.", "RoDbEditor",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var assets = (AssetListBox.ItemsSource as IEnumerable<Models.ExtractedAssetEntry>)?.ToList()
                     ?? extractedService.Search("", SearchBox?.Text?.Trim())?.ToList()
                     ?? new List<Models.ExtractedAssetEntry>();
        if (assets.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "No extracted assets to export.", "RoDbEditor",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int totalCopied = 0;
        int failed = 0;
        string? manifestPath = null;

        foreach (var asset in assets)
        {
            try
            {
                var result = extractedService.SavePairedFilesPreserveLayout(asset, destinationRoot, "ALL_RELATED");
                totalCopied += result.CopiedCount;
                if (!string.IsNullOrWhiteSpace(result.ManifestPath))
                    manifestPath = result.ManifestPath;
            }
            catch
            {
                failed++;
            }
        }

        System.Windows.MessageBox.Show(this,
            $"Extracted all related files.{Environment.NewLine}" +
            $"Entries processed: {assets.Count}{Environment.NewLine}" +
            $"Files copied: {totalCopied}{Environment.NewLine}" +
            $"Failed entries: {failed}{Environment.NewLine}" +
            $"Root: {destinationRoot}{Environment.NewLine}" +
            $"Manifest: {manifestPath ?? "(none)"}",
            "RoDbEditor",
            MessageBoxButton.OK,
            failed == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
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
        App.ExtractedAssetService?.ClearCache();

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
        App.Config.ExtractedAssetsPath = path;
        App.Config.Save();
        App.SpriteLookupService.ClearCache();
        App.SpriteLookupService = new SpriteLookupService(App.GrfService, App.FileSystemSpriteSource);
        App.ExtractedAssetService?.ClearCache();

        RefreshList();
        UpdateSourceIndicators();
        System.Windows.MessageBox.Show(this,
            "Extracted assets loaded." + Environment.NewLine +
            $"Sprite files indexed: {App.FileSystemSpriteSource.CachedCount}" + Environment.NewLine +
            $"Paired asset groups: {App.ExtractedAssetService?.TotalCount ?? 0}",
            "RoDbEditor", MessageBoxButton.OK);
    }

    private void MenuSpriteDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        var report = App.SpriteLookupService?.BuildSpriteDiagnosticReport() ?? "SpriteLookupService not available.";
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RoDbEditor_SpriteDiagnostic.txt");
        try
        {
            System.IO.File.WriteAllText(path, report);
        }
        catch { }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(report);
        sb.AppendLine();
        sb.AppendLine($"Report also saved to: {path}");
        System.Windows.MessageBox.Show(this, sb.ToString(), "Sprite Diagnostic", MessageBoxButton.OK);
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
        SetupFileWatcher(path);
        System.Windows.MessageBox.Show(this, $"Data folder set.\nItems: {App.ItemDbService?.Items?.Count ?? 0}, Mobs: {App.MobDbService?.Mobs?.Count ?? 0}, NPCs: {App.NpcIndexService?.All?.Count ?? 0}.", "RoDbEditor", MessageBoxButton.OK);
    }

    private void OpenClientFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select Ragnarok Client Root Folder (contains System/)",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var clientRoot = dlg.SelectedPath;
        App.Config!.ClientRootPath = clientRoot;
        App.Config.Save();

        // Re-create writers so they use the new client path
        if (!string.IsNullOrEmpty(clientRoot) && Directory.Exists(clientRoot))
        {
            App.ItemInfoLuaWriter = new ItemInfoLuaWriter(clientRoot);
            App.AccessoryIdWriter = new AccessoryIdWriter(clientRoot);
            App.ClientAssetWriter = new ClientAssetWriter(clientRoot);
            App.MobInfoLuaWriter = new MobInfoLuaWriter(clientRoot);
        }

        var sys = Path.Combine(App.Config.ClientRootPath!, "System");
        if (Directory.Exists(sys))
        {
            App.ClientItemInfoService.LoadFromClientSystem(sys);
            App.ClientNpcIdentityService.LoadFromClientSystem(sys);
            App.ClientJobNameService.LoadFromClientSystem(sys);
        }
        System.Windows.MessageBox.Show(this, "Client System files loaded.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SetClientPatchOutput_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select Patch Output Root (RoDbEditor will write System/ + data/ here)",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        App.Config!.ClientPatchRoot = dlg.SelectedPath;
        App.Config.Save();
        System.Windows.MessageBox.Show(this, "Patch output root set.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void WriteItemInfoOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(App.Config?.ClientPatchRoot))
        {
            System.Windows.MessageBox.Show(this, "Set Client Patch Output first (Tools > Set Client Patch Output...).", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var list = new List<ClientItemInfoEntry>();

        foreach (var item in App.ItemDbService.Items.Where(i => i.Id >= 50000))
        {
            if (!App.ClientItemInfoService.TryGet(item.Id, out var c) || c == null)
            {
                c = new ClientItemInfoEntry
                {
                    Id = item.Id,
                    IdentifiedDisplayName = item.DisplayName,
                    IdentifiedResourceName = item.ResourceName ?? item.AegisName,
                    UnidentifiedDisplayName = "????",
                    UnidentifiedResourceName = item.ResourceName ?? item.AegisName,
                    SlotCount = item.Slots ?? 0,
                    ClassNum = item.View ?? 0,
                };
                c.IdentifiedDescriptionName.Add("TODO: description");
                c.UnidentifiedDescriptionName.Add("Unidentified item.");
            }
            else
            {
                c.IdentifiedResourceName ??= item.AegisName;
                c.UnidentifiedResourceName ??= c.IdentifiedResourceName;
                c.ClassNum = item.View ?? c.ClassNum;
                c.SlotCount = item.Slots ?? c.SlotCount;
            }

            list.Add(c);
        }

        var path = App.ClientItemInfoWriter.WriteCustomFile(App.Config.ClientPatchRoot!, list);
        System.Windows.MessageBox.Show(this, $"Wrote: {path}", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AppendMobAvail_Click(object sender, RoutedEventArgs e)
    {
        if (AssetListBox.SelectedItem is not MobEntry mob)
        {
            System.Windows.MessageBox.Show(this, "Select a monster first.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(App.Config?.DataPath))
        {
            System.Windows.MessageBox.Show(this, "Select rAthena folder first (File > Select rAthena folder...).", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var entry = new MobAvailEntry
        {
            Mob = mob.AegisName,
            Sprite = mob.AegisName
        };

        var importPath = App.MobAvailService.AppendOrReplaceImportEntry(App.Config.DataPath, entry);
        System.Windows.MessageBox.Show(this, $"Appended to: {importPath}", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AppendNpcIdentity_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(App.Config?.ClientPatchRoot))
        {
            System.Windows.MessageBox.Show(this, "Set Client Patch Output first (Tools > Set Client Patch Output...).", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (AssetListBox.SelectedItem is not NpcScriptEntry npc)
        {
            System.Windows.MessageBox.Show(this, "Select an NPC first.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var spriteName = !string.IsNullOrWhiteSpace(npc.SpriteId) ? npc.SpriteId : npc.Name;
        if (string.IsNullOrWhiteSpace(spriteName))
        {
            System.Windows.MessageBox.Show(this, "NPC has no SpriteId or Name.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var npcId = 30000;
        if (!string.IsNullOrWhiteSpace(npc.SpriteId) && int.TryParse(npc.SpriteId, out var parsed))
            npcId = parsed;

        var p1 = App.ClientNpcIdentityService.AppendToPatchSystem(App.Config.ClientPatchRoot!, spriteName, npcId);
        var p2 = App.ClientJobNameService.AppendToPatchSystem(App.Config.ClientPatchRoot!, spriteName);

        System.Windows.MessageBox.Show(this, $"Wrote:\n{p1}\n{p2}", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
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
                var current = ItemEditScript?.Text ?? "";
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
        CopyCustomOutputItem_Click(sender, e);
    }

    private void MonsterExportButton_Click(object sender, RoutedEventArgs e)
    {
        CopyCustomOutputMob_Click(sender, e);
    }

    private void NpcExportButton_Click(object sender, RoutedEventArgs e)
    {
        CopyCustomOutputNpc_Click(sender, e);
    }

    private void CopyCustomOutputItem_Click(object sender, RoutedEventArgs e)
    {
        if (AssetListBox.SelectedItem is not ItemEntry item)
        {
            System.Windows.MessageBox.Show(this, "Select an item first.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var script = string.IsNullOrWhiteSpace(ItemEditScript?.Text) ? item.Script : ItemEditScript.Text.Trim();
        var text = App.BlueprintExportService.BuildItemBlueprint(item, script);

        var dlg = new TextExportDialog(
            this,
            title: "Item Blueprint",
            header: $"Item {item.Id} — {item.DisplayName}",
            content: text,
            defaultFileName: $"item_{item.Id}_{item.AegisName}_blueprint.txt");

        dlg.ShowDialog();
    }

    private void CopyCustomOutputMob_Click(object sender, RoutedEventArgs e)
    {
        if (AssetListBox.SelectedItem is not MobEntry mob)
        {
            System.Windows.MessageBox.Show(this, "Select a monster first.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var drops = MonsterDropsGrid.Items.Cast<MobDropEntry>().ToList();
        var mvpDrops = MonsterMvpDropsGrid.Items.Cast<MobDropEntry>().ToList();
        var text = App.BlueprintExportService.BuildMobBlueprint(mob, drops, mvpDrops);

        var dlg = new TextExportDialog(
            this,
            title: "Mob Blueprint",
            header: $"Mob {mob.Id} — {mob.DisplayName}",
            content: text,
            defaultFileName: $"mob_{mob.Id}_{mob.AegisName}_blueprint.txt");

        dlg.ShowDialog();
    }

    private void CopyCustomOutputNpc_Click(object sender, RoutedEventArgs e)
    {
        if (AssetListBox.SelectedItem is not NpcScriptEntry npc)
        {
            System.Windows.MessageBox.Show(this, "Select an NPC first.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var text = App.BlueprintExportService.BuildNpcBlueprint(npc);

        var dlg = new TextExportDialog(
            this,
            title: "NPC Blueprint",
            header: $"NPC — {npc.Name}",
            content: text,
            defaultFileName: $"npc_{npc.Name}_blueprint.txt");

        dlg.ShowDialog();
    }

    private CustomBundleExporter BuildCustomBundleExporter()
    {
        var clientRoot = GetClientRootForAssignment();
        var itemExporter = new ItemBundleExporter(clientRoot);
        var mobExporter = new MobBundleExporter(id => App.MobSkillDbService?.GetRowsForMob(id) ?? Array.Empty<MobSkillDbRow>());
        var npcExporter = new NpcBundleExporter();
        return new CustomBundleExporter(itemExporter, mobExporter, npcExporter);
    }

    private void CopyBundleToClipboard(ExportBundle bundle, string label)
    {
        var text = ExportBundleText.ToClipboardText(bundle);
        System.Windows.Clipboard.SetText(text);
        System.Windows.MessageBox.Show(this, $"Copied {label} to clipboard.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnAnalyzeClicked(object sender, RoutedEventArgs e)
    {
        if (App.ItemDbService == null || App.MobDbService == null || App.NpcIndexService == null)
        {
             System.Windows.MessageBox.Show(this, "Services not initialized.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
             return;
        }

        var builder = new WorkspaceIndexBuilder(
            App.ItemDbService,
            App.MobDbService,
            App.NpcIndexService
        );

        var engine = new AnalysisEngine(
            builder,
            App.GrfService,
            App.SpriteLookupService,
            App.FileSystemSpriteSource,
            App.NpcIndexService,
            App.MapIndexService
        );
        var (diagnostics, index) = engine.Analyze();
        _lastWorkspaceIndex = index;

        DiagnosticsListView.ItemsSource = diagnostics;
        
        System.Windows.MessageBox.Show(this, $"Analysis complete. Found {diagnostics.Count} issues.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnAnalyzeFolderClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder to analyze (must contain db/ and/or npc/ subdirectories)",
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        var folderPath = dialog.SelectedPath;

        try
        {
            // Create temporary services for this folder
            var tempItemDb = new ItemDbService();
            var tempMobDb = new MobDbService();
            var tempNpcIndex = new NpcIndexService();
            var tempMapIndex = new MapIndexService();

            tempItemDb.LoadFromDataPath(folderPath);
            tempMobDb.LoadFromDataPath(folderPath);
            tempNpcIndex.LoadFromDataPath(folderPath);
            tempMapIndex.LoadFromDataPath(folderPath);

            var builder = new WorkspaceIndexBuilder(tempItemDb, tempMobDb, tempNpcIndex);

            // Use current GRF/sprite services if available, otherwise null
            var engine = new AnalysisEngine(
                builder,
                App.GrfService,
                App.SpriteLookupService,
                App.FileSystemSpriteSource,
                tempNpcIndex,
                tempMapIndex
            );

            var (diagnostics, _) = engine.Analyze();

            // Sort diagnostics by Code, FilePath, LineNumber
            var sorted = diagnostics
                .OrderBy(d => d.Code)
                .ThenBy(d => d.FilePath)
                .ThenBy(d => d.LineNumber)
                .ToList();

            // Serialize to JSON
            var json = System.Text.Json.JsonSerializer.Serialize(sorted, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });

            // Write to analysis_out.json in the analyzed folder
            var outputPath = Path.Combine(folderPath, "analysis_out.json");
            File.WriteAllText(outputPath, json);

            System.Windows.MessageBox.Show(this,
                $"Analysis complete.\nFound {diagnostics.Count} issues.\nOutput written to:\n{outputPath}",
                "RoDbEditor",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this,
                $"Analysis failed:\n{ex.Message}",
                "RoDbEditor",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounceTimer;
    private bool _isAnalyzing = false;
    private bool _analysisDirty = false;
    private readonly object _analysisGate = new();

    private void SetupFileWatcher(string path)
    {
        if (_watcher != null) return;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

        try
        {
            _watcher = new FileSystemWatcher(path);
            _watcher.IncludeSubdirectories = true;
            _watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName;
            _watcher.InternalBufferSize = 64 * 1024;
            _watcher.Changed += OnFileSystemChanged;
            _watcher.Created += OnFileSystemChanged;
            _watcher.Deleted += OnFileSystemChanged;
            _watcher.Renamed += OnFileSystemRenamed;
            _watcher.Error += (_, e) => System.Diagnostics.Debug.WriteLine($"[Watcher] Error: {e.GetException()}");
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] Failed to setup watcher: {ex.Message}");
        }
    }

    private void OnFileSystemRenamed(object sender, RenamedEventArgs e) => OnFileSystemChanged(sender, e);

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        // Filter: Only care about .yml (DB) and .txt (NPC)
        var ext = Path.GetExtension(e.Name)?.ToLowerInvariant();
        if (ext != ".yml" && ext != ".txt")
            return;

        // Debounce: Reset timer to fire in 500ms
        _debounceTimer?.Dispose();
        _debounceTimer = new System.Threading.Timer(OnDebounceTick, null, 500, System.Threading.Timeout.Infinite);
    }

    private void OnDebounceTick(object? state)
    {
        Dispatcher.BeginInvoke(new System.Action(() =>
        {
            _ = ReloadAndAnalyzeAsync();
        }));
    }

    private async System.Threading.Tasks.Task ReloadAndAnalyzeAsync()
    {
        lock (_analysisGate)
        {
            if (_isAnalyzing)
            {
                _analysisDirty = true;
                return;
            }
            _isAnalyzing = true;
            _analysisDirty = false;
        }

        try
        {
            var dataPath = App.Config?.DataPath;
            if (string.IsNullOrEmpty(dataPath)) return;

            // Heavy work in background: reload + analyze
            var diagnostics = await System.Threading.Tasks.Task.Run(async () =>
            {
                await App.ReloadDataPathAsync(dataPath);
                var builder = new WorkspaceIndexBuilder(
                    App.ItemDbService,
                    App.MobDbService,
                    App.NpcIndexService
                );

                var engine = new AnalysisEngine(
                    builder,
                    App.GrfService,
                    App.SpriteLookupService,
                    App.FileSystemSpriteSource,
                    App.NpcIndexService,
                    App.MapIndexService
                );

                return engine.Analyze();
            });

            // UI updates only on dispatcher
            await Dispatcher.InvokeAsync(() =>
            {
                RefreshList();
                UpdateSourceIndicators();
                DiagnosticsListView.ItemsSource = diagnostics.Diagnostics;
                _lastWorkspaceIndex = diagnostics.Index;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ReloadAndAnalyze] Error: {ex}");
        }
        finally
        {
            bool rerun;
            lock (_analysisGate)
            {
                _isAnalyzing = false;
                rerun = _analysisDirty;
                _analysisDirty = false;
            }

            if (rerun)
            {
                await ReloadAndAnalyzeAsync(); // one rerun pass
            }
        }
    }

    private void CopySelectedDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (DiagnosticsListView?.SelectedItem is not DiagnosticRecord selected)
        {
            System.Windows.MessageBox.Show(this, "Select a diagnostic row first.", "Copy Diagnostics",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        CopyDiagnosticsToClipboard(new[] { selected });
    }

    private void CopyAllDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var items = DiagnosticsListView?.Items?.Cast<object>()
            .OfType<DiagnosticRecord>()
            .ToList();

        if (items == null || items.Count == 0)
        {
            System.Windows.MessageBox.Show(this, "No diagnostics to copy.", "Copy Diagnostics",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        CopyDiagnosticsToClipboard(items);
    }

    private void CopyDiagnosticsToClipboard(IEnumerable<DiagnosticRecord> diagnostics)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Severity\tCode\tMessage\tFile\tLine");

        foreach (var d in diagnostics)
        {
            var severity = d.Severity.ToString();
            var code = d.Code ?? "";
            var message = (d.Message ?? "").Replace("\r", " ").Replace("\n", " ");
            var file = d.FilePath ?? "";
            var line = d.LineNumber.ToString();
            sb.AppendLine($"{severity}\t{code}\t{message}\t{file}\t{line}");
        }

        System.Windows.Clipboard.SetText(sb.ToString());
    }

    private void DiagnosticsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DiagnosticsListView.SelectedItem is not DiagnosticRecord diag) return;
        if (string.IsNullOrEmpty(diag.FilePath) || !File.Exists(diag.FilePath)) return;

        // Force open NpcDetailsPanel and load file
        ItemDetailsPanel.Visibility = Visibility.Collapsed;
        MonsterDetailsPanel.Visibility = Visibility.Collapsed;
        NpcDetailsPanel.Visibility = Visibility.Visible;
        
        // Clear previous context
        NpcShopPanel.Visibility = Visibility.Collapsed;
        NpcWarpPanel.Visibility = Visibility.Collapsed;
        NpcDiffExpander.Visibility = Visibility.Collapsed;
        
        // Setup editor
        if (App.RagnarokScriptHighlighting != null)
            NpcScriptEditor.SyntaxHighlighting = App.RagnarokScriptHighlighting;
            
        NpcDetailName.Text = "FILE: " + System.IO.Path.GetFileName(diag.FilePath);
        NpcDetailMapPos.Text = diag.FilePath;
        NpcDetailType.Text = "TYPE: File View";

        try 
        {
             // Clear previous markers
             _markerService?.Clear();
             
             NpcScriptEditor.Text = File.ReadAllText(diag.FilePath);
             
             if (diag.LineNumber > 0 && diag.LineNumber <= NpcScriptEditor.LineCount)
             {
                 var line = NpcScriptEditor.Document.GetLineByNumber(diag.LineNumber);
                 NpcScriptEditor.ScrollToLine(diag.LineNumber);
                 
                 // Create yellow highlight marker
                 if (_markerService != null)
                 {
                     var marker = _markerService.Create(line.Offset, line.Length);
                     marker.BackgroundColor = Colors.Yellow;
                 }
                 
                 NpcScriptEditor.CaretOffset = line.Offset;
             }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, "Error reading file: " + ex.Message, "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PopulateItemReferences(int itemId, string aegisName)
    {
        if (_lastWorkspaceIndex == null)
        {
            ItemReferencesListView.ItemsSource = null;
            return;
        }

        var references = _lastWorkspaceIndex.References
            .Where(r => r.RefKind == "Item" &&
                       (r.To.Id == itemId || (r.To.Name != null && r.To.Name.Equals(aegisName, StringComparison.OrdinalIgnoreCase))))
            .Select(r => new
            {
                SourceFileName = Path.GetFileName(r.SourceFilePath),
                LineNumber = r.LineNumber,
                Snippet = r.Snippet,
                FullPath = r.SourceFilePath
            })
            .ToList();

        ItemReferencesListView.ItemsSource = references;
    }

    private void ItemReferences_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemReferencesListView.SelectedItem == null) return;

        dynamic selected = ItemReferencesListView.SelectedItem;
        string filePath = selected.FullPath;
        int lineNumber = selected.LineNumber;

        NavigateToFile(filePath, lineNumber);
    }

    private void NavigateToFile(string filePath, int lineNumber)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

        // Force open NpcDetailsPanel and load file
        ItemDetailsPanel.Visibility = Visibility.Collapsed;
        MonsterDetailsPanel.Visibility = Visibility.Collapsed;
        NpcDetailsPanel.Visibility = Visibility.Visible;

        // Clear previous context
        NpcShopPanel.Visibility = Visibility.Collapsed;
        NpcWarpPanel.Visibility = Visibility.Collapsed;
        NpcDiffExpander.Visibility = Visibility.Collapsed;

        // Setup editor
        if (App.RagnarokScriptHighlighting != null)
            NpcScriptEditor.SyntaxHighlighting = App.RagnarokScriptHighlighting;

        NpcDetailName.Text = "FILE: " + Path.GetFileName(filePath);
        NpcDetailMapPos.Text = filePath;
        NpcDetailType.Text = "TYPE: File View";

        try
        {
            // Clear previous markers
            _markerService?.Clear();

            NpcScriptEditor.Text = File.ReadAllText(filePath);

            if (lineNumber > 0 && lineNumber <= NpcScriptEditor.LineCount)
            {
                var line = NpcScriptEditor.Document.GetLineByNumber(lineNumber);
                NpcScriptEditor.ScrollToLine(lineNumber);

                // Create yellow highlight marker
                if (_markerService != null)
                {
                    var marker = _markerService.Create(line.Offset, line.Length);
                    marker.BackgroundColor = Colors.Yellow;
                }

                NpcScriptEditor.CaretOffset = line.Offset;
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, "Error reading file: " + ex.Message, "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Extracted Asset Properties Panel — event handlers (Steps 1/4/5)
    // ═══════════════════════════════════════════════════════════════════

    private readonly List<(string bonusType, int value)> _extractedBonusList = new();

    private void PopulateExtractedAssignTargets()
    {
        if (ExtractedAssignTargetCombo == null) return;
        var options = new List<AssignmentTargetOption>();
        var entityType = GetExtractedEntityType();
        switch (entityType)
        {
            case SpriteAssignmentEntityType.Npc:
                if (App.NpcIndexService?.All != null)
                    options.AddRange(App.NpcIndexService.All.Select(n => new AssignmentTargetOption
                    {
                        Payload = n,
                        Key = string.IsNullOrWhiteSpace(n.SpriteId) ? n.Name : n.SpriteId,
                        Display = $"{n.Name} [{n.SpriteId ?? "—"}] ({Path.GetFileName(n.FilePath)})"
                    }));
                break;
            case SpriteAssignmentEntityType.Monster:
                if (App.MobDbService?.Mobs != null)
                    options.AddRange(App.MobDbService.Mobs.Select(m => new AssignmentTargetOption
                    {
                        Payload = m,
                        Key = m.AegisName,
                        Display = $"{m.Id} - {m.DisplayName} [{m.AegisName}]"
                    }));
                break;
            default:
                if (App.ItemDbService?.Items != null)
                    options.AddRange(App.ItemDbService.Items.Select(i => new AssignmentTargetOption
                    {
                        Payload = i,
                        Key = i.AegisName,
                        Display = $"{i.Id} - {i.DisplayName} [{i.AegisName}]"
                    }));
                break;
        }
        ExtractedAssignTargetCombo.ItemsSource = options.OrderBy(o => o.Display, StringComparer.OrdinalIgnoreCase).Take(2000).ToList();
        ExtractedAssignTargetCombo.DisplayMemberPath = nameof(AssignmentTargetOption.Display);
        if (options.Count > 0 && ExtractedAssignTargetCombo.SelectedIndex < 0)
            ExtractedAssignTargetCombo.SelectedIndex = 0;
    }

    private void ExtractedEntityTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PopulateExtractedAssignTargets();
        UpdateExtractedEntityProperties(_currentExtractedAsset);
    }

    private void UpdateExtractedEntityProperties(Models.ExtractedAssetEntry? entry)
    {
        if (ExtractedItemPropertiesPanel == null) return;

        var entityType = GetExtractedEntityType();
        ExtractedItemPropertiesPanel.Visibility = entityType == SpriteAssignmentEntityType.Item
            ? Visibility.Visible : Visibility.Collapsed;
        if (ExtractedMonsterPropertiesPanel != null)
            ExtractedMonsterPropertiesPanel.Visibility = entityType == SpriteAssignmentEntityType.Monster
                ? Visibility.Visible : Visibility.Collapsed;
        if (ExtractedNpcPropertiesPanel != null)
            ExtractedNpcPropertiesPanel.Visibility = entityType == SpriteAssignmentEntityType.Npc
                ? Visibility.Visible : Visibility.Collapsed;

        // Update destination hint
        if (ExtractedDestinationHint != null)
        {
            var folder = entityType switch
            {
                SpriteAssignmentEntityType.Npc => @"data\sprite\npc",
                SpriteAssignmentEntityType.Monster => @"data\sprite\몬스터",
                _ => @"data\sprite\아이템"
            };
            ExtractedDestinationHint.Text = $"Destination: {folder}";
        }

        // Pre-populate fields
        if (entityType == SpriteAssignmentEntityType.Item)
            PrepopulateItemFields(entry);
        else if (entityType == SpriteAssignmentEntityType.Monster)
            PrepopulateMonsterFields(entry);
        else
            PrepopulateNpcFields(entry);
    }

    private SpriteAssignmentEntityType GetExtractedEntityType()
    {
        if (ExtractedEntityTypeCombo?.SelectedItem is ComboBoxItem item &&
            item.Content is string raw)
        {
            var key = raw.Trim().ToUpperInvariant();
            if (key.Contains("MONSTER")) return SpriteAssignmentEntityType.Monster;
            if (key.Contains("NPC")) return SpriteAssignmentEntityType.Npc;
        }
        return SpriteAssignmentEntityType.Item;
    }

    private void PrepopulateItemFields(Models.ExtractedAssetEntry? entry)
    {
        if (ExtItemId == null) return;
        var nextId = App.ItemDbService?.GetNextCustomItemId() ?? 50000;
        ExtItemId.Text = nextId.ToString();
        var baseName = entry?.BaseName ?? "Custom_Item";
        var aegis = string.Concat(baseName.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));
        ExtItemAegisName.Text = aegis;
        ExtItemName.Text = baseName.Replace('_', ' ');
        ExtItemBuy.Text = "0";
        ExtItemSell.Text = "0";
        ExtItemWeight.Text = "10";
        ExtItemAttack.Text = "0";
        ExtItemDefense.Text = "0";
        ExtItemReqLevel.Text = "1";
        ExtItemSlots.Text = "0";
        if (ExtItemMagicAttack != null) ExtItemMagicAttack.Text = "0";
        if (ExtItemRange != null) ExtItemRange.Text = "0";
        if (ExtItemEquipLevelMax != null) ExtItemEquipLevelMax.Text = "0";
        if (ExtItemWeaponLevel != null) ExtItemWeaponLevel.Text = "0";
        if (ExtItemArmorLevel != null) ExtItemArmorLevel.Text = "0";
        if (ExtItemRefineable != null) ExtItemRefineable.IsChecked = false;
        if (ExtItemGradable != null) ExtItemGradable.IsChecked = false;
        if (ExtItemView != null) ExtItemView.Text = "0";
        if (ExtItemAliasName != null) ExtItemAliasName.Text = "";
        if (ExtItemEquipScript != null) ExtItemEquipScript.Text = "";
        if (ExtItemUnEquipScript != null) ExtItemUnEquipScript.Text = "";

        // Populate bonus combo
        if (ExtItemBonusCombo != null)
            ExtItemBonusCombo.ItemsSource = BonusEffectRegistry.All;

        // Populate Jobs checkboxes
        PopulateExtractedJobCheckboxes();
        PopulateExtractedLocationCheckboxes();

        // Clear bonus list
        _extractedBonusList.Clear();
        RefreshExtractedBonusListUI();
        if (ExtItemScript != null) ExtItemScript.Text = "";
    }

    private void PrepopulateMonsterFields(Models.ExtractedAssetEntry? entry)
    {
        if (ExtMobId == null) return;
        ExtMobId.Text = "3000";
        var baseName = entry?.BaseName ?? "Custom_Monster";
        var aegis = string.Concat(baseName.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));
        ExtMobAegisName.Text = aegis.ToUpperInvariant();
        ExtMobName.Text = baseName.Replace('_', ' ');
        ExtMobLevel.Text = "1";
        ExtMobHp.Text = "100";
        ExtMobBaseExp.Text = "10";
        ExtMobJobExp.Text = "5";
    }

    private void PrepopulateNpcFields(Models.ExtractedAssetEntry? entry)
    {
        if (ExtNpcName == null) return;
        var baseName = entry?.BaseName ?? "Custom_NPC";
        ExtNpcName.Text = baseName.Replace('_', ' ');
        ExtNpcSpriteId.Text = baseName;
        ExtNpcMap.Text = "prontera";
    }

    private static readonly string[] _jobNames = new[]
    {
        "All", "Acolyte", "Alchemist", "Archer", "Assassin", "BardDancer",
        "Blacksmith", "Crusader", "Gunslinger", "Hunter", "KagerouOboro",
        "Knight", "Mage", "Merchant", "Monk", "Ninja", "Novice", "Priest",
        "Rebellion", "Rogue", "Sage", "SoulLinker", "StarGladiator",
        "Summoner", "SuperNovice", "Swordman", "Taekwon", "Thief", "Wizard"
    };

    private static readonly string[] _locationNames = new[]
    {
        "Head_Top", "Head_Mid", "Head_Low", "Armor", "Right_Hand", "Left_Hand",
        "Garment", "Shoes", "Right_Accessory", "Left_Accessory", "Both_Accessory",
        "Costume_Head_Top", "Costume_Head_Mid", "Costume_Head_Low", "Costume_Garment",
        "Ammo",
        "Shadow_Armor", "Shadow_Weapon", "Shadow_Shield", "Shadow_Shoes",
        "Shadow_Right_Accessory", "Shadow_Left_Accessory"
    };

    private void PopulateExtractedJobCheckboxes()
    {
        if (ExtItemJobsPanel == null) return;
        ExtItemJobsPanel.Children.Clear();
        foreach (var job in _jobNames)
        {
            var cb = new System.Windows.Controls.CheckBox { Content = job, Margin = new Thickness(0, 2, 12, 2) };
            if (job == "All") cb.IsChecked = true;
            ExtItemJobsPanel.Children.Add(cb);
        }
    }

    private void PopulateExtractedLocationCheckboxes()
    {
        if (ExtItemLocationsPanel == null) return;
        ExtItemLocationsPanel.Children.Clear();
        foreach (var loc in _locationNames)
        {
            var cb = new System.Windows.Controls.CheckBox { Content = loc, Margin = new Thickness(0, 2, 12, 2) };
            ExtItemLocationsPanel.Children.Add(cb);
        }
    }

    private void ExtItemType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ExtItemSubType == null) return;
        var type = (ExtItemType?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        ExtItemSubType.Items.Clear();
        ExtItemSubType.Text = "";
        if (type == "Weapon")
        {
            foreach (var st in new[] { "Fist", "Dagger", "1hSword", "2hSword", "1hSpear", "2hSpear",
                "1hAxe", "2hAxe", "Mace", "2hMace", "Staff", "Bow", "Knuckle", "Musical",
                "Whip", "Book", "Katar", "Revolver", "Rifle", "Gatling", "Shotgun", "Grenade", "Huuma", "2hStaff" })
                ExtItemSubType.Items.Add(new ComboBoxItem { Content = st });
        }
        else if (type == "Ammo")
        {
            foreach (var st in new[] { "Arrow", "Dagger", "Bullet", "Shell", "Grenade",
                "Shuriken", "Kunai", "Cannonball", "ThrowWeapon" })
                ExtItemSubType.Items.Add(new ComboBoxItem { Content = st });
        }
        else if (type == "Card")
        {
            foreach (var st in new[] { "Normal", "Enchant" })
                ExtItemSubType.Items.Add(new ComboBoxItem { Content = st });
        }
        // Armor and other types: SubType not valid (rAthena rejects it). Use Locations for equip slot.
    }

    private void PopulateItemEditSubTypes()
    {
        if (ItemEditSubType == null) return;
        var type = (ItemEditType?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? ItemEditType?.Text ?? "";
        ItemEditSubType.Items.Clear();
        ItemEditSubType.Text = "";
        if (type == "Weapon")
        {
            foreach (var st in new[] { "Fist", "Dagger", "1hSword", "2hSword", "1hSpear", "2hSpear",
                "1hAxe", "2hAxe", "Mace", "2hMace", "Staff", "Bow", "Knuckle", "Musical",
                "Whip", "Book", "Katar", "Revolver", "Rifle", "Gatling", "Shotgun", "Grenade", "Huuma", "2hStaff" })
                ItemEditSubType.Items.Add(new ComboBoxItem { Content = st });
        }
        else if (type == "Ammo")
        {
            foreach (var st in new[] { "Arrow", "Dagger", "Bullet", "Shell", "Grenade",
                "Shuriken", "Kunai", "Cannonball", "ThrowWeapon" })
                ItemEditSubType.Items.Add(new ComboBoxItem { Content = st });
        }
        else if (type == "Card")
        {
            foreach (var st in new[] { "Normal", "Enchant" })
                ItemEditSubType.Items.Add(new ComboBoxItem { Content = st });
        }
        // Armor and other types: SubType not valid (rAthena rejects it). Use Locations for equip slot.
    }

    private void ItemEditType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PopulateItemEditSubTypes();
    }

    private void ItemEditAddBonus_Click(object sender, RoutedEventArgs e)
    {
        if (ItemEditBonusCombo?.SelectedItem is not BonusEffectDefinition def) return;
        int.TryParse(ItemEditBonusValue?.Text, out var val);
        _itemBonusList.Add((def.BonusConstant, val));
        RefreshItemBonusListUI();
    }

    private void ItemEditRemoveBonus_Click(object sender, RoutedEventArgs e)
    {
        if (ItemEditBonusList == null) return;
        var idx = ItemEditBonusList.SelectedIndex;
        if (idx >= 0 && idx < _itemBonusList.Count)
        {
            _itemBonusList.RemoveAt(idx);
            RefreshItemBonusListUI();
        }
    }

    private void RefreshItemBonusListUI()
    {
        if (ItemEditBonusList == null) return;
        ItemEditBonusList.ItemsSource = null;
        ItemEditBonusList.ItemsSource = _itemBonusList
            .Select(b =>
            {
                var def = BonusEffectRegistry.All.FirstOrDefault(d => d.BonusConstant == b.bonusType);
                var label = def != null ? def.DisplayName : b.bonusType;
                return def != null && !def.TakesValue ? label : $"{label} {b.value}";
            })
            .ToList();
        var script = BonusEffectRegistry.BuildScript(_itemBonusList);
        if (ItemEditScript != null)
            ItemEditScript.Text = script;
    }

    private void ExtItemAddBonus_Click(object sender, RoutedEventArgs e)
    {
        if (ExtItemBonusCombo?.SelectedItem is not BonusEffectDefinition def) return;
        int.TryParse(ExtItemBonusValue?.Text, out var val);
        _extractedBonusList.Add((def.BonusConstant, val));
        RefreshExtractedBonusListUI();
    }

    private void ExtItemRemoveBonus_Click(object sender, RoutedEventArgs e)
    {
        if (ExtItemBonusList == null) return;
        var idx = ExtItemBonusList.SelectedIndex;
        if (idx >= 0 && idx < _extractedBonusList.Count)
        {
            _extractedBonusList.RemoveAt(idx);
            RefreshExtractedBonusListUI();
        }
    }

    private void RefreshExtractedBonusListUI()
    {
        if (ExtItemBonusList == null) return;
        ExtItemBonusList.ItemsSource = null;
        ExtItemBonusList.ItemsSource = _extractedBonusList
            .Select(b =>
            {
                var def = BonusEffectRegistry.All.FirstOrDefault(d => d.BonusConstant == b.bonusType);
                var label = def != null ? def.DisplayName : b.bonusType;
                return def != null && !def.TakesValue ? label : $"{label} {b.value}";
            })
            .ToList();

        var script = BonusEffectRegistry.BuildScript(_extractedBonusList);
        if (ExtItemScriptPreview != null)
            ExtItemScriptPreview.Text = script;
        if (ExtItemScript != null)
            ExtItemScript.Text = script;
    }

    private void ExtractedAssignSpriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentExtractedAsset == null)
        {
            System.Windows.MessageBox.Show(this, "Select an extracted asset first.", "RoDbEditor",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var entry = _currentExtractedAsset;
        if (string.IsNullOrWhiteSpace(entry.SprPath) && string.IsNullOrWhiteSpace(entry.ActPath))
        {
            System.Windows.MessageBox.Show(this, "No .spr/.act file found for this asset.", "RoDbEditor",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var entityType = GetExtractedEntityType();
        var selectedTarget = ExtractedAssignTargetCombo?.SelectedItem as AssignmentTargetOption;
        var targetKey = selectedTarget?.Key;
        if (string.IsNullOrWhiteSpace(targetKey))
        {
            targetKey = entityType switch
            {
                SpriteAssignmentEntityType.Item => ExtItemAegisName?.Text?.Trim(),
                SpriteAssignmentEntityType.Monster => ExtMobAegisName?.Text?.Trim(),
                SpriteAssignmentEntityType.Npc => ExtNpcSpriteId?.Text?.Trim(),
                _ => null
            };
        }
        if (string.IsNullOrWhiteSpace(targetKey))
            targetKey = BuildSpriteKeyFromAssetBase(entry.BaseName);
        if (entityType == SpriteAssignmentEntityType.Monster)
            targetKey = BuildUniqueMonsterAegis(targetKey);

        var clientRoot = GetClientRootForAssignment();
        if (string.IsNullOrWhiteSpace(clientRoot) || !Directory.Exists(clientRoot))
        {
            System.Windows.MessageBox.Show(this,
                "Client root not found. Ensure the client folder exists and GRFs are configured (RoDbEditor.ini or auto-load from client).",
                "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var relatedPaths = new List<string>();
        if (ExtractedIncludeRelatedCheckBox?.IsChecked == true && entry.SourcePaths != null)
        {
            var includeExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".wav", ".bmp", ".png", ".tga", ".jpg", ".jpeg", ".pal" };
            relatedPaths.AddRange(entry.SourcePaths.Where(p =>
                !string.IsNullOrWhiteSpace(p) && includeExts.Contains(Path.GetExtension(p))));
            // Also include .bmp/.png etc. with same base name from other folders (e.g. texture/effect)
            var crossFolderTextures = App.ExtractedAssetService?.FindTextureFilesForBase(entry.BaseName) ?? Array.Empty<string>();
            foreach (var p in crossFolderTextures)
            {
                if (!string.IsNullOrWhiteSpace(p) && File.Exists(p) && !relatedPaths.Contains(p, StringComparer.OrdinalIgnoreCase))
                    relatedPaths.Add(p);
            }
        }

        var isHeadgear = false;
        if (entityType == SpriteAssignmentEntityType.Item && selectedTarget?.Payload is ItemEntry extItemEntry
            && extItemEntry.Locations != null)
        {
            isHeadgear = extItemEntry.Locations.Keys.Any(k => k.StartsWith("Head_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("Costume_Head_", StringComparison.OrdinalIgnoreCase));
        }

        var targetGrfPath = App.Config?.TargetGrfPath ?? Path.Combine(clientRoot, App.Config?.TargetGrfFileName ?? "custom.grf");

        var safeBaseForPaths = string.Join("", targetKey.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (string.IsNullOrWhiteSpace(safeBaseForPaths)) safeBaseForPaths = "custom_sprite";

        var showGrfEditorOfferAfterSuccess = false;
        if (entityType == SpriteAssignmentEntityType.Item && !HasTextureInRelatedPaths(relatedPaths))
        {
            var noBmpMsg = "The .spr and .act you chose has no .bmp with them. Items need an icon for the inventory." +
                Environment.NewLine + Environment.NewLine +
                "We can proceed with .spr and .act only. You will need to add the .bmp manually. Drop it into these GRF paths:" +
                Environment.NewLine + Environment.NewLine +
                "• data\\texture\\유저인터페이스\\item\\" + safeBaseForPaths + ".bmp" +
                Environment.NewLine +
                "• data\\texture\\유저인터페이스\\collection\\" + safeBaseForPaths + ".bmp" +
                Environment.NewLine + Environment.NewLine +
                "I can open GRF Editor with your target GRF so you can transfer the file.";
            var noBmpResult = System.Windows.MessageBox.Show(this, noBmpMsg, "No .bmp found for this item",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (noBmpResult == MessageBoxResult.Cancel)
                return;
            showGrfEditorOfferAfterSuccess = true;
        }

        var req = new SpriteAssignmentRequest
        {
            EntityType = entityType,
            TargetKey = targetKey,
            SourceActPath = entry.ActPath ?? "",
            SourceSprPath = entry.SprPath ?? "",
            RelatedPaths = relatedPaths,
            ClientRootPath = clientRoot,
            TargetGrfPath = targetGrfPath,
            IsHeadgear = isHeadgear
        };

        try
        {
            var result = App.SpriteAssignmentService?.ExecuteAssignment(req);
            if (result == null)
            {
                System.Windows.MessageBox.Show(this, "SpriteAssignmentService not available.", "RoDbEditor");
                return;
            }
            if (result.Success)
            {
                var designation = "";
                if (entityType == SpriteAssignmentEntityType.Npc && selectedTarget?.Payload is NpcScriptEntry npc)
                    designation = App.EntityDesignationService.ApplyNpcSprite(npc, targetKey);
                else if (entityType == SpriteAssignmentEntityType.Monster && selectedTarget?.Payload is MobEntry mob)
                {
                    var custom = App.EntityDesignationService.CreateOrUpdateCustomMonsterFrom(mob, targetKey);
                    designation = $"Custom monster created: {custom.Id} ({custom.AegisName}) in db/import/mob_db.yml";
                }
                else if (entityType == SpriteAssignmentEntityType.Item && selectedTarget?.Payload is ItemEntry assignedItemExt)
                {
                    assignedItemExt.ResourceName = targetKey;
                    try
                    {
                        var saveResult = App.ItemDbService?.SaveItem(assignedItemExt);
                        designation = saveResult != null
                            ? $"Item {assignedItemExt.Id} ({assignedItemExt.AegisName}): ResourceName set to '{targetKey}' and saved to db/import/item_db.yml."
                            : $"Item {assignedItemExt.Id}: ResourceName set to '{targetKey}' (in memory).";
                    }
                    catch
                    {
                        designation = $"Item {assignedItemExt.Id}: ResourceName set to '{targetKey}' in memory.";
                    }
                    RefreshList();
                }
                else
                    designation = $"Added to data\\sprite\\{entityType.ToString().ToLowerInvariant()} in GRF";
                System.Windows.MessageBox.Show(this,
                    $"Sprite assigned successfully.\nAdded {result.CopiedFiles?.Count ?? 0} files to GRF.\n{designation}\nManifest: {result.ManifestPath}",
                    "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);

                if (showGrfEditorOfferAfterSuccess)
                {
                    var launchResult = System.Windows.MessageBox.Show(this,
                        "Added .spr and .act to GRF. Would you like me to open GRF Editor so you can add the .bmp manually?",
                        "Open GRF Editor?", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (launchResult == MessageBoxResult.Yes)
                    {
                        var editorPath = FindGrfEditorExecutable();
                        if (!string.IsNullOrEmpty(editorPath) && File.Exists(targetGrfPath))
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = editorPath,
                                    UseShellExecute = true,
                                    Arguments = $"\"{targetGrfPath}\""
                                });
                            }
                            catch (Exception ex)
                            {
                                System.Windows.MessageBox.Show(this, "Failed to launch GRF Editor: " + ex.Message,
                                    "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }
                        else
                        {
                            System.Windows.MessageBox.Show(this,
                                "GRF Editor.exe not found, or target GRF does not exist.",
                                "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                }
            }
            else
                System.Windows.MessageBox.Show(this,
                    "Assignment failed:\n" + string.Join("\n", result.Errors ?? new List<string>()),
                    "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, "Error during assignment: " + ex.Message, "RoDbEditor",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string? GetClientRootForAssignment()
    {
        if (!string.IsNullOrEmpty(App.Config?.ClientRootPath) && Directory.Exists(App.Config.ClientRootPath))
            return App.Config.ClientRootPath;
        var firstGrf = App.GrfService?.GrfPaths?.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstGrf) && File.Exists(firstGrf))
            return Path.GetDirectoryName(firstGrf);
        var defaultClient = @"F:\MMORPG\RAGNAROK ONLINE\client";
        return Directory.Exists(defaultClient) ? defaultClient : null;
    }

    private void ExtractedViewInGrfButton_Click(object sender, RoutedEventArgs e)
    {
        if (AssetListBox.SelectedItem is not Models.ExtractedAssetEntry entry)
        {
            System.Windows.MessageBox.Show(this, "Select an extracted asset first.", "RoDbEditor",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var target = TryGetSelectedRelatedFilePath()
                     ?? entry.SprPath
                     ?? entry.ActPath
                     ?? entry.PreviewPath
                     ?? entry.SourcePaths?.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(target) || !File.Exists(target))
        {
            System.Windows.MessageBox.Show(this, "No file to open.", "RoDbEditor",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var editorPath = FindGrfEditorExecutable();
        if (!string.IsNullOrEmpty(editorPath))
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = editorPath,
                    UseShellExecute = true,
                    Arguments = $"\"{target}\""
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, "Failed to launch GRF Editor: " + ex.Message,
                    "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = true,
                    Arguments = $"/select,\"{target}\""
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, "Failed to open Explorer: " + ex.Message,
                    "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ExtractedSaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentExtractedAsset == null)
        {
            System.Windows.MessageBox.Show(this, "Select an extracted asset first.", "RoDbEditor",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var entityType = GetExtractedEntityType();
        switch (entityType)
        {
            case SpriteAssignmentEntityType.Item:
                SaveExtractedAsItemEntry();
                break;
            case SpriteAssignmentEntityType.Monster:
                SaveExtractedAsMobEntry();
                break;
            case SpriteAssignmentEntityType.Npc:
                SaveExtractedAsNpcEntry();
                break;
        }
    }

    private void SaveExtractedAsItemEntry()
    {
        if (App.ItemDbService == null)
        {
            System.Windows.MessageBox.Show(this, "ItemDbService not available.", "RoDbEditor");
            return;
        }

        var item = new ItemEntry
        {
            Id = int.TryParse(ExtItemId?.Text, out var id) ? id : App.ItemDbService.GetNextCustomItemId(),
            AegisName = ExtItemAegisName?.Text?.Trim() ?? "Custom_Item",
            Name = ExtItemName?.Text?.Trim() ?? "Custom Item",
            Type = (ExtItemType?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? ExtItemType?.Text ?? "Etc",
            SubType = (ExtItemSubType?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? ExtItemSubType?.Text,
            Buy = int.TryParse(ExtItemBuy?.Text, out var buy) ? buy : (int?)null,
            Sell = int.TryParse(ExtItemSell?.Text, out var sell) ? sell : (int?)null,
            Weight = int.TryParse(ExtItemWeight?.Text, out var wt) ? wt : (int?)null,
            Attack = int.TryParse(ExtItemAttack?.Text, out var atk) ? atk : (int?)null,
            MagicAttack = int.TryParse(ExtItemMagicAttack?.Text, out var ma) ? ma : (int?)null,
            Defense = int.TryParse(ExtItemDefense?.Text, out var def) ? def : (int?)null,
            Range = int.TryParse(ExtItemRange?.Text, out var r) ? r : (int?)null,
            EquipLevelMin = int.TryParse(ExtItemReqLevel?.Text, out var lvl) ? lvl : (int?)null,
            EquipLevelMax = int.TryParse(ExtItemEquipLevelMax?.Text, out var elm) ? elm : (int?)null,
            WeaponLevel = int.TryParse(ExtItemWeaponLevel?.Text, out var wl) ? wl : (int?)null,
            ArmorLevel = int.TryParse(ExtItemArmorLevel?.Text, out var al) ? al : (int?)null,
            Slots = int.TryParse(ExtItemSlots?.Text, out var slots) ? slots : (int?)null,
            Gender = (ExtItemGender?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Both",
            Refineable = ExtItemRefineable?.IsChecked == true,
            Gradable = ExtItemGradable?.IsChecked == true,
            View = int.TryParse(ExtItemView?.Text, out var v) ? v : (int?)null,
            AliasName = ExtItemAliasName?.Text?.Trim(),
            EquipScript = string.IsNullOrWhiteSpace(ExtItemEquipScript?.Text) ? null : ExtItemEquipScript.Text.Trim(),
            UnEquipScript = string.IsNullOrWhiteSpace(ExtItemUnEquipScript?.Text) ? null : ExtItemUnEquipScript.Text.Trim(),
        };

        // Build Jobs dict from checkboxes
        item.Jobs = new Dictionary<string, bool>();
        if (ExtItemJobsPanel != null)
        {
            foreach (var child in ExtItemJobsPanel.Children)
            {
                if (child is System.Windows.Controls.CheckBox cb && cb.IsChecked == true)
                    item.Jobs[cb.Content?.ToString() ?? ""] = true;
            }
        }

        // Build Locations dict from checkboxes
        item.Locations = new Dictionary<string, bool>();
        if (ExtItemLocationsPanel != null)
        {
            foreach (var child in ExtItemLocationsPanel.Children)
            {
                if (child is System.Windows.Controls.CheckBox cb && cb.IsChecked == true)
                    item.Locations[cb.Content?.ToString() ?? ""] = true;
            }
        }

        // Script: authoritative source is the free-text ExtItemScript field
        item.Script = string.IsNullOrWhiteSpace(ExtItemScript?.Text) ? null : ExtItemScript.Text.Trim();

        // --- rAthena Validation ---
        var validationWarnings = ValidateItemEntry(item);
        if (validationWarnings.Count > 0)
        {
            var msg = "Issues found and auto-corrected:\n\n"
                + string.Join("\n", validationWarnings.Select((w, i) => $"  {i + 1}. {w}"))
                + "\n\nContinue saving?";
            var result = System.Windows.MessageBox.Show(this, msg, "rAthena Validation",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
        }

        try
        {
            App.ItemDbService.AddItem(item);
            var result = App.ItemDbService.SaveItem(item);
            if (result != null)
            {
                _operationsLog.RecordAdded(OperationEntityKind.Item, item.Id, item.AegisName, item.Name ?? "", result.Path, result.BodyIndex);
                RefreshOperationsList();
            }
            try
            {
                App.ItemInfoLuaWriter?.WriteEntry(item);
                if (IsHeadgearWithView(item))
                    App.AccessoryIdWriter?.WriteEntry(item);
                App.ClientAssetWriter?.EnsureItemIcon(item);
                App.ClientAssetWriter?.EnsureCollectionIcon(item);
            }
            catch (Exception luaEx)
            {
                System.Windows.MessageBox.Show(this,
                    "Item saved to YAML but failed to update client files:\n" + luaEx.Message,
                    "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            System.Windows.MessageBox.Show(this,
                $"Item saved: {item.Id} ({item.AegisName}) to db/import/item_db.yml",
                "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, "Error saving item: " + ex.Message,
                "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveExtractedAsNpcEntry()
    {
        if (App.NpcScriptWriter == null || string.IsNullOrWhiteSpace(App.Config.DataPath))
        {
            System.Windows.MessageBox.Show(this,
                "rAthena DataPath not set. Use File > Select rAthena folder to set the server path.",
                "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var name = ExtNpcName?.Text?.Trim() ?? "Custom NPC";
        var spriteId = ExtNpcSpriteId?.Text?.Trim() ?? _currentExtractedAsset?.BaseName ?? "custom_npc";
        var map = ExtNpcMap?.Text?.Trim() ?? "prontera";

        var path = App.NpcScriptWriter.WriteEntry(map, 150, 150, 4, name, spriteId);
        if (path == null)
        {
            System.Windows.MessageBox.Show(this, "Failed to create NPC script.", "RoDbEditor",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var clientRoot = GetClientRootForAssignment();
        if (!string.IsNullOrWhiteSpace(clientRoot) && Directory.Exists(clientRoot) &&
            _currentExtractedAsset != null && !string.IsNullOrWhiteSpace(_currentExtractedAsset.SprPath) &&
            File.Exists(_currentExtractedAsset.SprPath))
        {
            var targetGrfPath = App.Config?.TargetGrfPath ?? Path.Combine(clientRoot, App.Config?.TargetGrfFileName ?? "custom.grf");
            var req = new SpriteAssignmentRequest
            {
                EntityType = SpriteAssignmentEntityType.Npc,
                TargetKey = spriteId,
                SourceActPath = _currentExtractedAsset.ActPath ?? "",
                SourceSprPath = _currentExtractedAsset.SprPath,
                RelatedPaths = new List<string>(),
                ClientRootPath = clientRoot,
                TargetGrfPath = targetGrfPath
            };
            App.SpriteAssignmentService?.ExecuteAssignment(req);
        }

        App.NpcIndexService?.LoadFromDataPath(App.Config.DataPath);
        RefreshList();
        System.Windows.MessageBox.Show(this,
            $"NPC script created: {Path.GetFileName(path)}\nMap: {map}, Sprite: {spriteId}",
            "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SaveExtractedAsMobEntry()
    {
        if (App.MobDbService == null)
        {
            System.Windows.MessageBox.Show(this, "MobDbService not available.", "RoDbEditor");
            return;
        }

        var mob = new MobEntry
        {
            Id = int.TryParse(ExtMobId?.Text, out var id) ? id : App.MobDbService.GetNextCustomMobId(),
            AegisName = (ExtMobAegisName?.Text?.Trim() ?? "Custom_Monster").ToUpperInvariant(),
            Name = ExtMobName?.Text?.Trim() ?? "Custom Monster",
            Level = int.TryParse(ExtMobLevel?.Text, out var lvl) ? lvl : 1,
            Hp = int.TryParse(ExtMobHp?.Text, out var hp) ? hp : 100,
            BaseExp = int.TryParse(ExtMobBaseExp?.Text, out var bexp) ? bexp : 10,
            JobExp = int.TryParse(ExtMobJobExp?.Text, out var jexp) ? jexp : 5,
            Race = (ExtMobRace?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? ExtMobRace?.Text ?? "Formless",
            Element = (ExtMobElement?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? ExtMobElement?.Text ?? "Neutral",
            Size = (ExtMobSize?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? ExtMobSize?.Text ?? "Medium",
        };

        try
        {
            App.MobDbService.AddMob(mob);
            var result = App.MobDbService.SaveMob(mob);
            if (result != null)
            {
                _operationsLog.RecordAdded(OperationEntityKind.Mob, mob.Id, mob.AegisName, mob.Name ?? "", result.Path, result.BodyIndex);
                RefreshOperationsList();
            }
            try { App.MobInfoLuaWriter?.WriteEntry(mob); }
            catch (Exception luaEx)
            {
                System.Windows.MessageBox.Show(this,
                    "Monster saved to YAML but failed to update mobinfo_custom.lua:\n" + luaEx.Message,
                    "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            System.Windows.MessageBox.Show(this,
                $"Monster saved: {mob.Id} ({mob.AegisName}) to db/import/mob_db.yml",
                "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, "Error saving monster: " + ex.Message,
                "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshOperationsList()
    {
        if (OperationsListView == null) return;
        OperationsListView.ItemsSource = null;
        OperationsListView.ItemsSource = _operationsLog.Records.ToList();
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        var record = _operationsLog.TryPopLast();
        if (record == null)
        {
            System.Windows.MessageBox.Show(this, "No operation to undo.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            if (record.Kind == OperationKind.Added)
            {
                if (record.EntityKind == OperationEntityKind.Item)
                {
                    var item = App.ItemDbService?.Items?.FirstOrDefault(i => i.Id == record.Id);
                    if (IsHeadgearWithView(item))
                        App.AccessoryIdWriter?.RemoveEntry(item.View.Value, item.AegisName);
                    App.ItemDbService?.RemoveEntryAt(record.FilePath, record.BodyIndex);
                    App.ItemInfoLuaWriter?.RemoveEntry(record.Id);
                }
                else
                {
                    App.MobInfoLuaWriter?.RemoveEntry(record.Id);
                    App.MobDbService?.RemoveEntryAt(record.FilePath, record.BodyIndex);
                }
            }
            else if (record.Kind == OperationKind.Updated && record.PreviousYamlSnapshot != null)
            {
                File.WriteAllBytes(record.FilePath, record.PreviousYamlSnapshot);
            }
            else
            {
                System.Windows.MessageBox.Show(this, "Cannot undo: no snapshot available for update.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ReloadDataAfterFileChange();
            RefreshOperationsList();
            RefreshList();
            System.Windows.MessageBox.Show(this, "Undo completed.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, "Undo failed: " + ex.Message, "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (OperationsListView?.SelectedItem is not OperationRecord record)
        {
            System.Windows.MessageBox.Show(this, "Select an operation in the list to delete.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            bool ok = record.EntityKind == OperationEntityKind.Item
                ? App.ItemDbService?.RemoveEntryAt(record.FilePath, record.BodyIndex) ?? false
                : App.MobDbService?.RemoveEntryAt(record.FilePath, record.BodyIndex) ?? false;
            if (!ok)
            {
                System.Windows.MessageBox.Show(this, "Delete failed.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            _operationsLog.RemoveRecord(record);
            ReloadDataAfterFileChange();
            RefreshOperationsList();
            RefreshList();
            System.Windows.MessageBox.Show(this, "Entry deleted.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, "Delete failed: " + ex.Message, "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteLatestEntryButton_Click(object sender, RoutedEventArgs e)
    {
        var isItemContext = _currentCategory == "ITEMS";
        var record = _operationsLog.GetLastAddedForContext(isItemContext);
        if (record == null)
        {
            var ctx = isItemContext ? "item_db.yml" : "mob_db.yml";
            System.Windows.MessageBox.Show(this, $"No recent Added operation for {ctx}. Switch to ITEMS or MONSTERS tab and add an entry first.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            bool ok = record.EntityKind == OperationEntityKind.Item
                ? App.ItemDbService?.RemoveEntryAt(record.FilePath, record.BodyIndex) ?? false
                : App.MobDbService?.RemoveEntryAt(record.FilePath, record.BodyIndex) ?? false;
            if (!ok)
            {
                System.Windows.MessageBox.Show(this, "Delete failed.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            _operationsLog.RemoveRecord(record);
            ReloadDataAfterFileChange();
            RefreshOperationsList();
            RefreshList();
            System.Windows.MessageBox.Show(this, "Latest entry deleted.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, "Delete failed: " + ex.Message, "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ReloadDataAfterFileChange()
    {
        var dataPath = App.Config?.DataPath;
        if (string.IsNullOrEmpty(dataPath)) return;
        App.ReloadDataPath(dataPath);
        UpdateSourceIndicators();
    }

    private void ExtractedExportButton_Click(object sender, RoutedEventArgs e)
    {
        // Reuse existing export logic
        ItemExportButton_Click(sender, e);
    }

    private void ExtractedCancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (ExtractedAssetPropertiesPanel != null)
            ExtractedAssetPropertiesPanel.Visibility = Visibility.Collapsed;
        ItemDetailsPanel.Visibility = Visibility.Visible;
        ClearDetails();
    }
}

public class AssetEntry
{
    public string? Path { get; set; }
    public string? DisplayName { get; set; }
}
