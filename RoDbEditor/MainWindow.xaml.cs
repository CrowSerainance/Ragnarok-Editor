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
using RoDbEditor.Models;
using RoDbEditor.Services;
using RoDbEditor.Services.Analysis;
using RoDbEditor.UI;

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
    private string _originalMonsterDropsText = "";
    private string _originalNpcScript = "";
    private MobEntry? _currentMob;
    private MobEntry? _currentMobSnapshot;
    private Models.ExtractedAssetEntry? _currentExtractedAsset;
    private TextMarkerService? _markerService;
    private WorkspaceIndex? _lastWorkspaceIndex;

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

    if (AssetListBox.SelectedItem is Models.ExtractedAssetEntry extractedEntry)
    {
        ItemDetailsPanel.Visibility = Visibility.Visible;
        MonsterDetailsPanel.Visibility = Visibility.Collapsed;
        NpcDetailsPanel.Visibility = Visibility.Collapsed;
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
        if (StaticPreviewPanel == null || SpritePreviewPanel == null)
            return;

        if (_currentCategory == "EXTRACTED_ASSETS")
        {
            StaticPreviewPanel.Visibility = Visibility.Visible;
            SpritePreviewPanel.Visibility = Visibility.Visible;
        }
        else
        {
            StaticPreviewPanel.Visibility = Visibility.Visible;
            SpritePreviewPanel.Visibility = Visibility.Collapsed;
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

            switch (mode)
            {
                case PreviewMode.Sprite:
                    SpritePreviewPanel.Visibility = Visibility.Visible;
                    break;
                case PreviewMode.Image:
                    StaticPreviewPanel.Visibility = Visibility.Visible;
                    break;
                default:
                    CenterPreviewImage.Source = null;
                    SpriteViewer.LoadFromData(null, null);
                    break;
            }
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
        App.MobDbService.SaveMob(_currentMob);
        _currentMobSnapshot = CloneMob(_currentMob);
        _originalMonsterDropsText = SerializeMobDrops(_currentMob);
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
        if (ExtractedAssignmentPanel != null)
            ExtractedAssignmentPanel.Visibility = Visibility.Collapsed;
        DetailName.Text = "NAME: " + item.DisplayName;
        DetailId.Text = "ID: " + item.Id;
        DetailType.Text = "TYPE: " + item.Type;
        DetailDescription.Text = App.ItemInfoDescriptions.TryGetValue(item.Id, out var desc) ? desc : "";
        DetailDescription.IsReadOnly = true;
        _originalItemScript = item.Script ?? "";
        DetailScript.Text = _originalItemScript;
        DetailScript.IsReadOnly = false;
        SaveButton.Visibility = Visibility.Visible;
        SaveButton.Content = "SAVE";
        if (ExtractAllRelatedButton != null)
            ExtractAllRelatedButton.Visibility = Visibility.Collapsed;
        if (OpenInGrfEditorButton != null)
            OpenInGrfEditorButton.Visibility = Visibility.Collapsed;
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

        // Populate "Referenced By" list
        PopulateItemReferences(item.Id, item.AegisName);
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
        SaveButton.Content = "SAVE";
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
        DetailName.Text = "NAME: " + entry.BaseName;
        DetailId.Text = "ID: —";
        DetailType.Text = "TYPE: Suggested = " + entry.SuggestedCategory;
        DetailDescription.Text =
            "Folder: " + entry.FolderPath + Environment.NewLine +
            "Data folder: " + entry.DataRelativeFolder + Environment.NewLine +
            "Variant: " + entry.VariantName + Environment.NewLine +
            "Paired: " + entry.ExtensionsSummary;
        DetailDescription.IsReadOnly = true;
        DetailScript.Text = BuildExtractedTextPreview(entry);
        DetailScript.IsReadOnly = true;
        SaveButton.Visibility = Visibility.Visible;
        SaveButton.Content = "SAVE TO LOCATION";
        if (ExtractAllRelatedButton != null)
            ExtractAllRelatedButton.Visibility = Visibility.Visible;
        if (OpenInGrfEditorButton != null)
            OpenInGrfEditorButton.Visibility = Visibility.Visible;
        if (ExtractedAssignmentPanel != null)
            ExtractedAssignmentPanel.Visibility = Visibility.Visible;
        ItemDiffExpander.Visibility = Visibility.Collapsed;
        if (ItemRelatedFilesListBox != null && ItemRelatedFilesExpander != null)
        {
            var sourceList = (entry.SourcePaths ?? new List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ItemRelatedFilesListBox.ItemsSource = sourceList;
            ItemRelatedFilesExpander.Header = $"Related files ({sourceList.Count})";
            ItemRelatedFilesExpander.Visibility = sourceList.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ItemRelatedFilesExpander.IsExpanded = sourceList.Count > 0;
        }

        CenterPreviewImage.Source = null;
        SpriteViewer.LoadFromData(null, null);

        var hasSprite = TryPreviewRelatedFile(entry.SprPath);
        var hasStatic = TryPreviewRelatedFile(entry.PreviewPath);
        if (!hasSprite && !hasStatic)
            SetPreviewMode(PreviewMode.None);

        PopulateAssignmentTargets();
        UpdateAssignmentDestinationHint();
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
        SaveButton.Content = "SAVE";
        if (ExtractAllRelatedButton != null)
            ExtractAllRelatedButton.Visibility = Visibility.Collapsed;
        if (OpenInGrfEditorButton != null)
            OpenInGrfEditorButton.Visibility = Visibility.Collapsed;
        if (ExtractedAssignmentPanel != null)
            ExtractedAssignmentPanel.Visibility = Visibility.Collapsed;
        ItemDiffExpander.Visibility = Visibility.Collapsed;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (AssetListBox.SelectedItem is ItemEntry item)
        {
            item.Script = DetailScript.Text?.Trim();
            App.ItemDbService.SaveItem(item);
            System.Windows.MessageBox.Show(this, "Item saved.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (AssetListBox.SelectedItem is Models.ExtractedAssetEntry extracted)
        {
            SaveExtractedAssetSelection(extracted);
        }
    }

    private void SaveExtractedAssetSelection(Models.ExtractedAssetEntry entry)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select destination root folder",
            UseDescriptionForTitle = true,
            SelectedPath = @"F:\MMORPG\RAGNAROK ONLINE\client"
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
        if (ItemRelatedFilesListBox?.SelectedItem is not string selected)
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
        }

        var req = new SpriteAssignmentRequest
        {
            EntityType = entityType,
            TargetKey = targetKey,
            SourceActPath = actPath ?? "",
            SourceSprPath = sprPath,
            RelatedPaths = related,
            ClientRootPath = @"F:\MMORPG\RAGNAROK ONLINE\client"
        };

        var result = App.SpriteAssignmentService.ExecuteAssignment(req);
        if (!result.Success)
        {
            System.Windows.MessageBox.Show(this,
                "Assignment failed:" + Environment.NewLine + string.Join(Environment.NewLine, result.Errors),
                "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string designation = "Asset files copied.";
        if (entityType == SpriteAssignmentEntityType.Npc && selectedTarget?.Payload is NpcScriptEntry npc)
        {
            designation = App.EntityDesignationService.ApplyNpcSprite(npc, targetKey);
        }
        else if (entityType == SpriteAssignmentEntityType.Monster && selectedTarget?.Payload is MobEntry mob)
        {
            var custom = App.EntityDesignationService.CreateOrUpdateCustomMonsterFrom(mob, targetKey);
            designation = $"Custom monster created: {custom.Id} ({custom.AegisName}) in db/import/mob_db.yml";
        }

        var warningText = result.Warnings.Count > 0
            ? Environment.NewLine + "Warnings:" + Environment.NewLine + string.Join(Environment.NewLine, result.Warnings)
            : "";
        System.Windows.MessageBox.Show(this,
            $"Assignment complete.{Environment.NewLine}" +
            $"Entity: {entityType}{Environment.NewLine}" +
            $"Target key: {targetKey}{Environment.NewLine}" +
            $"Copied files: {result.CopiedFiles.Count}{Environment.NewLine}" +
            $"{designation}{Environment.NewLine}" +
            $"Manifest: {result.ManifestPath}{warningText}",
            "RoDbEditor",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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

    private void ExtractAllRelatedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCategory != "EXTRACTED_ASSETS")
            return;

        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select destination root folder for all related extracted files",
            UseDescriptionForTitle = true,
            SelectedPath = @"F:\MMORPG\RAGNAROK ONLINE\client"
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
}

public class AssetEntry
{
    public string? Path { get; set; }
    public string? DisplayName { get; set; }
}
