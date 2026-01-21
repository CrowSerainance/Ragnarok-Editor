using System.Collections.Generic;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using IOPath = System.IO.Path;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ROMapOverlayEditor.UserControls;
using ROMapOverlayEditor.GrfTown;
using System.Linq; 

namespace ROMapOverlayEditor;

public partial class MainWindow : Window
{
    private enum EditorMode
    {
        Function,   // select
        AddNpc,
        AddWarp,
        AddSpawn,
        Relocate
    }

    private ProjectData _project = new();
    private string? _currentProjectPath;

    // Fields for GrfTown logic
    private GrfTownWorkspace? _grfTown;
    private List<TownEntry> _towns = new();
    private string _towninfoPath = "";

    /// <summary>Original NPCs from Towninfo.lub per map, for diff/changelog.</summary>
    private readonly Dictionary<string, List<TownNpc>> _originalTownNpcs = new();

    private readonly ObservableCollection<Placable> _items = new();

    private EditorMode _mode = EditorMode.Function;

    // Drag-to-move state
    private Placable? _dragTarget;
    private Point _dragStartPixel;
    private bool _isDragStarted;
    private string? _snapshotForDragCancel;

    private bool _isDirty;
    private const int MaxUndo = 50;
    private readonly List<string> _undoStack = new();
    private readonly List<string> _redoStack = new();

    public Placable? SelectedObject 
    {
        get => ObjectsList.SelectedItem as Placable;
        set => ObjectsList.SelectedItem = value;
    }

    public MainWindow()
    {
        InitializeComponent();
        
        // Setup initial project
        _project.PropertyChanged += _project_PropertyChanged;
        
        Loaded += (_, __) =>
        {
            // Only do drawing once WPF has actually created & connected named elements
            RedrawGrid();
            RedrawOverlay();
            
            // Set DataContexts
            ProjectView.DataContext = _project;
            Inspector.DataContext = null;
        };
        ObjectsList.ItemsSource = _items;

        UpdatePropsPanel(null); // Just to clear inspector
        UpdateStatus("Ready");
        UpdateMode(EditorMode.Function);
        UpdateWindowTitle();
        // UpdateUndoRedoButtons(); // Removed button refs for now or need to check if they exist? 
        // I removed Undo/Redo buttons from UI? 
        // Ah, I missed adding Undo/Redo buttons to the new Toolbar.
        // I should probably add them back or rely on Shortcuts.
        // Let's rely on shortcuts for now or add them back if I have space. The layout has 3 bands.
    }

    private void _project_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectData.PixelsPerTile))
        {
            RedrawGrid();
            SetDirty();
        }
        else if (e.PropertyName == nameof(ProjectData.OriginBottomLeft))
        {
            RedrawGrid(); 
            SetDirty();
        }
        else
        {
            SetDirty();
        }
    }

    // --------------------------
    // New Event Handlers
    // --------------------------

    private void Inspector_FocusRequested(object sender, RoutedEventArgs e)
    {
         if (SelectedObject is Placable p)
         {
             var center = TileToPixelCenter(p.X, p.Y);
             // Center the scrollviewer (ZCanvas) on this point?
             // ZCanvas API doesn't expose "CenterOn". 
             // We can just emulate it or leave as TODO.
             // For now, let's just make sure it's visible.
             UpdateStatus($"Focusing on {p.Label}");
         }
    }

    private void Inspector_DeleteRequested(object sender, RoutedEventArgs e)
    {
        if (SelectedObject is Placable toDel)
        {
            PushUndo();
            _project.Items.RemoveAll(x => x.Id == toDel.Id);
            RefreshList();
            UpdatePropsPanel(null);
            RedrawOverlay();
            SetDirty();
            UpdateStatus("Deleted selected");
        }
    }
    
    private void DbView_SpawnRequested(object sender, RoutedEventArgs e)
    {
         var view = sender as DatabaseView;
         // How to get the MobEntry? It is passed via DataContext of the DetailsPanel inside DbView, 
         // but the event args don't carry it. 
         // I should look at DbView.DetailsPanel.DataContext? 
         // Accessing private members of usercontrol is hard.
         // Let's assume DbView exposes SelectedMob.
         // I'll update DbView to expose SelectedMob or pass it in args.
         // For now, let's just create a default Spawn.
         
         UpdateMode(EditorMode.AddSpawn);
         UpdateStatus("Spawn Mode Active: Click to place spawn (Stub Mob ID)");
    }
    
    private void ProjectView_ValidateRequested(object sender, RoutedEventArgs e)
    {
        var errors = new List<string>();
        foreach(var item in _project.Items)
        {
            if (item is WarpPlacable w && (string.IsNullOrEmpty(w.DestMap) || w.DestMap == "prontera"))
                errors.Add($"Warp {w.Label} has default/empty dest");
        }
        
        if (errors.Count == 0) ProjectView.SetValidationMessage("No issues found.");
        else ProjectView.SetValidationMessage($"Found {errors.Count} issues:\n- " + string.Join("\n- ", errors.Take(5)));
    }
    
    private void ProjectView_ExportAllRequested(object sender, RoutedEventArgs e)
    {
        ExportAllToFolder();
    }
    
    // --------------------------
    // Standard Methods (Adapted)
    // --------------------------

    private void SetDirty()
    {
        _isDirty = true;
        UpdateWindowTitle();
    }

    private void ClearDirty()
    {
        _isDirty = false;
        UpdateWindowTitle();
    }

    private void UpdateWindowTitle()
    {
        Title = "RO Map Overlay Editor (MVP)" + (_isDirty ? " *" : "");
    }

    private bool TrySave()
    {
        if (_currentProjectPath == null)
        {
            if (!string.IsNullOrEmpty(_project.BaseFolderPath) && Directory.Exists(_project.BaseFolderPath))
            {
                _currentProjectPath = IOPath.Combine(_project.BaseFolderPath, "project.romap.json");
            }
            else
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "ROMapOverlayEditor Project (*.romap.json)|*.romap.json|JSON (*.json)|*.json",
                    FileName = "project.romap.json"
                };
                if (dlg.ShowDialog() != true) return false;
                _currentProjectPath = dlg.FileName;
                _project.BaseFolderPath = IOPath.GetDirectoryName(_currentProjectPath);
            }
        }
        try
        {
            var dir = IOPath.GetDirectoryName(_currentProjectPath);
            if (!string.IsNullOrEmpty(dir)) EnsureFolderStructure(dir);

            ProjectIO.Save(_currentProjectPath, _project);
            if (string.IsNullOrEmpty(_project.BaseFolderPath))
                _project.BaseFolderPath = IOPath.GetDirectoryName(_currentProjectPath);
            ClearDirty();
            UpdateStatus($"Saved: {IOPath.GetFileName(_currentProjectPath)}");
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save:\n{ex.Message}", "Error");
            return false;
        }
    }

    /// <summary>Creates scripts/, db/, imports/ under the project folder if missing.</summary>
    private static void EnsureFolderStructure(string basePath)
    {
        if (string.IsNullOrEmpty(basePath)) return;
        foreach (var sub in new[] { "scripts", "db", "imports" })
        {
            var p = IOPath.Combine(basePath, sub);
            if (!Directory.Exists(p)) Directory.CreateDirectory(p);
        }
    }

    private bool PromptSaveIfDirty()
    {
        if (!_isDirty) return true;
        var r = MessageBox.Show("Save changes to the project?", "RO Map Overlay Editor", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.Yes) return TrySave();
        return true; 
    }

    private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isDirty) return;
        var r = MessageBox.Show("Save changes to the project?", "RO Map Overlay Editor", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
        if (r == MessageBoxResult.Yes && !TrySave()) { e.Cancel = true; return; }
    }
    
    // Undo/Redo
    private void PushUndo()
    {
        _redoStack.Clear();
        _undoStack.Insert(0, ProjectIO.ToJson(_project));
        if (_undoStack.Count > MaxUndo) _undoStack.RemoveAt(_undoStack.Count - 1);
    }

    private void PushUndoSnapshot(string json)
    {
        _redoStack.Clear();
        _undoStack.Insert(0, json);
        if (_undoStack.Count > MaxUndo) _undoStack.RemoveAt(_undoStack.Count - 1);
    }

    private void ApplyProjectData(ProjectData d, Guid? selectId = null, bool markDirty = true)
    {
        // Unsubscribe old
        _project.PropertyChanged -= _project_PropertyChanged;
        
        _project = d;
        _project.PropertyChanged += _project_PropertyChanged;
        
        ProjectView.DataContext = _project; // Rebind
        
        LoadBackgroundImageIfAny();
        if (!string.IsNullOrEmpty(_project.GrfFilePath))
            InitializeTownDropdownFromGrf(_project.GrfFilePath);
        RedrawGrid();
        RefreshList(selectId);
        RedrawOverlay();
        UpdatePropsPanel(ObjectsList.SelectedItem as Placable);
        if (markDirty) SetDirty();
    }
    
    // (Undo/Redo button updates removed as UI elements are gone, logic remains in memory)

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undoStack.Count == 0) return;
        var j = _undoStack[0];
        _undoStack.RemoveAt(0);
        _redoStack.Insert(0, ProjectIO.ToJson(_project));
        if (_redoStack.Count > MaxUndo) _redoStack.RemoveAt(_redoStack.Count - 1);
        ApplyProjectData(ProjectIO.FromJson(j));
        UpdateStatus("Undo");
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (_redoStack.Count == 0) return;
        var j = _redoStack[0];
        _redoStack.RemoveAt(0);
        _undoStack.Insert(0, ProjectIO.ToJson(_project));
        if (_undoStack.Count > MaxUndo) _undoStack.RemoveAt(_undoStack.Count - 1);
        ApplyProjectData(ProjectIO.FromJson(j));
        UpdateStatus("Redo");
    }

    // Modes
    private void UpdateMode(EditorMode mode)
    {
        _mode = mode;
        UpdateStatus($"Mode: {_mode}");
        
        // Sync Buttons
        if (ModeSelect != null) ModeSelect.IsChecked = (mode == EditorMode.Function || mode == EditorMode.Relocate);
        if (ModeNpc != null) ModeNpc.IsChecked = (mode == EditorMode.AddNpc);
        if (ModeWarp != null) ModeWarp.IsChecked = (mode == EditorMode.AddWarp);
        if (ModeSpawn != null) ModeSpawn.IsChecked = (mode == EditorMode.AddSpawn);
    }

    private void Function_Click(object sender, RoutedEventArgs e) => UpdateMode(EditorMode.Function);
    private void Relocate_Click(object sender, RoutedEventArgs e) => UpdateMode(EditorMode.Relocate); // Using Relocate for Select/Move
    private void AddNpcMode_Click(object sender, RoutedEventArgs e) => UpdateMode(EditorMode.AddNpc);
    private void AddWarpMode_Click(object sender, RoutedEventArgs e) => UpdateMode(EditorMode.AddWarp);
    private void AddSpawnMode_Click(object sender, RoutedEventArgs e) => UpdateMode(EditorMode.AddSpawn);

    // Project Actions
    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptSaveIfDirty()) return;

        _undoStack.Clear();
        _redoStack.Clear();

        ApplyProjectData(new ProjectData(), markDirty: false);
        _currentProjectPath = null;

        // Ensure UI reflects a truly “blank” project
        ObjectsList.SelectedItem = null;
        Inspector.DataContext = null;

        ClearDirty();
        UpdateMode(EditorMode.Function);
        UpdateStatus("New project");
        UpdateWindowTitle();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => TrySave();

    private void OpenGrf_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new OpenFileDialog
            {
                Title = "Open GRF",
                Filter = "Ragnarok Archives (*.grf;*.gpf)|*.grf;*.gpf|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dlg.ShowDialog() != true) return;

            var path = dlg.FileName;

            // 1) Validate quickly so we can give a better message than “invalid file table offset”
            var validation = Grf.GrfArchive.TryValidate(path);
            if (!validation.Ok)
            {
                MessageBox.Show(validation.UserMessage, "Failed to open GRF", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _project.GrfFilePath = path;
            _project.GrfInternalPath = null;
            _project.BackgroundImagePath = null;
            _originalTownNpcs.Clear();

            InitializeTownDropdownFromGrf(path);

            if (_towns.Count == 0 &&
                MessageBox.Show("Town data not found in GRF. Select a folder that contains Towninfo.lua or Towninfo.lub?", "Lua data", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                if (TryPickAndValidateLuaFolder(out var folder))
                {
                    _project.LuaDataFolderPath = folder;
                    InitializeTownDropdownFromGrf(path);
                }
            }

            LoadBackgroundImageIfAny();
            RedrawGrid();
            RedrawOverlay();
            UpdateStatus(_towns.Count > 0 ? $"Opened GRF: {IOPath.GetFileName(path)} — select a town" : $"Opened GRF: {IOPath.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open GRF.\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetLuaFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!TryPickAndValidateLuaFolder(out var folder)) return;
        _project.LuaDataFolderPath = folder;
        SetDirty();
        if (!string.IsNullOrEmpty(_project.GrfFilePath))
        {
            InitializeTownDropdownFromGrf(_project.GrfFilePath);
            LoadBackgroundImageIfAny();
            RedrawGrid();
            RedrawOverlay();
        }
        else
            MessageBox.Show("Lua folder set. Open a GRF to load towns.", "Lua Folder", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static bool TryPickAndValidateLuaFolder(out string? folderPath)
    {
        folderPath = null;
        var dlg = new OpenFolderDialog
        {
            Title = "Select folder containing Towninfo.lua or Towninfo.lub (e.g. data\\System or extracted client data)"
        };
        if (dlg.ShowDialog() != true) return false;
        var root = dlg.FolderName;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return false;
        var found = GrfTownWorkspace.FolderTowninfoCandidates.Any(rel => File.Exists(IOPath.Combine(root, rel)));
        if (!found)
        {
            MessageBox.Show("No Towninfo.lua or Towninfo.lub found in this folder.", "Lua Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        folderPath = root;
        return true;
    }

    // OnGrfOpened removed: Open GRF now only picks file and populates Town dropdown (no browser).
    // (Initialize GrfTown Integration
        // Note: GrfArchive.Open returns an object that we used in browser but we also need one for the workspace if we want to keep it open.
        // Actually, GrfBrowserWindow loads the archive. To properly reuse it, we should pass the reader functions.
        // However, OnGrfOpened only receives the GrfArchive for browser.
        // We need to re-open or keep the archive open?
        // GrfArchive is IDisposable. The one passed to OnGrfOpened is disposed by the caller?
        // Let's check OpenGrf_Click.
        // OpenGrf_Click: using var grf = Grf.GrfArchive.Open(path); OnGrfOpened(grf);
        // This means 'grf' is disposed after OnGrfOpened returns.
        
        // We need the GrfTownWorkspace to persist. So we need a NEW GrfArchive instance or we must change OpenGrf_Click to keep it alive.
        // Minimal friction change: Re-open for the workspace, OR just store the path and open on demand?
        // GrfTownWorkspace takes delegates. Let's create a new lightweight reader manager?
        // Actually, GrfArchive.Open locks file. Sharing read access is safer. 
        // Let's just store the path and open a fresh reader for the workspace?
        // But we want to iterate files. GrfArchive loads table in memory. It's fast.
        
    private void InitializeTownDropdownFromGrf(string grfFilePath)
    {
        // Define simple wrappers for GrfTownWorkspace to use our existing GrfArchive
        // PROBLEM: We don't want to keep a file stream open forever if we can avoid it.
        // GrfTownWorkspace does bulk load once, then load image on demand.
        // On demand reading needs an open handle or re-opening.
        // Let's re-open on demand.
        
        _grfTown = new GrfTownWorkspace(
            IOPath.GetFileName(grfFilePath),
            listPaths: () => 
            {
                using var grf = Grf.GrfArchive.Open(grfFilePath);
                return grf.Entries.Select(x => x.Path).ToList();
            },
            existsInGrf: (p) => 
            {
                using var grf = Grf.GrfArchive.Open(grfFilePath);
                return grf.Entries.Any(x => x.Path.Equals(p, StringComparison.OrdinalIgnoreCase));
            },
            readBytesFromGrf: (p) => 
            {
                using var grf = Grf.GrfArchive.Open(grfFilePath);
                return grf.Extract(p);
            },
            luaDataFolderPath: _project.LuaDataFolderPath
        );

        var (towns, sourcePath, warning) = _grfTown.LoadTownList();
        _towns = towns;
        _towninfoPath = sourcePath;

        TownCombo.ItemsSource = _towns;
        TownCombo.IsEnabled = _towns.Count > 0;

        CopyExportBtn.IsEnabled = false;

        if (!string.IsNullOrWhiteSpace(warning))
        {
            MessageBox.Show(warning, "Town list", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        if (_towns.Count > 0)
        {
            var preferred = _project.LastSelectedTown ?? _project.MapName;
            var existing = _towns.FirstOrDefault(t => t.Name.Equals(preferred, StringComparison.OrdinalIgnoreCase));
            if (existing != null) TownCombo.SelectedItem = existing;
            else TownCombo.SelectedIndex = 0;
        }
    }

    private void TownCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_grfTown == null) return;
        if (TownCombo.SelectedItem is not TownEntry selected) return;

        // Load town NPC list (already parsed) + attempt image load
        var res = _grfTown.LoadTown(selected.Name, _towninfoPath, _towns);
        if (!res.Ok || res.Town == null)
        {
            MessageBox.Show(res.Message, "Town load", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        LoadTownNpcsIntoOverlay(res.Town);
        _project.LastSelectedTown = selected.Name;

        // 2) Load map image from the “img path” stored in SourcePath after the pipe
        var imgPath = ParseImagePathFromTownSource(res.Town.SourcePath);
        if (!string.IsNullOrWhiteSpace(imgPath))
        {
            TryLoadGrfMapImage(imgPath);
        }

        CopyExportBtn.IsEnabled = true;
        UpdateStatus($"Loaded town: {res.Town.Name}");
    }

    private void CopyExport_Click(object sender, RoutedEventArgs e)
    {
        if (TownCombo.SelectedItem is not TownEntry town) return;
        if (_grfTown == null || string.IsNullOrEmpty(_project.GrfFilePath)) return;

        var currentNpcs = _project.Items.OfType<NpcPlacable>()
            .Where(n => string.Equals(n.MapName, town.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var originalNpcs = _originalTownNpcs.GetValueOrDefault(town.Name);

        var md = ExportBundleBuilder.BuildMarkdownBundle(town, _project.GrfFilePath, _towninfoPath, currentNpcs, originalNpcs);
        Clipboard.SetText(md);
        UpdateStatus($"Copied export for: {town.Name}");
    }

    private static string ParseImagePathFromTownSource(string sourcePath)
    {
        // sourcePath may be "towninfo | imgpath"
        var parts = sourcePath.Split('|').Select(x => x.Trim()).ToArray();
        if (parts.Length >= 2) return parts[1];
        return "";
    }

    private void TryLoadGrfMapImage(string internalPath)
    {
        if (_grfTown == null) return;

        try
        {
            var bytes = _grfTown.Read(internalPath);
            using var ms = new MemoryStream(bytes);

            // Re-use our existing load helper
            var bmp = LoadBitmapWithTransparency(ms);
            
            if (BgImage != null) { BgImage.Source = bmp; BgImage.Width = bmp.PixelWidth; BgImage.Height = bmp.PixelHeight; }
            if (GridLayer != null) { GridLayer.Width = bmp.PixelWidth; GridLayer.Height = bmp.PixelHeight; }
            if (OverlayLayer != null) { OverlayLayer.Width = bmp.PixelWidth; OverlayLayer.Height = bmp.PixelHeight; }

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                if (ZCanvas != null && bmp != null) 
                { 
                    var viewport = new Size(ZCanvas.ActualWidth, ZCanvas.ActualHeight); 
                    var content = new Size(bmp.PixelWidth, bmp.PixelHeight); 
                    ZCanvas.FitToView(content, viewport); 
                }
            }));
            
            // Allow overlay logic to handle map name changes?
            // Since we changed the image, we should probably update project paths if we want to "save" this state as a new map.
            _project.GrfInternalPath = internalPath;
            // Map Name is updated in LoadTownNpcsIntoOverlay
            
            RedrawOverlay();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to load map image from GRF:\n{internalPath}\n\n{ex.Message}",
                "Map image",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void LoadTownNpcsIntoOverlay(TownEntry town)
    {
        PushUndo();
        _project.MapName = town.Name;
        
        // Remove old NPCs? User request implied "selecting town auto-loads... NPC list for that town".
        // This implies replacing current view with that town.
        // So we clear old items.
        
        _project.Items.Clear();
        _originalTownNpcs[town.Name] = town.Npcs.Select(n => new TownNpc { Name = n.Name, X = n.X, Y = n.Y, Type = n.Type, Sprite = n.Sprite }).ToList();

        foreach (var n in town.Npcs)
        {
             _project.Items.Add(new NpcPlacable 
             { 
                 MapName = town.Name, 
                 X = n.X, 
                 Y = n.Y, 
                 Label = n.Name, 
                 ScriptName = n.Name, 
                 Sprite = string.IsNullOrWhiteSpace(n.Sprite) ? "4_M_MERCHANT" : n.Sprite,
                 ScriptBody = TowninfoImporter.GetDefaultScriptBody(n.Name, n.Type)
             });
        }
        
        RefreshList();
        RedrawOverlay();
        SetDirty();
        UpdateWindowTitle();
    }





    private void LoadImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        PushUndo();
        _project.BackgroundImagePath = dlg.FileName;
        _project.GrfFilePath = null;
        _project.GrfInternalPath = null;
        LoadBackgroundImageIfAny();
        RedrawGrid();
        SetDirty();
        UpdateStatus("Loaded background image");
    }

    private void LoadBackgroundImageIfAny()
    {
        // Clear overlay layers (markers + grid drawings)
        if (OverlayLayer != null) OverlayLayer.Children.Clear();
        if (GridLayer != null) GridLayer.Children.Clear();

        // Default: fully clear background image and collapse the drawing extents
        if (BgImage != null)
        {
            BgImage.Source = null;
            BgImage.Width = 0;
            BgImage.Height = 0;
        }
        if (GridLayer != null)
        {
            GridLayer.Width = 0;
            GridLayer.Height = 0;
        }
        if (OverlayLayer != null)
        {
            OverlayLayer.Width = 0;
            OverlayLayer.Height = 0;
        }

        // 1) GRF source
        if (!string.IsNullOrEmpty(_project.GrfFilePath) && !string.IsNullOrEmpty(_project.GrfInternalPath) && File.Exists(_project.GrfFilePath))
        {
            try
            {
                using var grf = Grf.GrfArchive.Open(_project.GrfFilePath);
                byte[] bytes = grf.Extract(_project.GrfInternalPath);
                using var ms = new MemoryStream(bytes);
                // Helper to load BMP and treat Magenta (255,0,255) as transparent
                var bmp = LoadBitmapWithTransparency(ms);
                
                if (BgImage != null) { BgImage.Source = bmp; BgImage.Width = bmp.PixelWidth; BgImage.Height = bmp.PixelHeight; }
                if (GridLayer != null) { GridLayer.Width = bmp.PixelWidth; GridLayer.Height = bmp.PixelHeight; }
                if (OverlayLayer != null) { OverlayLayer.Width = bmp.PixelWidth; OverlayLayer.Height = bmp.PixelHeight; }
                
                // Ensure Layout is updated before fitting
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    if (ZCanvas != null) 
                    { 
                        var viewport = new Size(ZCanvas.ActualWidth, ZCanvas.ActualHeight); 
                        var content = new Size(bmp.PixelWidth, bmp.PixelHeight); 
                        if (viewport.Width > 0 && viewport.Height > 0)
                            ZCanvas.FitToView(content, viewport); 
                    }
                }));

                RedrawOverlay();
                return;
            }
            catch (Exception ex)
            {
                UpdateStatus("Failed to load map from GRF; cleared.");
                MessageBox.Show($"Failed to load from GRF:\n{ex.Message}", "Error");
                if (ZCanvas != null) ZCanvas.ResetView();
                RedrawOverlay();
                return;
            }
        }

        // 2) File-based
        var path = _project.BackgroundImagePath;

        if (string.IsNullOrWhiteSpace(path))
        {
            // No background image set => blank canvas is expected for a new project
            if (ZCanvas != null) ZCanvas.ResetView();
            return;
        }

        if (!File.Exists(path))
        {
            // Path is set but file missing => keep blank view but inform user
            UpdateStatus("Background image missing; cleared canvas.");
            if (ZCanvas != null) ZCanvas.ResetView();
            return;
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();

            if (BgImage != null)
            {
                BgImage.Source = bmp;
                BgImage.Width = bmp.PixelWidth;
                BgImage.Height = bmp.PixelHeight;
            }

            if (GridLayer != null)
            {
                GridLayer.Width = bmp.PixelWidth;
                GridLayer.Height = bmp.PixelHeight;
            }

            if (OverlayLayer != null)
            {
                OverlayLayer.Width = bmp.PixelWidth;
                OverlayLayer.Height = bmp.PixelHeight;
            }

            if (ZCanvas != null)
            {
                var viewport = new Size(ZCanvas.ActualWidth, ZCanvas.ActualHeight);
                var content = new Size(bmp.PixelWidth, bmp.PixelHeight);
                ZCanvas.FitToView(content, viewport);
            }
        }
        catch (Exception ex)
        {
            if (BgImage != null)
            {
                BgImage.Source = null;
                BgImage.Width = 0;
                BgImage.Height = 0;
            }
            if (GridLayer != null)
            {
                GridLayer.Width = 0;
                GridLayer.Height = 0;
            }
            if (OverlayLayer != null)
            {
                OverlayLayer.Width = 0;
                OverlayLayer.Height = 0;
            }

            UpdateStatus("Failed to load background image; cleared canvas.");
            MessageBox.Show($"Failed to load image:\n{ex.Message}", "Error");
            if (ZCanvas != null) ZCanvas.ResetView();
        }
        
        RedrawOverlay();
    }

    private void Fit_Click(object sender, RoutedEventArgs e)
    {
        if (BgImage.Source is BitmapSource bmp)
        {
            var viewport = new Size(ZCanvas.ActualWidth, ZCanvas.ActualHeight);
            var content = new Size(bmp.PixelWidth, bmp.PixelHeight);
            ZCanvas.FitToView(content, viewport);
            UpdateStatus("Fit to view");
        }
    }

    private void ResetView_Click(object sender, RoutedEventArgs e)
    {
        ZCanvas.ResetView();
        UpdateStatus("View reset");
    }

    private void GridToggle_Changed(object sender, RoutedEventArgs e) => RedrawGrid();
    private void LabelsToggle_Changed(object sender, RoutedEventArgs e) => RedrawOverlay();

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_dragTarget != null && _isDragStarted && _snapshotForDragCancel != null)
            {
                ApplyProjectData(ProjectIO.FromJson(_snapshotForDragCancel), _dragTarget.Id, markDirty: false);
                _snapshotForDragCancel = null;
                ZCanvas.ReleaseMouseCapture();
                _dragTarget = null;
                _isDragStarted = false;
                UpdateStatus("Drag cancelled");
            }
            else if (_mode != EditorMode.Function)
                UpdateMode(EditorMode.Function);
            e.Handled = true;
            return;
        }
        
        // F-Keys for Mode
        if (e.Key == Key.F1) { Relocate_Click(sender, e); e.Handled = true; return; }
        if (e.Key == Key.F2) { AddNpcMode_Click(sender, e); e.Handled = true; return; }
        if (e.Key == Key.F3) { AddWarpMode_Click(sender, e); e.Handled = true; return; }
        if (e.Key == Key.F4) { AddSpawnMode_Click(sender, e); e.Handled = true; return; }
        // Focus
        if (e.Key == Key.F) { Inspector_FocusRequested(sender, e); e.Handled = true; return; }

        if (Keyboard.FocusedElement is TextBox) return;

        if (e.Key == Key.Delete && ObjectsList.SelectedItem is Placable)
        {
            Inspector_DeleteRequested(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { Undo_Click(sender, e); e.Handled = true; return; }
        if (e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { Redo_Click(sender, e); e.Handled = true; return; }

        // Nudge
        if (ObjectsList.SelectedItem is not Placable p) return;
        int step = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? 10 : (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 5 : 1;
        int dx = 0, dy = 0;
        if (e.Key == Key.Left) dx = -step;
        else if (e.Key == Key.Right) dx = step;
        else if (e.Key == Key.Up) dy = step;
        else if (e.Key == Key.Down) dy = -step;

        if (dx != 0 || dy != 0)
        {
            // Note: with TwoWay binding, changing X/Y triggers PropertyChanged which can trigger RedrawOverlay if we subscribed to item changes
            // But we currently only subscribe to _project.PropertyChanged (ProjectData), NOT items.
            // Items are INotifyPropertyChanged now. We should ideally subscribe. 
            // For now, explicit update:
            PushUndo();
            p.X += dx;
            p.Y += dy;
            // Redraw happens via explicit call or binding?
            // Since marker position is manually drawn in RedrawOverlay(), we must call it.
            RedrawOverlay(); 
            SetDirty();
            UpdateStatus($"Nudged {p.Kind} to {p.X},{p.Y}");
            e.Handled = true;
        }
    }

    private void RedrawGrid()
    {
        if (GridLayer == null || GridToggle == null || BgImage == null) return;
        GridLayer.Children.Clear();
        if (GridToggle.IsChecked != true) return;
        if (BgImage.Source is not BitmapSource bmp) return;
        var ppt = _project.PixelsPerTile;
        if (ppt <= 0) return;
        GridLayer.Width = bmp.PixelWidth;
        GridLayer.Height = bmp.PixelHeight;

        for (double x = 0; x <= bmp.PixelWidth; x += ppt)
        {
            int ti = (int)Math.Round(x / ppt);
            bool major = (ti % 10) == 0;
            var line = new Line
            {
                X1 = x, Y1 = 0, X2 = x, Y2 = bmp.PixelHeight,
                Stroke = new SolidColorBrush(Color.FromArgb((byte)(major ? 80 : 40), 255, 255, 255)),
                StrokeThickness = major ? 2 : 1
            };
            GridLayer.Children.Add(line);
        }
        for (double y = 0; y <= bmp.PixelHeight; y += ppt)
        {
            int ti = (int)Math.Round(y / ppt);
            bool major = (ti % 10) == 0;
            var line = new Line
            {
                X1 = 0, Y1 = y, X2 = bmp.PixelWidth, Y2 = y,
                Stroke = new SolidColorBrush(Color.FromArgb((byte)(major ? 80 : 40), 255, 255, 255)),
                StrokeThickness = major ? 2 : 1
            };
            GridLayer.Children.Add(line);
        }
    }

    private (int tileX, int tileY, Point pixelPoint) MouseToTile(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(BgImage);
        var (tx, ty) = PixelToTile(p);
        return (tx, ty, p);
    }

    private (int tx, int ty) PixelToTile(Point p)
    {
        var ppt = _project.PixelsPerTile;
        if (ppt <= 0) return (0, 0);
        int tx = (int)Math.Floor(p.X / ppt);
        int tyTop = (int)Math.Floor(p.Y / ppt);
        if (_project.OriginBottomLeft && BgImage.Source is BitmapSource bmp)
        {
            int tilesHigh = (int)Math.Ceiling(bmp.PixelHeight / ppt);
            int ty = (tilesHigh - 1) - tyTop;
            return (tx, ty);
        }
        return (tx, tyTop);
    }

    private Point TileToPixelCenter(int x, int y)
    {
        var ppt = _project.PixelsPerTile;
        if (_project.OriginBottomLeft && BgImage.Source is BitmapSource bmp)
        {
            int tilesHigh = (int)Math.Ceiling(bmp.PixelHeight / ppt);
            int tyTop = (tilesHigh - 1) - y;
            return new Point(x * ppt + ppt / 2.0, tyTop * ppt + ppt / 2.0);
        }
        return new Point(x * ppt + ppt / 2.0, y * ppt + ppt / 2.0);
    }

    private void ZCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (BgImage.Source is null) { UpdateStatus("Load an image first."); return; }
        
        var (tx, ty, pix) = MouseToTile(e);
        UpdateStatus($"Mouse tile: {tx},{ty}");

        if (_mode == EditorMode.AddNpc)
        {
            PushUndo();
            var npc = new NpcPlacable { MapName = _project.MapName, X = tx, Y = ty, Label = "npc", ScriptName = "MyNpc" };
            AddAndSelect(npc);
            return;
        }
        if (_mode == EditorMode.AddWarp)
        {
             PushUndo();
             var warp = new WarpPlacable { MapName = _project.MapName, X = tx, Y = ty, Label = "warp", WarpName = $"warp_{_project.Items.Count}" };
             AddAndSelect(warp);
             return;
        }
        if (_mode == EditorMode.AddSpawn)
        {
             PushUndo();
             var spawn = new SpawnPlacable { MapName = _project.MapName, X = tx, Y = ty, Label = "Spawn" };
             AddAndSelect(spawn);
             return;
        }

        if (_mode == EditorMode.Relocate)
        {
             if (ObjectsList.SelectedItem is Placable sel)
             {
                 PushUndo();
                 sel.X = tx; sel.Y = ty;
                 RefreshList(sel.Id);
                 RedrawOverlay();
                 SetDirty();
             }
             return;
        }

        // Pointer/Select Mode
        var hit = HitTestOverlay(pix, 14);
        if (hit != null)
        {
            ObjectsList.SelectedItem = hit;
            _dragTarget = hit;
            _dragStartPixel = pix;
            _isDragStarted = false;
            ZCanvas.CaptureMouse();
        }
        else
        {
            ObjectsList.SelectedItem = null;
        }
        UpdatePropsPanel(hit);
        RedrawOverlay();
    }
    
    private void AddAndSelect(Placable p)
    {
        _project.Items.Add(p);
        RefreshList(p.Id);
        RedrawOverlay();
        SetDirty();
        // Switch back to Select mode after add? Or keep adding? Using keep adding behavior.
    }

    private void ZCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragTarget == null) return;
        var pix = e.GetPosition(BgImage);
        if (!_isDragStarted)
        {
            if ((pix - _dragStartPixel).Length <= 3) return;
            _isDragStarted = true;
            _snapshotForDragCancel = ProjectIO.ToJson(_project);
        }
        var (tx, ty) = PixelToTile(pix);
        if (_dragTarget.X != tx || _dragTarget.Y != ty)
        {
            _dragTarget.X = tx;
            _dragTarget.Y = ty;
            // UpdatePropsPanel not needed if bound? Inspector binds to object props so it should auto update.
            // But stats grid in Inspector shows X,Y.
            RedrawOverlay();
        }
    }

    private void ZCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragTarget != null)
        {
            if (_isDragStarted && _snapshotForDragCancel != null)
            {
                PushUndoSnapshot(_snapshotForDragCancel);
                SetDirty();
                _snapshotForDragCancel = null;
            }
            ZCanvas.ReleaseMouseCapture();
            _dragTarget = null;
            _isDragStarted = false;
        }
    }

    private Placable? HitTestOverlay(Point pixelPointOnImage, double maxPixelDistance)
    {
        Placable? best = null;
        double bestDist = double.MaxValue;
        foreach (var item in _project.Items)
        {
            var c = TileToPixelCenter(item.X, item.Y);
            var dist = (c - pixelPointOnImage).Length;
            if (dist <= maxPixelDistance && dist < bestDist)
            {
                bestDist = dist;
                best = item;
            }
        }
        return best;
    }

    private void RedrawOverlay()
    {
        if (OverlayLayer == null) return;
        OverlayLayer.Children.Clear();
        var selectedId = (ObjectsList.SelectedItem as Placable)?.Id;
        foreach (var item in _project.Items)
        {
            var p = TileToPixelCenter(item.X, item.Y);
            var isSelected = selectedId.HasValue && item.Id == selectedId.Value;
            Shape marker = item.Kind switch
            {
                PlacableKind.Warp => new Rectangle { Width = 12, Height = 12 },
                PlacableKind.Spawn => new Ellipse { Width = 10, Height = 10, StrokeDashArray=new DoubleCollection{2,1} },
                _ => new Ellipse { Width = 12, Height = 12 }
            };
            marker.Fill = isSelected ? Brushes.Yellow : (item.Kind == PlacableKind.Warp ? Brushes.DeepSkyBlue : (item.Kind == PlacableKind.Spawn ? Brushes.MediumPurple : Brushes.LimeGreen));
            marker.Stroke = Brushes.Black;
            marker.StrokeThickness = 1;
            Canvas.SetLeft(marker, p.X - marker.Width/2);
            Canvas.SetTop(marker, p.Y - marker.Height/2);
            OverlayLayer.Children.Add(marker);
        }
    }

    private void ObjectsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var sel = ObjectsList.SelectedItem as Placable;
        UpdatePropsPanel(sel);
        RedrawOverlay();
    }
    
    private void UpdatePropsPanel(Placable? p)
    {
        // Now mostly handled by Binding, but we need to set DataContext of Inspector
        if (Inspector != null) Inspector.DataContext = p;
        
        // Also ensure ProjectView knows selection? No.
        
        // Switch tab to Inspector if object selected?
        if (p != null && RightTabs != null)
        {
            // Only switch if we are not already on Inspector?
            // User might be filtering DB. Better not auto-switch aggressively.
            // But Phase 1 item 1 says: "INSPECTOR (selected object details)".
            // Let's safe switch if we are in Project tab (where the list is).
            // Actually, if I click in list, I probably want to see details.
            RightTabs.SelectedIndex = 0; // Inspector
        }
    }

    private void RefreshList(Guid? selectId = null)
    {
        _items.Clear();
        foreach (var x in _project.Items) _items.Add(x);
        if (selectId.HasValue)
        {
            var f = _items.FirstOrDefault(x => x.Id == selectId.Value);
            if (f != null) ObjectsList.SelectedItem = f;
        }
    }

    private void UpdateStatus(string msg) { if (StatusText != null) StatusText.Text = msg; }
    
    private string Sanitize(string s) => string.Join("_", s.Split(IOPath.GetInvalidFileNameChars()));
    
    private void ImportNpcs_Click(object sender, RoutedEventArgs e) 
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Text (*.txt)|*.txt|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var text = File.ReadAllText(dlg.FileName);
            var imported = RAthenaImporter.ImportNpcs(text, _project.MapName);
            PushUndo();
            foreach (var n in imported)
                _project.Items.Add(n);
            RefreshList();
            RedrawOverlay();
            SetDirty();
            UpdateStatus($"Imported NPCs: {imported.Count} ({IOPath.GetFileName(dlg.FileName)})");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Import failed:\n{ex.Message}", "Error");
        }
    }
    
    private void ImportWarps_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Text (*.txt)|*.txt|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var text = File.ReadAllText(dlg.FileName);
            var imported = RAthenaImporter.ImportWarps(text, _project.MapName);
            PushUndo();
            foreach (var w in imported)
                _project.Items.Add(w);
            RefreshList();
            RedrawOverlay();
            SetDirty();
            UpdateStatus($"Imported warps: {imported.Count} ({IOPath.GetFileName(dlg.FileName)})");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Import failed:\n{ex.Message}", "Error");
        }
    }
    
    private void ExportNpcs_Click(object sender, RoutedEventArgs e)
    {
        var scriptsDir = ResolveScriptsDir();
        var dlg = new SaveFileDialog
        {
            Filter = "Text (*.txt)|*.txt",
            FileName = "npcs_custom.txt",
            InitialDirectory = scriptsDir
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var txt = RAthenaExporter.ExportNpcs(_project);
            File.WriteAllText(dlg.FileName, txt);
            UpdateStatus($"Exported NPCs to {IOPath.GetFileName(dlg.FileName)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed:\n{ex.Message}", "Error");
        }
    }

    private void ExportWarps_Click(object sender, RoutedEventArgs e)
    {
        var scriptsDir = ResolveScriptsDir();
        var dlg = new SaveFileDialog
        {
            Filter = "Text (*.txt)|*.txt",
            FileName = "warps_custom.txt",
            InitialDirectory = scriptsDir
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var txt = RAthenaExporter.ExportWarps(_project);
            File.WriteAllText(dlg.FileName, txt);
            UpdateStatus($"Exported Warps to {IOPath.GetFileName(dlg.FileName)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed:\n{ex.Message}", "Error");
        }
    }

    private string? ResolveScriptsDir()
    {
        if (string.IsNullOrEmpty(_project.BaseFolderPath)) return null;
        var sub = string.IsNullOrEmpty(_project.ExportScriptsPath) ? "scripts" : _project.ExportScriptsPath.TrimEnd(IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar);
        var combined = IOPath.Combine(_project.BaseFolderPath, sub);
        return Directory.Exists(combined) ? combined : (Directory.Exists(_project.BaseFolderPath) ? _project.BaseFolderPath : null);
    }

    /// <summary>Export NPCs and Warps into project scripts/ when BaseFolderPath is set; else run individual export dialogs.</summary>
    private void ExportAllToFolder()
    {
        if (string.IsNullOrEmpty(_project.BaseFolderPath) || !Directory.Exists(_project.BaseFolderPath))
        {
            ExportNpcs_Click(this, new RoutedEventArgs());
            ExportWarps_Click(this, new RoutedEventArgs());
            return;
        }
        try
        {
            EnsureFolderStructure(_project.BaseFolderPath);
            var scripts = IOPath.Combine(_project.BaseFolderPath, string.IsNullOrEmpty(_project.ExportScriptsPath) ? "scripts" : _project.ExportScriptsPath);
            if (!Directory.Exists(scripts)) Directory.CreateDirectory(scripts);

            var npcPath = IOPath.Combine(scripts, "npcs_custom.txt");
            var warpPath = IOPath.Combine(scripts, "warps_custom.txt");
            File.WriteAllText(npcPath, RAthenaExporter.ExportNpcs(_project));
            File.WriteAllText(warpPath, RAthenaExporter.ExportWarps(_project));
            UpdateStatus($"Exported NPCs and Warps to scripts/");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed:\n{ex.Message}", "Error");
        }
    }
    private System.Windows.Media.Imaging.BitmapSource LoadBitmapWithTransparency(Stream stream)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = stream;
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze(); // freeze original if possible, but we will convert it

        // Convert to BGRA32 so we can manipulate alpha
        var format = PixelFormats.Bgra32;
        var w = bmp.PixelWidth;
        var h = bmp.PixelHeight;
        var stride = (w * format.BitsPerPixel + 7) / 8;
        var pixels = new byte[h * stride];

        // FormatConvertedBitmap handles the conversion safely
        var converted = new FormatConvertedBitmap(bmp, format, null, 0);
        converted.CopyPixels(pixels, stride, 0);

        // Scan for Magenta (255, 0, 255) and set Alpha to 0
        // BGRA layout: [B, G, R, A]
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] == 255 && pixels[i + 1] == 0 && pixels[i + 2] == 255)
            {
                pixels[i + 3] = 0; // Transparent
            }
        }

        var result = BitmapSource.Create(w, h, 96, 96, format, null, pixels, stride);
        result.Freeze();
        return result;
    }
}
