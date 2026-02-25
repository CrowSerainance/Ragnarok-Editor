using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
using RoDbEditor.Config;
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

            // Startup health check: warn about loose datainfo files that override GRF
            CheckForLooseDatainfoFiles();
        };
    }

    private void UpdateSourceIndicators()
    {
        if (SourceIndicator1 == null || SourceIndicator2 == null || SourceIndicator3 == null)
            return;

        // GRF and sprite assets: from config only (FILE ASSIGNMENT / GRF paths)
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
            SourceIndicator1.Text = "GRF: (not configured)";

        // Server DB (rAthena) path: from config only; set via File -> Select rAthena folder or FILE ASSIGNMENT
        var dataPath = App.Config?.DataPath;
        if (!string.IsNullOrWhiteSpace(dataPath))
            SourceIndicator2.Text = "Server DB: " + dataPath;
        else
            SourceIndicator2.Text = "Server DB: (not configured)";

        // Items: from configured server YAML or GRF iteminfo, not hardcoded
        var itemSvc = App.ItemDbService;
        if (itemSvc != null && itemSvc.Items.Count > 0)
            SourceIndicator3.Text = itemSvc.IsLoadedFromYaml
                ? $"Items: from server YAML ({itemSvc.Items.Count:N0})"
                : $"Items: from GRF iteminfo ({itemSvc.Items.Count:N0})";
        else
            SourceIndicator3.Text = "Items: (none loaded)";
    }

    private void UpdateListLabel()
    {
        if (CurrentListLabel == null)
        {
            // optionally log or defer the update
            return;
        }
        if (_currentCategory == "FILE_ASSIGNMENT")
            CurrentListLabel.Text = "CURRENT LIST: File Assignment";
        else
            CurrentListLabel.Text = $"CURRENT LIST: {_currentCategory}";
    }

    private void CategoryTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryTabs == null || CategoryTabs.SelectedIndex < 0 || CategoryTabs.SelectedIndex > 5) return;
        var headers = new[] { "ITEMS", "MONSTERS", "NPCs", "MAPS", "QUESTS", "FILE_ASSIGNMENT" };
        _currentCategory = headers[CategoryTabs.SelectedIndex];

        if (FileAssignmentPanel != null)
            FileAssignmentPanel.Visibility = Visibility.Collapsed;

        if (NpcMapFilterPanel != null)
            NpcMapFilterPanel.Visibility = _currentCategory == "NPCs" ? Visibility.Visible : Visibility.Collapsed;

        if (_currentCategory == "FILE_ASSIGNMENT")
        {
            if (CurrentListLabel != null)
                CurrentListLabel.Text = "CURRENT LIST: File Assignment";
            if (FileAssignmentPanel != null)
                FileAssignmentPanel.Visibility = Visibility.Visible;
            RefreshFileAssignProfileCombo();
            PopulateFileAssignmentPaths();
            UpdateListLabel();
            RefreshList();
            return;
        }

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

        private void RefreshListButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshList();
        }

        private void RefreshList()
        {
            if (SearchBox == null || AssetListBox == null)
                return;

            if (_currentCategory == "FILE_ASSIGNMENT")
            {
                AssetListBox.ItemsSource = null;
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
        if (FileAssignmentPanel != null)
            FileAssignmentPanel.Visibility = Visibility.Collapsed;

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

        // No selection: show the correct detail panel for current tab
        NpcDetailsPanel.Visibility = Visibility.Collapsed;
        if (_currentCategory == "FILE_ASSIGNMENT")
        {
            ItemDetailsPanel.Visibility = Visibility.Collapsed;
            MonsterDetailsPanel.Visibility = Visibility.Collapsed;
            if (FileAssignmentPanel != null)
                FileAssignmentPanel.Visibility = Visibility.Visible;
        }
        else if (_currentCategory == "MONSTERS")
        {
            ItemDetailsPanel.Visibility = Visibility.Collapsed;
            MonsterDetailsPanel.Visibility = Visibility.Visible;
            if (FileAssignmentPanel != null)
                FileAssignmentPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            ItemDetailsPanel.Visibility = Visibility.Visible;
            MonsterDetailsPanel.Visibility = Visibility.Collapsed;
            if (FileAssignmentPanel != null)
                FileAssignmentPanel.Visibility = Visibility.Collapsed;
        }
        ClearDetails();
    }

    private void SetPreviewMode(PreviewMode mode)
    {
        if (SpriteViewer == null || CenterPreviewImage == null)
            return;

        SpriteViewer.Stop();
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
            $@"data\texture\���저���터���������\item\{item.Id}.bmp",
            $@"data\texture\���저���터���������\item\{resourceName}.bmp",
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

        var ext = Path.GetExtension(filePath)?.ToLowerInvariant() ?? "";
        if (ext == ".spr" || ext == ".act")
        {
            var actPath = ext == ".act" ? filePath : Path.ChangeExtension(filePath, ".act");
            var sprPath = ext == ".spr" ? filePath : Path.ChangeExtension(filePath, ".spr");
            var (actData, sprData) = App.SpriteLookupService.GetSpriteData(actPath, sprPath);
            if (sprData == null || sprData.Length == 0)
                return false;

            SetPreviewMode(PreviewMode.Sprite);
            SpriteViewer.LoadFromData(actData, sprData);
            SpriteViewer.Play();
            if (!SpriteViewer.LastLoadSucceeded)
                return false;
                        return true;
        }

        if (ext == ".pal")
        {
            var swatch = LoadPalettePreviewFromFile(filePath);
            if (swatch == null)
                return false;
            SetPreviewMode(PreviewMode.Image);
            CenterPreviewImage.Source = swatch;
                        return true;
        }

        var preview = LoadBitmapFromFile(filePath);
        if (preview == null)
            return false;
        SetPreviewMode(PreviewMode.Image);
        CenterPreviewImage.Source = preview;
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

        // Headgear without View ID — sprite won't show in-game
        bool hasHeadLocation = item.Locations != null && item.Locations.Any(kv =>
            kv.Value &&
            (kv.Key.StartsWith("Head_", StringComparison.OrdinalIgnoreCase) ||
             kv.Key.StartsWith("Costume_Head_", StringComparison.OrdinalIgnoreCase)));
        if (hasHeadLocation && (!item.View.HasValue || item.View.Value <= 0))
        {
            warnings.Add("Headgear has Head_ location but no View ID. The equipped sprite will NOT render in-game. "
                + "Set View to a unique number (e.g. 32001) so accessoryid/accname entries are written.");
        }
        if (item.View.HasValue && item.View.Value > 0 && App.ItemDbService != null)
        {
            var clashes = App.ItemDbService.Items
                .Where(i => i.Id != item.Id && i.View.HasValue && i.View.Value == item.View.Value)
                .Take(3)
                .Select(i => $"{i.AegisName}({i.Id})")
                .ToList();
            if (clashes.Count > 0)
            {
                warnings.Add($"View ID {item.View.Value} is already used by {string.Join(", ", clashes)}. "
                    + "Headgear sprite View IDs should be unique to avoid wrong sprite mapping.");
            }
        }

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
        item.View = ParseNullableIntLoose(ItemEditView?.Text);
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
            if (result == null)
            {
                var reason = App.ItemDbService.LastError ?? "Could not resolve save target path (check DataPath in config).";
                System.Windows.MessageBox.Show(this,
                    "Failed to save item to YAML:\n" + reason,
                    "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (result.IsUpdate)
                _operationsLog.RecordUpdated(OperationEntityKind.Item, item.Id, item.AegisName, item.Name ?? "", result.Path, result.BodyIndex, snapshot);
            else
                _operationsLog.RecordAdded(OperationEntityKind.Item, item.Id, item.AegisName, item.Name ?? "", result.Path, result.BodyIndex);
            RefreshOperationsList();
            try { App.ItemInfoLuaWriter?.WriteEntry(item); }
            catch (Exception luaEx)
            {
                System.Windows.MessageBox.Show(this,
                    "Item saved to YAML but failed to update itemInfo_C.lua:\n" + luaEx.Message,
                    "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            try
            {
                if (item.View.HasValue && item.View.Value > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[SaveItem] View>0 for {item.AegisName}, View={item.View}, calling AccessoryIdWriter");
                    if (App.AccessoryIdWriter == null)
                        throw new InvalidOperationException("AccessoryIdWriter is not initialized. Check ClientRootPath in configuration.");
                    App.AccessoryIdWriter.WriteEntry(item);
                    System.Diagnostics.Debug.WriteLine($"[SaveItem] AccessoryIdWriter.WriteEntry completed for {item.AegisName}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[SaveItem] Skipped AccessoryIdWriter: View is empty/<=0 for {item.AegisName}");
                }
                App.ClientAssetWriter?.EnsureItemIcon(item);
                App.ClientAssetWriter?.EnsureCollectionIcon(item);
            }
            catch (Exception assetEx)
            {
                System.Windows.MessageBox.Show(this,
                    $"Item saved to YAML/itemInfo but failed writing client assets:\n\n{assetEx.Message}\n\nIf this was an accessoryid/accname error, re-save after closing GRF Editor or any tool that may lock the GRF file.",
                    "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            System.Windows.MessageBox.Show(this, "Item saved.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, "Error saving item: " + ex.Message, "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
        if (ItemRelatedFilesListBox?.SelectedItem is string itemSel)
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


    private void OpenInGrfEditorButton_Click(object sender, RoutedEventArgs e)
    {
        var cfg = App.Config;
        var grfPath = !string.IsNullOrEmpty(cfg?.TargetGrfPath) ? cfg.TargetGrfPath
            : (string.IsNullOrEmpty(cfg?.ClientRootPath) ? null : Path.Combine(cfg.ClientRootPath, cfg?.TargetGrfFileName ?? "custom.grf"));
        if (string.IsNullOrEmpty(grfPath) || !File.Exists(grfPath))
        {
            var folder = Path.GetDirectoryName(grfPath ?? "");
            if (!string.IsNullOrEmpty(folder)) OpenFolderInExplorer(folder);
            return;
        }
        var editorPath = FindGrfEditorExecutable();
        if (string.IsNullOrEmpty(editorPath))
        {
            System.Windows.MessageBox.Show(this, "GRF Editor not found.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            OpenFolderInExplorer(Path.GetDirectoryName(grfPath) ?? "");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = editorPath, UseShellExecute = true, Arguments = $"\"{grfPath}\"" });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, "Failed to launch GRF Editor: " + ex.Message, "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string? FindGrfEditorExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GRF Editor.exe"),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private void PopulateFileAssignmentPaths()
    {
        var cfg = App.Config;
        FileAssignRathenaPath.Text = !string.IsNullOrEmpty(cfg?.DataPath) ? cfg.DataPath : "(not configured)";
        var clientRoot = cfg?.ClientRootPath ?? "";
        if (FileAssignClientRootPath != null)
            FileAssignClientRootPath.Text = !string.IsNullOrEmpty(clientRoot) ? clientRoot : "(not configured)";
        if (FileAssignPatchRootPath != null)
            FileAssignPatchRootPath.Text = !string.IsNullOrEmpty(cfg?.ClientPatchRoot) ? cfg.ClientPatchRoot : "(not configured)";
        var itemInfoPath = string.IsNullOrEmpty(clientRoot) ? "(not configured)" : Path.Combine(clientRoot, "SystemEN");
        FileAssignItemInfoPath.Text = itemInfoPath;
        var accessoryPath = string.IsNullOrEmpty(clientRoot) ? "(not configured)" : Path.Combine(clientRoot, "data", "luafiles514", "lua files", "datainfo");
        FileAssignAccessoryPath.Text = accessoryPath;
        var spritePath = string.IsNullOrEmpty(clientRoot) ? "(not configured)" : Path.Combine(clientRoot, "data", "sprite");
        if (!string.IsNullOrEmpty(clientRoot) && !Directory.Exists(spritePath))
            spritePath = Path.Combine(clientRoot, "data");
        FileAssignSpritePath.Text = spritePath;
        var grfPath = !string.IsNullOrEmpty(cfg?.TargetGrfPath) ? cfg.TargetGrfPath
            : (!string.IsNullOrEmpty(clientRoot) ? Path.Combine(clientRoot, cfg?.TargetGrfFileName ?? "custom.grf") : null);
        FileAssignGrfPath.Text = !string.IsNullOrEmpty(grfPath) ? grfPath : "(not configured)";
    }

    private void RefreshFileAssignProfileCombo()
    {
        if (FileAssignProfileCombo == null) return;
        var selected = FileAssignProfileCombo.SelectedItem as WorkspaceProfile;
        FileAssignProfileCombo.ItemsSource = null;
        FileAssignProfileCombo.ItemsSource = App.Config.Profiles;
        FileAssignProfileCombo.SelectedItem = selected ?? App.Config.GetActiveProfile();
    }

    private void FileAssignProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Combo selection change does not apply profile; user clicks Apply to switch.
    }

    private void FileAssignNewProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = "Profile " + (App.Config.Profiles.Count + 1);
        var profile = new WorkspaceProfile { Name = name };
        App.Config.Profiles.Add(profile);
        App.Config.ActiveProfileName = name;
        App.Config.ApplyActiveProfileToLegacyFields();
        App.Config.Save();
        RefreshFileAssignProfileCombo();
        PopulateFileAssignmentPaths();
        System.Windows.MessageBox.Show(this, "New profile created. Set paths and click Apply to use it.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void FileAssignSaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = FileAssignProfileCombo?.SelectedItem as WorkspaceProfile;
        if (profile == null)
        {
            System.Windows.MessageBox.Show(this, "Select a profile first.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        profile.DataPath = App.Config.DataPath;
        profile.ClientRootPath = App.Config.ClientRootPath;
        profile.ClientPatchRoot = App.Config.ClientPatchRoot;
        profile.TargetGrfPath = App.Config.TargetGrfPath;
        profile.TargetGrfFileName = App.Config.TargetGrfFileName ?? "custom.grf";
        profile.GrfPaths.Clear();
        foreach (var path in App.Config.GrfPaths)
            profile.GrfPaths.Add(path);
        App.Config.Save();
        System.Windows.MessageBox.Show(this, "Profile saved.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void FileAssignDeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (App.Config.Profiles.Count <= 1)
        {
            System.Windows.MessageBox.Show(this, "Cannot delete the last profile.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var profile = FileAssignProfileCombo?.SelectedItem as WorkspaceProfile;
        if (profile == null) return;
        App.Config.Profiles.Remove(profile);
        if (App.Config.Profiles.Count == 0) return;
        App.Config.ActiveProfileName = App.Config.Profiles[0].Name;
        var next = App.Config.GetActiveProfile();
        if (next != null)
        {
            App.ApplyWorkspaceProfile(next);
            RefreshList();
            UpdateSourceIndicators();
        }
        App.Config.Save();
        RefreshFileAssignProfileCombo();
        PopulateFileAssignmentPaths();
    }

    private void FileAssignApplyProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = FileAssignProfileCombo?.SelectedItem as WorkspaceProfile;
        if (profile == null)
        {
            System.Windows.MessageBox.Show(this, "Select a profile first.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        App.ApplyWorkspaceProfile(profile);
        RefreshList();
        UpdateSourceIndicators();
        PopulateFileAssignmentPaths();
        SetupFileWatcher(App.Config.DataPath ?? "");
        System.Windows.MessageBox.Show(this, "Profile applied.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void FileAssignSetRathena_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select server data folder (contains db/, npc/, system/)",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var path = dlg.SelectedPath;
        if (string.IsNullOrEmpty(path)) return;
        var active = App.Config.GetActiveProfile();
        if (active == null) { App.Config.EnsureDefaultProfileFromLegacyFields(); active = App.Config.GetActiveProfile(); }
        if (active != null) { active.DataPath = path; App.ApplyWorkspaceProfile(active); }
        RefreshList();
        UpdateSourceIndicators();
        PopulateFileAssignmentPaths();
        SetupFileWatcher(path);
    }

    private void FileAssignSetClientRoot_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select Ragnarok Client Root Folder (contains System/)",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var clientRoot = dlg.SelectedPath;
        var active = App.Config.GetActiveProfile();
        if (active == null) { App.Config.EnsureDefaultProfileFromLegacyFields(); active = App.Config.GetActiveProfile(); }
        if (active != null) { active.ClientRootPath = clientRoot; App.ApplyWorkspaceProfile(active); }
        RefreshList();
        UpdateSourceIndicators();
        PopulateFileAssignmentPaths();
    }

    private void FileAssignSetPatchRoot_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select Patch Output Root",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var path = dlg.SelectedPath;
        var active = App.Config.GetActiveProfile();
        if (active != null) { active.ClientPatchRoot = path; App.Config.ApplyActiveProfileToLegacyFields(); }
        App.Config.Save();
        PopulateFileAssignmentPaths();
    }

    private void FileAssignOpenClientRoot_Click(object sender, RoutedEventArgs e)
    {
        var root = App.Config?.ClientRootPath;
        OpenFolderInExplorer(string.IsNullOrEmpty(root) ? "" : root);
    }

    private void FileAssignOpenPatchRoot_Click(object sender, RoutedEventArgs e)
    {
        var root = App.Config?.ClientPatchRoot;
        OpenFolderInExplorer(string.IsNullOrEmpty(root) ? "" : root);
    }

    private static void OpenFolderInExplorer(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath)) return;
        if (Directory.Exists(folderPath))
            Process.Start("explorer.exe", folderPath);
        else
            System.Windows.MessageBox.Show(System.Windows.Application.Current.MainWindow, "Folder does not exist: " + folderPath, "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void FileAssignOpenRathena_Click(object sender, RoutedEventArgs e)
    {
        var dataPath = App.Config?.DataPath;
        if (string.IsNullOrEmpty(dataPath))
        {
            System.Windows.MessageBox.Show(this, "RAthena location is not configured.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        OpenFolderInExplorer(Path.Combine(dataPath, "db", "import"));
    }

    private void FileAssignOpenItemInfo_Click(object sender, RoutedEventArgs e)
    {
        var root = App.Config?.ClientRootPath;
        OpenFolderInExplorer(string.IsNullOrEmpty(root) ? "" : Path.Combine(root, "SystemEN"));
    }

    private void FileAssignOpenAccessory_Click(object sender, RoutedEventArgs e)
    {
        var root = App.Config?.ClientRootPath;
        OpenFolderInExplorer(string.IsNullOrEmpty(root) ? "" : Path.Combine(root, "data", "luafiles514", "lua files", "datainfo"));
    }

    private void FileAssignOpenSprite_Click(object sender, RoutedEventArgs e)
    {
        var root = App.Config?.ClientRootPath;
        if (string.IsNullOrEmpty(root)) { OpenFolderInExplorer(""); return; }
        var sprite = Path.Combine(root, "data", "sprite");
        OpenFolderInExplorer(Directory.Exists(sprite) ? sprite : Path.Combine(root, "data"));
    }

    private void FileAssignOpenGrf_Click(object sender, RoutedEventArgs e)
    {
        var cfg = App.Config;
        var grfPath = !string.IsNullOrEmpty(cfg?.TargetGrfPath) ? cfg.TargetGrfPath
            : (string.IsNullOrEmpty(cfg?.ClientRootPath) ? null : Path.Combine(cfg.ClientRootPath, cfg?.TargetGrfFileName ?? "custom.grf"));
        if (string.IsNullOrEmpty(grfPath))
        {
            System.Windows.MessageBox.Show(this, "Custom GRF path is not configured.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!File.Exists(grfPath))
        {
            OpenFolderInExplorer(Path.GetDirectoryName(grfPath) ?? "");
            return;
        }
        var editorPath = FindGrfEditorExecutable();
        if (string.IsNullOrEmpty(editorPath))
        {
            System.Windows.MessageBox.Show(this, "GRF Editor not found. Open folder instead.", "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
            OpenFolderInExplorer(Path.GetDirectoryName(grfPath) ?? "");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = editorPath, UseShellExecute = true, Arguments = $"\"{grfPath}\"" });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, "Failed to launch GRF Editor: " + ex.Message, "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static bool HasTextureInRelatedPaths(IEnumerable<string>? paths)
    {
        var textureExts = new[] { ".bmp", ".png", ".tga", ".jpg", ".jpeg", ".gif" };
        return paths?.Any(p => textureExts.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)) ?? false;
    }

    /// <summary>
    /// Startup health check: detect loose datainfo files on disk that override GRF content.
    /// Loose files in client\data\ have HIGHEST priority above all GRFs, so stale/broken
    /// fragments can silently break all headgear sprites.
    /// </summary>
    private void CheckForLooseDatainfoFiles()
    {
        var clientRoot = App.Config?.ClientRootPath;
        if (string.IsNullOrWhiteSpace(clientRoot) || !Directory.Exists(clientRoot))
            return;

        var datainfoDir = Path.Combine(clientRoot, "data", "luafiles514", "lua files", "datainfo");
        if (!Directory.Exists(datainfoDir))
            return;

        var dangerousFiles = new[] { "accessoryid.lua", "accessoryid.lub", "accname.lua", "accname.lub" };
        var found = new List<string>();
        foreach (var f in dangerousFiles)
        {
            var fullPath = Path.Combine(datainfoDir, f);
            if (File.Exists(fullPath))
                found.Add(fullPath);
        }

        if (found.Count == 0)
            return;

        var fileList = string.Join("\n", found.Select(f => "  • " + Path.GetFileName(f)));
        var msg = "WARNING: Loose datainfo files detected on disk:\n\n"
            + fileList
            + "\n\nThese override GRF content and can break ALL headgear sprites. "
            + "They were likely left behind by a previous tool or extraction.\n\n"
            + "Delete them now? (RoDbEditor writes directly to custom.grf — loose files are not needed.)";

        var result = System.Windows.MessageBox.Show(this, msg, "RoDbEditor — Loose File Hazard",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            var deleted = 0;
            foreach (var f in found)
            {
                try { File.Delete(f); deleted++; }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(this, $"Could not delete {Path.GetFileName(f)}: {ex.Message}",
                        "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            if (deleted > 0)
                System.Windows.MessageBox.Show(this, $"Deleted {deleted} loose file(s). GRF content will now take effect.",
                    "RoDbEditor", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static bool IsHeadgearWithView(ItemEntry? item)
    {
        if (item == null || !item.View.HasValue || item.View.Value <= 0)
            return false;

        var hasHeadLocation = item.Locations != null && item.Locations.Any(kv =>
            kv.Value &&
            (kv.Key.StartsWith("Head_", StringComparison.OrdinalIgnoreCase) ||
             kv.Key.StartsWith("Costume_Head_", StringComparison.OrdinalIgnoreCase)));

        if (hasHeadLocation)
            return true;

        // Compatibility fallback:
        // many custom headgear entries are saved as Armor with View set,
        // but their equip-location flags may be incomplete at first save.
        return string.Equals(item.Type, "Armor", StringComparison.OrdinalIgnoreCase);
    }

    private static int? ParseNullableIntLoose(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var s = text.Trim();
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;

        // Accept common user input formats: "32,001", "+32001", "32001+"
        s = s.Replace(",", "").Replace("_", "").Trim();
        if (s.StartsWith("+", StringComparison.Ordinal))
            s = s[1..].Trim();
        if (s.EndsWith("+", StringComparison.Ordinal))
            s = s[..^1].Trim();

        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return value;

        return null;
    }

    private void ExtractAllRelatedButton_Click(object sender, RoutedEventArgs e)
    {
        return;
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
        var active = App.Config.GetActiveProfile();
        if (active == null)
        {
            App.Config.EnsureDefaultProfileFromLegacyFields();
            active = App.Config.GetActiveProfile();
        }
        if (active != null)
        {
            active.DataPath = path;
            App.ApplyWorkspaceProfile(active);
        }
        RefreshList();
        UpdateSourceIndicators();
        if (FileAssignmentPanel != null && FileAssignmentPanel.Visibility == Visibility.Visible)
            PopulateFileAssignmentPaths();
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
        var active = App.Config.GetActiveProfile();
        if (active == null)
        {
            App.Config.EnsureDefaultProfileFromLegacyFields();
            active = App.Config.GetActiveProfile();
        }
        if (active != null)
        {
            active.ClientRootPath = clientRoot;
            App.ApplyWorkspaceProfile(active);
        }
        RefreshList();
        UpdateSourceIndicators();
        if (FileAssignmentPanel != null && FileAssignmentPanel.Visibility == Visibility.Visible)
            PopulateFileAssignmentPaths();
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
        var path = dlg.SelectedPath;
        var active = App.Config.GetActiveProfile();
        if (active != null)
        {
            active.ClientPatchRoot = path;
            App.Config.ApplyActiveProfileToLegacyFields();
        }
        App.Config.Save();
        if (FileAssignmentPanel != null && FileAssignmentPanel.Visibility == Visibility.Visible)
            PopulateFileAssignmentPaths();
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
        var normalizedPath = string.IsNullOrEmpty(path) ? "" : Path.GetFullPath(path);
        if (_watcher != null)
        {
            var currentPath = _watcher.Path;
            if (string.Equals(currentPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
                return;
            try { _watcher.Dispose(); } catch { /* ignore */ }
            _watcher = null;
        }
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

    //������������������������������������������������������������������������������������������������������������������������������������
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





    private static string? GetClientRootForAssignment()
    {
        if (!string.IsNullOrEmpty(App.Config?.ClientRootPath) && Directory.Exists(App.Config.ClientRootPath))
            return App.Config.ClientRootPath;
        var firstGrf = App.GrfService?.GrfPaths?.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstGrf) && File.Exists(firstGrf))
            return Path.GetDirectoryName(firstGrf);
        return null;
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
                    if (item?.View.HasValue == true && item.View.Value > 0)
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


}

public class AssetEntry
{
    public string? Path { get; set; }
    public string? DisplayName { get; set; }
}
