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
using ROMapOverlayEditor.Sources;
using ROMapOverlayEditor.Grf;
using ROMapOverlayEditor.Vfs;
using ROMapOverlayEditor.MapAssets;
using ROMapOverlayEditor.Patching;
using ROMapOverlayEditor.Gat;
using ROMapOverlayEditor.Ui;
using ROMapOverlayEditor.Tools;
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

    private Ui.GatEditorWindow? _gat3dWindow;
    private ProjectData _project = new();
    private string? _currentProjectPath;

    // Fields for GrfTown logic
    private GrfTownWorkspace? _grfTown;
    private List<TownEntry> _towns = new();
    private string _towninfoPath = "";

    // Workspace sources (new unified abstraction)
    private GrfFileSource? _grfSource;
    private FolderFileSource? _luaFolderSource;
    private CompositeFileSource? _vfs;
    private readonly CompositeVfs _compositeVfs = new();
    private CompositeVfs? _compositeVfs3D;
    private GrfFileSource? _grfSource3D;
    private readonly EditStaging _staging = new();
    private readonly SimpleModeController _simpleMode = new();
    private bool _advancedMode = false;
    private MapTransform? _mapTransform;

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
            
            // Register advanced UI elements for simple mode
            if (MobEditorTab != null) _simpleMode.RegisterAdvanced(MobEditorTab);
            if (NpcEditorTab != null) _simpleMode.RegisterAdvanced(NpcEditorTab);
            if (QuestEditorTab != null) _simpleMode.RegisterAdvanced(QuestEditorTab);
            if (DatabaseTab != null) _simpleMode.RegisterAdvanced(DatabaseTab);
            if (ProjectTab != null) _simpleMode.RegisterAdvanced(ProjectTab);
            

            
            // Wire up Tab Selection
            MainTabs.SelectionChanged += MainTabs_SelectionChanged;

            _simpleMode.Apply(_advancedMode);
            
            // Default startup config
            EditorState.Current.ActiveTool = EditorTool.Select;
            EditorState.Current.SnapToGrid = true;
            EditorState.Current.ShowGrid = true;
            EditorState.Current.ShowLabels = true;
            EditorState.Current.RotateSensitivity = 1.0;
            EditorState.Current.PanSensitivity = 1.0;
            EditorState.Current.ZoomSensitivity = 1.0;
        };
        ObjectsList.ItemsSource = _items;

        UpdatePropsPanel(null); // Just to clear inspector
        UpdateStatus("Ready");
        UpdateMode(EditorMode.Function);
        UpdateWindowTitle();
    }

    private void MainWindow_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        var st = EditorState.Current;
        st.IsShiftDown = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        st.IsCtrlDown = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        st.IsAltDown = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MainTabs?.SelectedItem == Map3DEditorTab)
            EnsureMap3DEditorInitialized();
    }

    /// <summary>
    /// Opens the GRF browser to pick a file path (internal path).
    /// </summary>
    private string? BrowseGrfInternalPath()
    {
        if (_grfSource == null) return null;

        var dlg = new GrfBrowserWindow(_grfSource);
        dlg.Owner = this;
        if (dlg.ShowDialog() == true)
        {
             return dlg.SelectedPath;
        }
        return null;
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
        else if (e.PropertyName == nameof(ProjectData.MapTileWidth) ||
                 e.PropertyName == nameof(ProjectData.MapTileHeight))
        {
            RedrawOverlay();
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

        // Rebuild sources from project data
        RebuildSourcesFromProject();

        LoadBackgroundImageIfAny();
        if (_grfSource != null)
            InitializeTownDropdownFromVfs();
        RedrawGrid();
        RefreshList(selectId);
        RedrawOverlay();
        UpdatePropsPanel(ObjectsList.SelectedItem as Placable);
        if (markDirty) SetDirty();
    }

    /// <summary>Rebuild workspace sources from current project settings.</summary>
    private void RebuildSourcesFromProject()
    {
        // Dispose old GRF source
        _grfSource?.Dispose();
        _grfSource = null;

        // Rebuild GRF source if path is set
        if (!string.IsNullOrEmpty(_project.GrfFilePath) && File.Exists(_project.GrfFilePath))
        {
            try
            {
                _grfSource = new GrfFileSource(_project.GrfFilePath);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Failed to open GRF: {ex.Message}");
            }
        }

        // Rebuild Lua folder source if path is set
        if (!string.IsNullOrEmpty(_project.LuaDataFolderPath) && Directory.Exists(_project.LuaDataFolderPath))
        {
            _luaFolderSource = new FolderFileSource(_project.LuaDataFolderPath, "Lua Folder");
        }
        else
        {
            _luaFolderSource = null;
        }

        RebuildVfs();
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
        
        // Sync EditorState
        EditorState.Current.ActiveTool = mode == EditorMode.Relocate ? EditorTool.MoveObject
            : mode == EditorMode.AddNpc ? EditorTool.PlaceNpc
            : mode == EditorMode.AddWarp ? EditorTool.PlaceWarp
            : EditorTool.Select;
        
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

            // Probe GRF header for diagnostics (before attempting to open)
            var probeResult = Grf.GrfHeaderProbe.Probe(path);
            UpdateStatus(probeResult);
            System.Diagnostics.Debug.WriteLine(probeResult);

            // Use the new GrfFileSource (unified reader) - it validates on construction
            try
            {
                var newSource = new GrfFileSource(path);

                // Dispose old source if any
                _grfSource?.Dispose();
                _grfSource = newSource;
                
                // Clear dimension cache when opening new GRF
                MapDimensionResolver.ClearCache();
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to open GRF:\n\n{ex.Message}\n\n{probeResult}";
                MessageBox.Show(errorMsg, "Failed to open GRF", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _project.GrfFilePath = path;
            _project.GrfInternalPath = null;
            _project.BackgroundImagePath = null;
            _originalTownNpcs.Clear();
            
            // Auto-detect client data path
            TryAutoDetectClientDataPath();

            // Setup VFS sources
            UpdateStatus("Indexing GRF...");

            // Rebuild both Legacy and New VFS (includes GRF, Lua, and Client Data)
            RebuildVfs();
            InitializeTownDropdownFromVfs();

            if (_towns.Count == 0 &&
                MessageBox.Show("Town data not found in GRF. Select a folder that contains Towninfo.lua or Towninfo.lub?", "Lua data", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                if (TryPickAndValidateLuaFolder(out var folder) && folder != null)
                {
                    _project.LuaDataFolderPath = folder;
                    _luaFolderSource = new FolderFileSource(folder, "Lua Folder");
                    RebuildVfs();
                    InitializeTownDropdownFromVfs();
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

    private void MountPack_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Mount Asset Pack",
            Filter = "Archives & Folders (*.zip;*.7z;*.grf)|*.zip;*.7z;*.grf|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dlg.ShowDialog() != true) return;
        var path = dlg.FileName;
        var ext = IOPath.GetExtension(path).ToLowerInvariant();

        try
        {
            if (ext == ".zip" || ext == ".7z" || ext == ".rar")
            {
                var arc = new ArchiveSource(path, priority: 50, displayName: $"Pack: {IOPath.GetFileName(path)}");
                _compositeVfs.Mount(arc);
                UpdateStatus($"Mounted archive: {IOPath.GetFileName(path)}");
            }
            else if (ext == ".grf" || ext == ".gpf")
            {
                // Mount extra GRF (lower priority than main)
                 var extraGrf = new GrfFileSource(path); 
                 _compositeVfs.Mount(new GrfSourceAdapter(
                    displayName: $"Extra GRF: {IOPath.GetFileName(path)}",
                    priority: 60,
                    listPaths: () => extraGrf.EnumeratePaths(),
                    readBytes: (vp) => 
                    {
                        try { return (true, extraGrf.ReadAllBytes(vp), null); }
                        catch (Exception ex) { return (false, null, ex.Message); }
                    }
                ));
                 UpdateStatus($"Mounted extra GRF: {IOPath.GetFileName(path)}");
            }
            else
            {
                MessageBox.Show("Selected file type not supported for packing yet.", "Mount Pack", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            
            // Re-detect assets for current town if any
            if (_project.MapName != null)
                TryAutoLoadMapAssetsForTown(_project.MapName);
        }
        catch (Exception ex)
        {
             MessageBox.Show($"Failed to mount pack:\n{ex.Message}", "Mount Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetLuaFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!TryPickAndValidateLuaFolder(out var folder) || folder == null) return;
        _project.LuaDataFolderPath = folder;
        _luaFolderSource = new FolderFileSource(folder, "Lua Folder");
        RebuildVfs();
        SetDirty();
        
        // Auto-detect client data path
        TryAutoDetectClientDataPath();

        if (_grfSource != null)
        {
            InitializeTownDropdownFromVfs();
            LoadBackgroundImageIfAny();
            RedrawGrid();
            RedrawOverlay();
            
            // Show result
            if (_towns.Count > 0)
            {
                UpdateStatus($"Lua folder set: {IOPath.GetFileName(folder)} — {_towns.Count} towns loaded");
            }
            else
            {
                UpdateStatus($"Lua folder set: {IOPath.GetFileName(folder)} — no towns parsed (check file format)");
            }
        }
        else
        {
            // Test if we can find Towninfo even without GRF
            var testVfs = new CompositeFileSource(null, _luaFolderSource);
            var (testTowns, _, testWarning) = TowninfoResolver.LoadTownList(testVfs);
            if (testTowns.Count > 0)
            {
                MessageBox.Show(
                    $"Lua folder set successfully.\n\n" +
                    $"Found {testTowns.Count} towns in:\n{folder}\n\n" +
                    "Open a GRF file to load map images and complete the setup.",
                    "Lua Folder", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    $"Lua folder set, but no towns were parsed.\n\n" +
                    $"Folder: {folder}\n\n" +
                    $"Reason: {testWarning}\n\n" +
                    "The file may be bytecode or use a non-standard format.\n" +
                    "Try selecting a different folder or use a decompiled Towninfo.lua file.",
                    "Lua Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
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
        if (string.IsNullOrWhiteSpace(root))
        {
            MessageBox.Show("No folder was selected. Please select a folder and try again.", "Lua Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        
        if (!Directory.Exists(root))
        {
            MessageBox.Show($"The selected folder does not exist:\n{root}", "Lua Folder", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        // Check for Towninfo files specifically (more helpful than just "any Lua file")
        var towninfoFiles = new List<string>();
        
        // Check direct files
        var directLua = IOPath.Combine(root, "Towninfo.lua");
        var directLub = IOPath.Combine(root, "Towninfo.lub");
        if (File.Exists(directLua)) towninfoFiles.Add("Towninfo.lua");
        if (File.Exists(directLub)) towninfoFiles.Add("Towninfo.lub");
        
        // Check System subfolder
        var systemLua = IOPath.Combine(root, "System", "Towninfo.lua");
        var systemLub = IOPath.Combine(root, "System", "Towninfo.lub");
        if (File.Exists(systemLua)) towninfoFiles.Add("System\\Towninfo.lua");
        if (File.Exists(systemLub)) towninfoFiles.Add("System\\Towninfo.lub");
        
        // Check data\System subfolder
        var dataSystemLua = IOPath.Combine(root, "data", "System", "Towninfo.lua");
        var dataSystemLub = IOPath.Combine(root, "data", "System", "Towninfo.lub");
        if (File.Exists(dataSystemLua)) towninfoFiles.Add("data\\System\\Towninfo.lua");
        if (File.Exists(dataSystemLub)) towninfoFiles.Add("data\\System\\Towninfo.lub");
        
        // Also do a recursive search as fallback
        if (towninfoFiles.Count == 0)
        {
            try
            {
                var found = Directory.EnumerateFiles(root, "Towninfo.lua", SearchOption.AllDirectories)
                    .Concat(Directory.EnumerateFiles(root, "Towninfo.lub", SearchOption.AllDirectories))
                    .Take(5)
                    .ToList();
                
                if (found.Count > 0)
                {
                    foreach (var f in found)
                    {
                        var rel = IOPath.GetRelativePath(root, f);
                        towninfoFiles.Add(rel);
                    }
                }
            }
            catch { }
        }

        if (towninfoFiles.Count == 0)
        {
            var message = $"No Towninfo.lua or Towninfo.lub files found in:\n{root}\n\n" +
                         "Please select a folder that contains:\n" +
                         "• Towninfo.lua or Towninfo.lub (directly in the folder), OR\n" +
                         "• System\\Towninfo.lua/lub, OR\n" +
                         "• data\\System\\Towninfo.lua/lub\n\n" +
                         "Tip: If you selected 'data\\System', try selecting 'data' or the client root folder instead.";
            MessageBox.Show(message, "Lua Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        folderPath = root;
        return true;
    }

    /// <summary>Rebuild the composite VFS from current GRF and Lua folder sources.</summary>
    private void RebuildVfs()
    {
        // 1. Legacy VFS (CompositeFileSource)
        _vfs = new CompositeFileSource(_grfSource, _luaFolderSource);

        // 2. New VFS (CompositeVfs)
        _compositeVfs.UnmountAll();

        // Mount GRF
        if (_grfSource != null && !string.IsNullOrEmpty(_project.GrfFilePath))
        {
            _compositeVfs.Mount(new GrfSourceAdapter(
                displayName: $"Main GRF: {IOPath.GetFileName(_project.GrfFilePath)}",
                priority: 100,
                listPaths: () => _grfSource.EnumeratePaths(),
                readBytes: (vp) =>
                {
                    try { return (true, _grfSource.ReadAllBytes(vp), null); }
                    catch (Exception ex) { return (false, null, ex.Message); }
                }
            ));
        }

        // Mount Lua Data Folder
        if (!string.IsNullOrEmpty(_project.LuaDataFolderPath) && Directory.Exists(_project.LuaDataFolderPath))
        {
            _compositeVfs.Mount(new FolderSource(_project.LuaDataFolderPath, 80, "Lua Folder"));
        }

        // Mount Client Data Path (Auto-detected or manually set)
        if (!string.IsNullOrEmpty(_project.ClientDataPath) && Directory.Exists(_project.ClientDataPath))
        {
            // Mount the folder itself (e.g. access "prontera.gat" directly)
            _compositeVfs.Mount(new FolderSource(_project.ClientDataPath, 90, "Client Data"));

            // If the folder is named "data", mount its parent too (so "data/prontera.gat" works)
            var name = IOPath.GetFileName(_project.ClientDataPath);
            if (name.Equals("data", StringComparison.OrdinalIgnoreCase))
            {
                var parent = IOPath.GetDirectoryName(_project.ClientDataPath);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                {
                    _compositeVfs.Mount(new FolderSource(parent, 91, "Client Root"));
                }
            }
        }
    }

    /// <summary>Initialize town dropdown using the VFS and TowninfoResolver.</summary>
    private void InitializeTownDropdownFromVfs()
    {
        if (_vfs == null)
        {
            TownCombo.ItemsSource = null;
            TownCombo.IsEnabled = false;
            CopyExportBtn.IsEnabled = false;
            return;
        }

        var (towns, sourcePath, warning) = TowninfoResolver.LoadTownList(_vfs);
        _towns = towns;
        _towninfoPath = sourcePath;

        // Also update the legacy GrfTownWorkspace for compatibility with LoadTown
        if (_grfSource != null)
        {
            _grfTown = new GrfTownWorkspace(
                IOPath.GetFileName(_grfSource.GrfPath),
                listPaths: () => _grfSource.EnumeratePaths().ToList(),
                existsInGrf: (p) => _grfSource.Exists(p),
                readBytesFromGrf: (p) => _grfSource.ReadAllBytes(p),
                luaDataFolderPath: _project.LuaDataFolderPath
            );
        }

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

    // Legacy InitializeTownDropdownFromGrf removed - now using InitializeTownDropdownFromVfs with unified sources

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

    private void ExportPatchZip_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "ZIP Patch (*.zip)|*.zip",
            FileName = "ro_patch.zip"
        };
        if (dlg.ShowDialog() != true) return;

        var manifest =
            $"ROMapOverlayEditor Patch\n" +
            $"Files: {_staging.Files.Count}\n" +
            $"Generated: {DateTime.Now}\n";

        PatchWriter.WriteZip(dlg.FileName, _staging.Files, manifest);
        UpdateStatus($"Patch exported: {IOPath.GetFileName(dlg.FileName)} ({_staging.Files.Count} files)");
    }

    private void OpenGat3DEditor_Click(object sender, RoutedEventArgs e)
    {
        if (_gat3dWindow == null || !_gat3dWindow.IsLoaded)
        {
            _gat3dWindow = new Ui.GatEditorWindow();
            _gat3dWindow.Owner = this;
            _gat3dWindow.Show();
            _gat3dWindow.Initialize(GetVfsFor3D(), _staging, BrowseGrfInternalPath);
        }
        else
        {
            _gat3dWindow.Activate();
        }

        var map = (TownCombo.SelectedItem as TownEntry)?.Name ?? _project.MapName;
        if (!string.IsNullOrWhiteSpace(map))
            _gat3dWindow.LoadMap(map);
    }

    /// <summary>VFS for 3D Map Editor: dedicated 3D GRF if set, else main project GRF.</summary>
    private CompositeVfs GetVfsFor3D()
    {
        if (_compositeVfs3D != null && _compositeVfs3D.Sources.Count > 0)
            return _compositeVfs3D;
        return _compositeVfs;
    }

    private void OpenGrfFor3D_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new OpenFileDialog
            {
                Title = "Open GRF for 3D Map Editor",
                Filter = "Ragnarok Archives (*.grf;*.gpf)|*.grf;*.gpf|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dlg.ShowDialog() != true) return;

            var path = dlg.FileName;
            _grfSource3D?.Dispose();
            _grfSource3D = new GrfFileSource(path);

            _compositeVfs3D = new CompositeVfs();
            _compositeVfs3D.Mount(new GrfSourceAdapter(
                displayName: $"3D GRF: {IOPath.GetFileName(path)}",
                priority: 100,
                listPaths: () => _grfSource3D!.EnumeratePaths(),
                readBytes: (vp) =>
                {
                    try { return (true, _grfSource3D!.ReadAllBytes(vp), null); }
                    catch (Exception ex) { return (false, null, ex.Message); }
                }
            ));

            EnsureMap3DEditorInitialized();
            PopulateRswMapDropdown();
            if (Map3DStatusText != null)
                Map3DStatusText.Text = $"3D GRF: {IOPath.GetFileName(path)} ({VfsPathResolver.EnumerateRswMapNames(_compositeVfs3D!).Count} RSW maps)";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open GRF for 3D.\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadMap3D_Click(object sender, RoutedEventArgs e)
    {
        var name = (Map3DMapCombo?.SelectedItem as string) ?? Map3DMapCombo?.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Select an RSW map from the dropdown or type a map name (e.g. prontera).", "3D Map Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        EnsureMap3DEditorInitialized();
        GatEditorView3DTab?.LoadMap(name);
    }

    private void Map3DMapCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Auto-load when user picks a different map from dropdown (BrowEdit-style). Skip when populating (e.RemovedItems empty = initial set).
        if (e?.AddedItems?.Count > 0 && e.AddedItems[0] is string name && !string.IsNullOrWhiteSpace(name) &&
            e.RemovedItems?.Count > 0 && GatEditorView3DTab != null && GetVfsFor3D().Sources.Count > 0)
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                if (Map3DMapCombo?.SelectedItem is string sel && sel == name)
                    GatEditorView3DTab.LoadMap(name);
            }));
        }
    }

    private void PopulateRswMapDropdown()
    {
        if (Map3DMapCombo == null) return;
        var vfs = GetVfsFor3D();
        if (vfs.Sources.Count == 0)
        {
            Map3DMapCombo.ItemsSource = null;
            Map3DMapCombo.Text = "";
            return;
        }
        var names = VfsPathResolver.EnumerateRswMapNames(vfs);
        Map3DMapCombo.ItemsSource = names;
        if (names.Count > 0)
            Map3DMapCombo.SelectedIndex = 0;
        else
            Map3DMapCombo.Text = "";
    }

    /// <summary>Initialize 3D tab view with current VFS when tab is used.</summary>
    private void EnsureMap3DEditorInitialized()
    {
        var vfs = GetVfsFor3D();
        if (vfs.Sources.Count == 0)
        {
            if (Map3DStatusText != null)
                Map3DStatusText.Text = "Open a GRF (Map Editor tab or 'Open GRF for 3D…').";
            if (Map3DMapCombo != null)
                Map3DMapCombo.ItemsSource = null;
            return;
        }
        GatEditorView3DTab?.Initialize(vfs, _staging, () => _project?.GrfInternalPath);
        PopulateRswMapDropdown();
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
        if (_grfSource == null) return;

        try
        {
            var bytes = _grfSource.ReadAllBytes(internalPath);
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
        // AUTO-FETCH MAP DIMENSIONS FROM GAT
        var dimResult = MapDimensionResolver.GetDimensions(town.Name, _compositeVfs);
        if (dimResult.Success)
        {
            _project.MapTileWidth = dimResult.Width;
            _project.MapTileHeight = dimResult.Height;
            UpdateStatus($"Loaded {town.Name}: {dimResult.Width}x{dimResult.Height} tiles");
        }
        else
        {
            // Reset to 0 to trigger estimation
            _project.MapTileWidth = 0;
            _project.MapTileHeight = 0;
            UpdateStatus($"Loaded {town.Name} (dimensions unknown, using estimate)");
        }
        
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
        
        // Auto-fit to content if no background image is present
        FitViewToOverlayContentIfNeeded();
        
        // After town NPCs are loaded, attempt to auto-load minimap + gat from the currently open GRF.
        TryAutoLoadMapAssetsForTown(town.Name);

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

        // 1) GRF source (use unified GrfFileSource if available)
        if (_grfSource != null && !string.IsNullOrEmpty(_project.GrfInternalPath))
        {
            try
            {
                byte[] bytes = _grfSource.ReadAllBytes(_project.GrfInternalPath);
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

    private void GridToggle_Changed(object sender, RoutedEventArgs e)
    {
        EditorState.Current.ShowGrid = GridToggle.IsChecked == true;
        RedrawGrid();
    }
    private void LabelsToggle_Changed(object sender, RoutedEventArgs e)
    {
        EditorState.Current.ShowLabels = LabelsToggle.IsChecked == true;
        RedrawOverlay();
    }

    private void AdvancedModeToggle_Changed(object sender, RoutedEventArgs e)
    {
        _advancedMode = AdvancedModeToggle?.IsChecked == true;
        _simpleMode.Apply(_advancedMode);
        UpdateStatus(_advancedMode ? "Advanced mode: ON" : "Advanced mode: OFF");
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Modifier state (always sync)
        var st = EditorState.Current;
        st.IsShiftDown = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        st.IsCtrlDown = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        st.IsAltDown = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

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

        // Tool hotkeys: Q=Select, W=Move, E=PlaceNpc, R=PaintGat
        if (e.Key == Key.Q) { st.ActiveTool = EditorTool.Select; UpdateMode(EditorMode.Function); e.Handled = true; return; }
        if (e.Key == Key.W) { st.ActiveTool = EditorTool.MoveObject; UpdateMode(EditorMode.Relocate); e.Handled = true; return; }
        if (e.Key == Key.E) { st.ActiveTool = EditorTool.PlaceNpc; UpdateMode(EditorMode.AddNpc); e.Handled = true; return; }
        if (e.Key == Key.R) { st.ActiveTool = EditorTool.PaintGat_Walkable; e.Handled = true; return; }

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

    // ═══════════════════════════════════════════════════════════════════════════════
    // MAP DIMENSION HANDLING
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Get map dimensions, trying multiple sources in order:
    /// 1. Project settings (if manually set)
    /// 2. GAT file in GRF (authoritative source)
    /// 3. Fallback estimation from image size
    /// </summary>
    private (double Width, double Height) GetMapDimensions()
    {
        // Priority 1: Manual project settings
        if (_project.MapTileWidth > 0 && _project.MapTileHeight > 0)
            return (_project.MapTileWidth, _project.MapTileHeight);

        // Priority 2: Read from GAT file (using VFS with filesystem fallback)
        var mapName = _project.MapName;
        if (!string.IsNullOrWhiteSpace(mapName))
        {
            var result = MapDimensionResolver.GetDimensionsWithFallback(mapName, _compositeVfs, _project.ClientDataPath);
            if (result.Success)
            {
                // Cache in project for future use
                _project.MapTileWidth = result.Width;
                _project.MapTileHeight = result.Height;
                return (result.Width, result.Height);
            }
        }

        // Priority 3: Estimate from image dimensions
        if (BgImage?.Source is BitmapSource bmp)
        {
            // Assume 1:1 for unknown maps (not ideal but safe fallback)
            return (bmp.PixelWidth, bmp.PixelHeight);
        }

        // Default fallback
        return (400, 400);
    }

    /// <summary>
    /// Convert tile coordinates to pixel coordinates for display.
    /// Handles Y-axis inversion (RO bottom-left → image top-left).
    /// Uses MapTransform when available for correct placement.
    /// </summary>
    private Point TileToPixelCenter(int tileX, int tileY)
    {
        if (_mapTransform != null && _mapTransform.IsSane())
        {
            var (px, py) = _mapTransform.TileToPixelCenter(tileX, tileY);
            return new Point(px, py);
        }

        // Fallback to old method if transform not available
        if (BgImage?.Source is not BitmapSource bmp)
            return new Point(0, 0);

        double imageWidth = bmp.PixelWidth;
        double imageHeight = bmp.PixelHeight;

        // Get authoritative map dimensions from GAT
        var (mapTileWidth, mapTileHeight) = GetMapDimensions();

        // Calculate scale factors
        double scaleX = imageWidth / mapTileWidth;
        double scaleY = imageHeight / mapTileHeight;

        // Convert with Y-axis inversion
        // RO: Y=0 at bottom, increases upward
        // Image: Y=0 at top, increases downward
        double pixelX = tileX * scaleX;
        double pixelY = (mapTileHeight - tileY) * scaleY;

        // Return center of tile
        return new Point(pixelX + scaleX / 2.0, pixelY + scaleY / 2.0);
    }

    /// <summary>
    /// Convert pixel coordinates (mouse click) to tile coordinates.
    /// Inverse of TileToPixelCenter.
    /// </summary>
    private (int tileX, int tileY) PixelToTile(Point pixelPoint)
    {
        if (BgImage?.Source is not BitmapSource bmp)
            return (0, 0);

        double imageWidth = bmp.PixelWidth;
        double imageHeight = bmp.PixelHeight;

        var (mapTileWidth, mapTileHeight) = GetMapDimensions();

        double scaleX = imageWidth / mapTileWidth;
        double scaleY = imageHeight / mapTileHeight;

        // Inverse conversion with Y-axis correction
        int tileX = (int)Math.Floor(pixelPoint.X / scaleX);
        int tileY = (int)Math.Floor(mapTileHeight - (pixelPoint.Y / scaleY));

        // Clamp to valid range
        tileX = Math.Clamp(tileX, 0, (int)mapTileWidth - 1);
        tileY = Math.Clamp(tileY, 0, (int)mapTileHeight - 1);

        return (tileX, tileY);
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

            // Labels
            if (LabelsToggle.IsChecked == true || isSelected)
            {
                var label = new TextBlock
                {
                    Text = item.Label,
                    FontSize = 10,
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)),
                    Padding = new Thickness(2),
                    IsHitTestVisible = false
                };
                
                // Measure to center
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(label, p.X - label.DesiredSize.Width / 2);
                Canvas.SetTop(label, p.Y - marker.Height / 2 - label.DesiredSize.Height - 2);
                
                OverlayLayer.Children.Add(label);
            }
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
    private Size ComputeOverlayContentSizeFromItems(double paddingPx = 64)
    {
        // If we have a bitmap loaded, that is the authoritative content size.
        if (BgImage?.Source is BitmapSource bmp && bmp.PixelWidth > 0 && bmp.PixelHeight > 0)
            return new Size(bmp.PixelWidth, bmp.PixelHeight);

        // Otherwise infer content bounds from placed items (tile coords -> pixel coords).
        if (_project.Items == null || _project.Items.Count == 0)
            return new Size(0, 0);

        var ppt = _project.PixelsPerTile;
        if (ppt <= 0) ppt = 8.0;

        int maxX = _project.Items.Max(i => i.X);
        int maxY = _project.Items.Max(i => i.Y);

        // Convert max tile to pixel extent (tile origin + tile width)
        double w = (maxX + 2) * ppt + paddingPx;
        double h = (maxY + 2) * ppt + paddingPx;

        // Prevent absurdly tiny results
        w = Math.Max(w, 512);
        h = Math.Max(h, 512);

        return new Size(w, h);
    }

    private void EnsureVirtualCanvasExtents(Size content)
    {
        // If there is no bitmap loaded, BgImage.Width/Height are 0,
        // but we still need the layers to occupy a virtual coordinate space,
        // otherwise everything is offscreen and FitToView has nothing to target.
        if (content.Width <= 0 || content.Height <= 0) return;

        if (GridLayer != null)
        {
            GridLayer.Width = content.Width;
            GridLayer.Height = content.Height;
        }

        if (OverlayLayer != null)
        {
            OverlayLayer.Width = content.Width;
            OverlayLayer.Height = content.Height;
        }
    }

    private void FitViewToOverlayContentIfNeeded(bool force = false)
    {
        if (ZCanvas == null) return;

        // If a real background image exists, the current behavior already fits on load.
        // Only auto-fit when there is NO background image (blank canvas) OR when forced.
        bool hasBmp = BgImage?.Source is BitmapSource bmp && bmp.PixelWidth > 0 && bmp.PixelHeight > 0;
        if (hasBmp && !force) return;

        var content = ComputeOverlayContentSizeFromItems();
        if (content.Width <= 0 || content.Height <= 0) return;

        EnsureVirtualCanvasExtents(content);

        // Make sure layout is ready before reading ActualWidth/Height
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            var viewport = new Size(ZCanvas.ActualWidth, ZCanvas.ActualHeight);
            if (viewport.Width <= 0 || viewport.Height <= 0) return;

            ZCanvas.FitToView(content, viewport);
        }));
    }
    private void TryAutoLoadMapAssetsForTown(string mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName)) return;

        // 1) Find Minimap using VFS and MapResolver
        var bestBmp = MapResolver.FindMinimapPath(_compositeVfs, mapName);
        
        if (!string.IsNullOrEmpty(bestBmp))
        {
            try 
            {
                if (_compositeVfs.TryReadAllBytes(bestBmp, out var bytes, out var err) && bytes != null)
                {
                    var minimap = LoadBitmapWithTransparency(new MemoryStream(bytes));
                    
                    if (BgImage != null) { BgImage.Source = minimap; BgImage.Width = minimap.PixelWidth; BgImage.Height = minimap.PixelHeight; }
                    if (GridLayer != null) { GridLayer.Width = minimap.PixelWidth; GridLayer.Height = minimap.PixelHeight; }
                    if (OverlayLayer != null) { OverlayLayer.Width = minimap.PixelWidth; OverlayLayer.Height = minimap.PixelHeight; }
                    
                    // Update project references
                    _project.GrfInternalPath = bestBmp; // Stored as "relative" path in VFS
                    _project.BackgroundImagePath = null;
                    
                    // Fit view
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                    {
                        if (ZCanvas != null && minimap != null)
                        {
                            var viewport = new Size(ZCanvas.ActualWidth, ZCanvas.ActualHeight);
                            var content = new Size(minimap.PixelWidth, minimap.PixelHeight);
                            ZCanvas.FitToView(content, viewport);
                        }
                    }));
                    
                    UpdateStatus($"Loaded map image: {bestBmp}");
                    
                    // Build MapTransform if we have GAT dimensions (with filesystem fallback)
                    var gatResult = MapDimensionResolver.GetDimensionsWithFallback(mapName, _compositeVfs, _project.ClientDataPath);
                    if (gatResult.Success && minimap != null)
                    {
                        try
                        {
                            _mapTransform = MapTransformBuilder.Build(
                                imgW: minimap.PixelWidth,
                                imgH: minimap.PixelHeight,
                                gatW: gatResult.Width,
                                gatH: gatResult.Height
                            );
                            _project.PixelsPerTile = _mapTransform.PixelsPerTile;
                            _project.MapTileWidth = gatResult.Width;
                            _project.MapTileHeight = gatResult.Height;
                            UpdateStatus($"MapTransform: {_mapTransform}");
                        }
                        catch (Exception ex)
                        {
                            UpdateStatus($"MapTransform build failed: {ex.Message}");
                        }
                    }
                    
                    RedrawOverlay();
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Failed to load map image: {ex.Message}");
            }
        }
        else
        {
             UpdateStatus($"Map image not found for '{mapName}'");
        }
    }

    /// <summary>
    /// Auto-detect client data folder path from GRF or Lua folder location.
    /// </summary>
    private void TryAutoDetectClientDataPath()
    {
        // If already set, don't override
        if (!string.IsNullOrWhiteSpace(_project.ClientDataPath) && Directory.Exists(_project.ClientDataPath))
            return;

        // Strategy 1: If GRF is at F:\...\client\data.grf, data folder is F:\...\client\data
        if (!string.IsNullOrEmpty(_project.GrfFilePath))
        {
            var grfDir = IOPath.GetDirectoryName(_project.GrfFilePath);
            if (!string.IsNullOrEmpty(grfDir))
            {
                var dataPath = IOPath.Combine(grfDir, "data");
                if (Directory.Exists(dataPath) && Directory.GetFiles(dataPath, "*.gat", SearchOption.TopDirectoryOnly).Length > 0)
                {
                    _project.ClientDataPath = dataPath;
                    UpdateStatus($"Auto-detected client data: {dataPath}");
                    return;
                }
            }
        }

        // Strategy 2: If Lua folder is set, walk up to find data folder
        if (!string.IsNullOrEmpty(_project.LuaDataFolderPath))
        {
            var dir = _project.LuaDataFolderPath;
            while (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                if (IOPath.GetFileName(dir).Equals("data", StringComparison.OrdinalIgnoreCase))
                {
                    if (Directory.GetFiles(dir, "*.gat", SearchOption.TopDirectoryOnly).Length > 0)
                    {
                        _project.ClientDataPath = dir;
                        UpdateStatus($"Auto-detected client data: {dir}");
                        return;
                    }
                }
                var parent = IOPath.GetDirectoryName(dir);
                if (parent == dir) break; // reached root
                dir = parent;
            }
        }
    }

}
