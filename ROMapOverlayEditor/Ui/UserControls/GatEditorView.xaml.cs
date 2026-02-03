// ============================================================================
// GatEditorView.xaml.cs - FIXED VERSION with Paint GAT Toggle
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Microsoft.Win32;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using ROMapOverlayEditor.Gat;
using ROMapOverlayEditor.Input;
using ROMapOverlayEditor.MapAssets;
using ROMapOverlayEditor.Patching;
using ROMapOverlayEditor.Rsw;
using ROMapOverlayEditor.Tools;
using HelixHit = ROMapOverlayEditor.Tools.HelixHitCompat;
using ROMapOverlayEditor.Vfs;
using ROMapOverlayEditor.Gnd;
using ROMapOverlayEditor.Map3D;
using ROMapOverlayEditor.ThreeD;
using TerrainBuilderLM = ROMapOverlayEditor.Map3D.TerrainBuilderWithLightmaps;
using ROMapOverlayEditor.Rsm;

namespace ROMapOverlayEditor.UserControls
{
    public partial class GatEditorView : UserControl
    {
        // ====================================================================
        // FIELDS
        // ====================================================================
        
        private CompositeVfs? _vfs;
        private EditStaging? _staging;
        
        private string _mapName = "";
        private GatFile? _gat;
        private RswFile? _rsw;
        private string _gatVirtualPath = "";
        
        private GndFileV2? _gndMap3D;
        private byte[]? _gndBytes;

        private readonly object _loadLock = new();

        private readonly ROMapOverlayEditor.ThreeD.BrowEditCameraController _cam = new();
        private readonly EditorInputRouter _inputRouter;
        
        private readonly object _rebuildSync = new();
        private CancellationTokenSource? _rebuildCts;
        private const double roScale = 1.0;

        private Model3D? _terrainModel;
        private int _terrainBuildId;
        private bool _needsCameraReset = true;
        private bool _isPaintModeActive = false;

        private readonly Dictionary<Visual3D, RswObject> _visualToObj = new();

#region Inspector UI Helpers

// Selected object backing for the Inspector panel
private RswObject? _selectedObject;

// Lazy-wired inspector controls (so XAML mismatches won't crash)
private Button? _inspectorFocusBtn;
private Button? _inspectorDeleteBtn;
private TextBox? _inspectorMapTxt;
private TextBox? _inspectorXTxt;
private TextBox? _inspectorYTxt;

// Safe UI invoke wrapper
private void SafeUi(Action a, string context)
{
    try { a(); }
    catch (Exception ex)
    {
        try { Set3DStatus($"{context} failed: {ex.Message}"); } catch { }
        try { System.Diagnostics.Debug.WriteLine($"[{context}] {ex}"); } catch { }
        try { MessageBox.Show($"{context} failed:\n{ex.Message}", "ROMapOverlayEditor", MessageBoxButton.OK, MessageBoxImage.Error); } catch { }
    }
}

// Safe find-name helper (won't throw if name not present)
private T? FindByName<T>(string name) where T : class
{
    try { return this.FindName(name) as T; }
    catch { return null; }
}

// Called once when control is loaded; wires Inspector buttons safely
private void WireInspectorIfPresent()
{
    // NOTE: change these names ONLY if your XAML uses different x:Name values.
    _inspectorFocusBtn = FindByName<Button>("InspectorFocusButton") ?? FindByName<Button>("FocusButton") ?? FindByName<Button>("BtnFocus");
    _inspectorDeleteBtn = FindByName<Button>("InspectorDeleteButton") ?? FindByName<Button>("DeleteButton") ?? FindByName<Button>("BtnDelete");

    _inspectorMapTxt = FindByName<TextBox>("InspectorMapText") ?? FindByName<TextBox>("MapText") ?? FindByName<TextBox>("TxtMap");
    _inspectorXTxt   = FindByName<TextBox>("InspectorXText")   ?? FindByName<TextBox>("XText")   ?? FindByName<TextBox>("TxtX");
    _inspectorYTxt   = FindByName<TextBox>("InspectorYText")   ?? FindByName<TextBox>("YText")   ?? FindByName<TextBox>("TxtY");

    if (_inspectorFocusBtn != null)
        _inspectorFocusBtn.Click += Inspector_Focus_Click;

    if (_inspectorDeleteBtn != null)
        _inspectorDeleteBtn.Click += Inspector_Delete_Click;

    UpdateInspectorUi();
}

private void UpdateInspectorUi()
{
    // Don't let missing controls crash the app
    try
    {
        if (_inspectorMapTxt != null)
            _inspectorMapTxt.Text = _selectedObject is RswModel m ? (m.FileName ?? "") : (_selectedObject?.GetType().Name ?? "");

        // Your Inspector X/Y appears to be 2D map coords (RO uses X/Z; your panel is X/Y).
        // We'll present X and Z if available.
        if (_inspectorXTxt != null)
            _inspectorXTxt.Text = _selectedObject != null ? $"{_selectedObject.Position.X:0.###}" : "";

        if (_inspectorYTxt != null)
            _inspectorYTxt.Text = _selectedObject != null ? $"{_selectedObject.Position.Z:0.###}" : "";
    }
    catch { }
}

#endregion

#region Inspector Handlers

private void Inspector_Focus_Click(object? sender, RoutedEventArgs e)
{
    SafeUi(() =>
    {
        if (_selectedObject == null)
        {
            Set3DStatus("Inspector: no selected object to focus.");
            return;
        }

        // Focus camera to selected object's world position
        var p = _selectedObject.Position;
        _cam.SetTarget(p.X * roScale, Math.Max(10, p.Y * roScale), p.Z * roScale);

        // Pull camera back a bit so focus is visible
        _cam.SetDistance(Math.Max(_cam.Distance, 120));

        ApplyCameraToRenderer();
        Set3DStatus("Focused camera on selected object.");
    }, "Inspector Focus");
}

private void Inspector_Delete_Click(object? sender, RoutedEventArgs e)
{
    SafeUi(() =>
    {
        if (_selectedObject == null || _rsw == null)
        {
            Set3DStatus("Inspector: nothing selected / no RSW loaded.");
            return;
        }

        // Only delete if it actually exists in RSW objects list
        int idx = _rsw.Objects.IndexOf(_selectedObject);
        if (idx < 0)
        {
            Set3DStatus("Inspector: selected object not found in current RSW.");
            return;
        }

        // Remove, clear selection, rebuild visuals
        _rsw.Objects.RemoveAt(idx);
        _selectedObject = null;
        UpdateInspectorUi();
        RebuildMesh();
        Set3DStatus("Deleted selected object from RSW (runtime list).");
    }, "Inspector Delete");
}

#endregion

        // ====================================================================
        // EVENTS
        // ====================================================================
        
        public event EventHandler<string>? MapLoaded;

        // ====================================================================
        // CONSTRUCTOR
        // ====================================================================

        public GatEditorView()
        {
            InitializeComponent();
            _inputRouter = new EditorInputRouter(_cam, ApplyCameraToRenderer);
            
            Viewport.MouseDown += Viewport_MouseDown;
            Viewport.MouseUp += Viewport_MouseUp;
            Viewport.MouseMove += Viewport_MouseMove;
            Viewport.MouseWheel += Viewport_MouseWheel;

            Viewport.Camera = new PerspectiveCamera
            {
                Position = new Point3D(0, 200, 200),
                LookDirection = new Vector3D(0, -200, -200),
                UpDirection = new Vector3D(0, 1, 0),
                FieldOfView = 45
            };
            Viewport.Children.Add(new DefaultLights());
            Viewport.IsHeadLightEnabled = true;
            Viewport.ShowFrameRate = true;

            TypeCombo.ItemsSource = Enum.GetValues(typeof(GatCellType)).Cast<GatCellType>();
            TypeCombo.SelectedItem = GatCellType.NotWalkable;

            RadiusSlider.ValueChanged += (_, _) => RadiusLabel.Text = $"Brush Radius: {(int)RadiusSlider.Value}";
            
            PreviewText.Text = "Enter a map name and click Load to see resolved paths and header info.";

            this.Loaded += (_, __) => SafeUi(WireInspectorIfPresent, "WireInspectorIfPresent");
        }

        // ====================================================================
        // INITIALIZATION
        // ====================================================================
        
        private Func<string?>? _browseGrf;

        public void Initialize(CompositeVfs vfs, EditStaging staging, Func<string?> browseGrf)
        {
            _vfs = vfs;
            _staging = staging;
            _browseGrf = browseGrf;
            
            if (_vfs == null)
                System.Diagnostics.Debug.WriteLine("[GatEditorView] WARNING: VFS is null after Initialize!");
            else
                System.Diagnostics.Debug.WriteLine("[GatEditorView] VFS initialized successfully");
        }

        // ====================================================================
        // EVENT HANDLERS - XAML BINDINGS
        // ====================================================================

        private void CameraSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_cam != null && CameraSpeedSlider != null)
                _cam.SpeedMultiplier = (float)CameraSpeedSlider.Value;
        }

        private void SensSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_cam == null) return;

            if (sender == RotateSensSlider && RotateSensSlider != null)
                _cam.RotateSensitivity = (float)RotateSensSlider.Value;
            else if (sender == PanSensSlider && PanSensSlider != null)
                _cam.PanSensitivity = (float)PanSensSlider.Value;
            else if (sender == ZoomSensSlider && ZoomSensSlider != null)
                _cam.ZoomSensitivity = (float)ZoomSensSlider.Value;
        }

        private void ViewOptionChanged(object sender, RoutedEventArgs e)
        {
            _terrainModel = null;
            RebuildMesh();
        }

        private void ResetView_Click(object sender, RoutedEventArgs e)
        {
            if (_gndMap3D != null)
            {
                double cx = (_gndMap3D.Width * 0.5) * _gndMap3D.TileScale;
                double cz = (_gndMap3D.Height * 0.5) * _gndMap3D.TileScale;
                double dist = Math.Max(_gndMap3D.Width * _gndMap3D.TileScale, _gndMap3D.Height * _gndMap3D.TileScale) * 1.25;
                _cam.ResetEvenOut(suggestedDistance: dist, tx: cx, ty: 100, tz: cz);
                ApplyCameraToRenderer();
            }
            _needsCameraReset = true;
            Viewport?.ZoomExtents();
        }

        private void PaintModeToggle_Checked(object sender, RoutedEventArgs e)
        {
            _isPaintModeActive = true;
            Set3DStatus("Paint mode enabled");
            
            if (PaintControlsPanel != null)
            {
                PaintControlsPanel.IsEnabled = true;
                PaintControlsPanel.Opacity = 1.0;
            }
            
            if (PaintModeToggle != null)
                PaintModeToggle.Content = "🎨 Paint Mode ACTIVE";
            
            if (ChkGatOverlay != null && ChkGatOverlay.IsChecked != true)
                ChkGatOverlay.IsChecked = true;
            
            EditorState.Current.ActiveTool = EditorTool.PaintGat_Walkable;
            PickInfo.Text = "Paint mode enabled. Left-click on terrain to paint cells.";
        }
        
        private void PaintModeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _isPaintModeActive = false;
            
            if (PaintControlsPanel != null)
            {
                PaintControlsPanel.IsEnabled = false;
                PaintControlsPanel.Opacity = 0.5;
            }
            
            if (PaintModeToggle != null)
                PaintModeToggle.Content = "🎨 Enable Paint Mode";
            
            EditorState.Current.ActiveTool = EditorTool.Select;
            PickInfo.Text = "Paint mode disabled. Click to select cells.";
        }

        private void Rebuild_Click(object sender, RoutedEventArgs e) => RebuildMesh();

        private void SaveToStaging_Click(object sender, RoutedEventArgs e)
        {
            SafeUi(() =>
            {
                if (_gat == null)
                {
                    MessageBox.Show("No GAT loaded.", "Save to Staging", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_staging == null)
                {
                    MessageBox.Show("Edit staging not initialized.", "Save to Staging", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(_gatVirtualPath))
                {
                    MessageBox.Show("GAT path is empty (map not loaded properly).", "Save to Staging", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                byte[] gatBytes = GatIO.Write(_gat);
                _staging.Put(_gatVirtualPath, gatBytes);

                int count =
                    (_staging.Files != null) ? _staging.Files.Count
                    : 0;

                Set3DStatus($"Saved GAT to staging: {_gatVirtualPath} (staged: {count})");
            }, "SaveToStaging");
        }

        private void ExportZip_Click(object sender, RoutedEventArgs e)
        {
            if (_staging == null)
            {
                MessageBox.Show("Nothing to export: staging is null.", "Export", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Call the compat wrapper (added in Patch 3/3)
                ROMapOverlayEditor.Patching.PatchExporterCompat.ExportPatchZip(_staging);

                var n = _staging.Files.Count;
                MessageBox.Show($"Exported patch ZIP from staging ({n} file(s)).", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ====================================================================
        // MAP LOADING
        // ====================================================================
        
        private async void Open3DMap_Click(object sender, RoutedEventArgs e)
        {
            if (_vfs == null)
            {
                MessageBox.Show("Please open a GRF file first (File → Open GRF).", "3D Map", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var inputDialog = new Window
            {
                Title = "Open 3D Map",
                Width = 400,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this)
            };

            var stack = new StackPanel { Margin = new Thickness(15) };
            stack.Children.Add(new TextBlock { Text = "Enter map name (e.g., prontera, 0@guild_r):", Margin = new Thickness(0, 0, 0, 10) });

            var textBox = new TextBox { Text = string.IsNullOrEmpty(_mapName) ? "prontera" : _mapName, Margin = new Thickness(0, 0, 0, 15) };
            stack.Children.Add(textBox);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var okButton = new Button { Content = "Load", Width = 75, Margin = new Thickness(0, 0, 10, 0) };
            var cancelButton = new Button { Content = "Cancel", Width = 75 };
            
            bool? dialogResult = null;
            okButton.Click += (s, args) => { dialogResult = true; inputDialog.Close(); };
            cancelButton.Click += (s, args) => { dialogResult = false; inputDialog.Close(); };
            textBox.KeyDown += (s, args) => { if (args.Key == Key.Enter) { dialogResult = true; inputDialog.Close(); } };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            stack.Children.Add(buttonPanel);

            inputDialog.Content = stack;
            inputDialog.ShowDialog();

            if (dialogResult != true || string.IsNullOrWhiteSpace(textBox.Text))
                return;

            await Load3DMapSafeAsync(textBox.Text.Trim());
        }

        private async void LoadMapBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_vfs == null)
                {
                    MessageBox.Show("Please open a GRF file first.", "3D Map", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var mapName = MapNameInput?.Text?.Trim() ?? "";
                
                if (string.IsNullOrWhiteSpace(mapName))
                {
                    MessageBox.Show("Enter a map name (e.g., prontera) and click Load.", "3D Map", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                if (mapName.EndsWith(".rsw", StringComparison.OrdinalIgnoreCase))
                    mapName = mapName[..^4];
                
                await Load3DMapSafeAsync(mapName);
            }
            catch (Exception ex)
            {
                // IMPORTANT: prevent WPF from dying silently
                MessageBox.Show($"Load failed:\n{ex}", "3D Map Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Set3DStatus($"Load failed: {ex.Message}");
            }
        }

        public void LoadMap(string mapNameOrPath)
        {
            if (_vfs == null) return;
            _ = Load3DMapSafeAsync(mapNameOrPath);
        }

        private async Task Load3DMapSafeAsync(string mapNameOrBase)
        {
            lock (_rebuildSync)
            {
                _rebuildCts?.Cancel();
                _rebuildCts = new CancellationTokenSource();
            }

            try
            {
                Dispatcher.Invoke(() => { Viewport?.Children.Clear(); });
            }
            catch (Exception)
            {
                // Control may not be loaded yet
            }

            await Task.Run(async () =>
            {
                try
                {
                    await TryLoadMapAsync(mapNameOrBase);
                    Dispatcher.Invoke(() =>
                    {
                        try { RebuildMesh(); }
                        catch (Exception ex)
                        {
                            Set3DStatus($"Rebuild failed: {ex.Message}");
                            System.Diagnostics.Debug.WriteLine($"[Load3DMapSafeAsync] RebuildMesh: {ex}");
                        }
                    });
                }
                catch (Exception ex)
                {
                    Set3DStatus($"Load failed: {ex.Message}");
                    try { Dispatcher.Invoke(() => MessageBox.Show($"Load failed:\n{ex.Message}", "3D Map Load Error", MessageBoxButton.OK, MessageBoxImage.Error)); }
                    catch (Exception) { /* UI may be gone */ }
                }
            });
        }

        private async Task TryLoadMapAsync(string mapNameOrPath)
        {
            var mapName = Path.GetFileNameWithoutExtension(mapNameOrPath);
            
            var loadResult = await Task.Run(() => Rsw3DLoader.LoadForView(_vfs!, mapNameOrPath));
            if (!loadResult.Ok)
                throw new Exception(loadResult.Message ?? "Failed to load map data.");

            var map = loadResult.Map!;

            RswFile? rsw = null;
            GatFile? gat = null;
            GndFileV2? gndMap3D = null;
            string rswErr = "", gatErr = "", gndErr = "";

            try { rsw = RswIO.Read(map.RswBytes); }
            catch (Exception ex) { rswErr = ex.Message; }

            try
            {
                gat = (map.GatBytes != null && map.GatBytes.Length > 0)
                    ? GatIO.Read(map.GatBytes)
                    : new GatFile { Width = 100, Height = 100, Cells = new GatCell[10000] };
            }
            catch (Exception ex)
            {
                gatErr = ex.Message; 
                gat = new GatFile { Width = 100, Height = 100, Cells = new GatCell[10000] };
            }

            try
            {
                if (map.GndBytes == null || map.GndBytes.Length == 0)
                    gndErr = "No GND data.";
                else
                    gndMap3D = GndReaderV2.Read(map.GndBytes);
            }
            catch (Exception ex) { gndErr = ex.Message; }

            _mapName = mapName;
            _rsw = rsw;
            _gndBytes = map.GndBytes;
            _gndMap3D = gndMap3D;
            _gatVirtualPath = map.GatPath;

            if (_staging != null && _staging.TryGet(map.GatPath, out var stagedBytes))
                _gat = GatIO.Read(stagedBytes);
            else if (gat != null)
                _gat = gat;
            else
                _gat = new GatFile { Width = 100, Height = 100, Cells = new GatCell[10000] };

            _terrainModel = null;
            _terrainBuildId++;

            await Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (MapNameInput != null) MapNameInput.Text = mapName;
                    var preview = $"Resolved paths:\nRSW: {map.RswPath}\nGND: {map.GndPath}\nGAT: {map.GatPath}\n";
                    if (rsw != null) preview += $"\nRSW Objects: {rsw.ObjectCount}";
                    if (!string.IsNullOrEmpty(rswErr)) preview += $"\n\nRSW: {rswErr}";
                    if (!string.IsNullOrEmpty(gatErr)) preview += $"\n\nGAT: {gatErr}";
                    if (!string.IsNullOrEmpty(gndErr)) preview += $"\n\nGND parse failed: {gndErr}";
                    if (PreviewText != null) PreviewText.Text = preview;
                    if (StatusLabel != null) StatusLabel.Text = $"Loaded {_mapName}";
                    MapLoaded?.Invoke(this, map.RswPath ?? _mapName);
                }
                catch (Exception ex)
                {
                    if (StatusLabel != null) StatusLabel.Text = $"Load error: {ex.Message}";
                }
            });
        }

        private void Clear3DScene()
        {
            Viewport?.Children.Clear();
            _terrainModel = null;
            _rsw = null;
            _gndMap3D = null;
            _gat = null;
        }

        private void Set3DStatus(string text)
        {
            try
            {
                Dispatcher.Invoke(() => { if (StatusLabel != null) StatusLabel.Text = text; });
            }
            catch (Exception)
            {
                // Dispatcher may be shutting down or control unloaded
            }
        }

        // ====================================================================
        // MESH BUILDING
        // ====================================================================
        
        private void RebuildMesh()
        {
            if (_gndMap3D == null && _gat == null && _rsw == null) return;

            CancellationToken token;
            lock (_rebuildSync)
            {
                _rebuildCts?.Cancel();
                _rebuildCts = new CancellationTokenSource();
                token = _rebuildCts.Token;
            }

            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (token.IsCancellationRequested) return;
                    if (Viewport == null) return;

                    Viewport.Children.Clear();
                    _visualToObj.Clear();
                    // RSW-based lights (BrowEdit-style preview): sun + ambient from RSW header, point lights from RSW light list
                    if (_rsw != null)
                        AddRswLights(_rsw, roScale);
                    else
                        Viewport.Children.Add(new DefaultLights());
                    
                    // 1) Terrain
                    if (ChkTerrainTextures?.IsChecked == true && _gndMap3D != null)
                    {
                        if (_vfs == null)
                        {
                            Set3DStatus("Error: GRF not loaded.");
                        }
                        else if (_terrainModel != null)
                        {
                            Viewport.Children.Add(new ModelVisual3D { Content = _terrainModel });
                        }
                        else
                        {
                            // Build terrain on background thread to avoid blocking UI (stuck loading / 0 FPS).
                            // Uses GndFileV2 (from GndReaderV2) so Cubes carry full Height00/10/01/11; yScale must not be zeroed.
                            Set3DStatus("Building terrain...");
                            var gnd = _gndMap3D;
                            var loadTex = TryLoadBytesFromVfs;
                            const float terrainYScale = 1.0f; // Preserve GND elevation; do not zero or flatten.
                            int buildId = ++_terrainBuildId;
                            Task.Run(() =>
                            {
                                try
                                {
                                    return TerrainBuilderLM.BuildTerrainDataOffThread(gnd, loadTex, terrainYScale);
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[RebuildMesh] BuildTerrainDataOffThread: {ex}");
                                    return null;
                                }
                            }).ContinueWith(t =>
                            {
                                if (t.IsFaulted || t.Result == null)
                                {
                                    Dispatcher.Invoke(() => { if (buildId == _terrainBuildId) Set3DStatus("Terrain build failed."); });
                                    return;
                                }
                                var terrainData = t.Result;
                                Dispatcher.InvokeAsync(() =>
                                {
                                    if (buildId != _terrainBuildId || token.IsCancellationRequested || Viewport == null) return;
                                    try
                                    {
                                        _terrainModel = TerrainBuilderLM.CreateModel3DGroupFromTerrainData(terrainData);
                                        Viewport.Children.Add(new ModelVisual3D { Content = _terrainModel });
                                        Set3DStatus($"Ready — {_mapName}");
                                        if (_needsCameraReset)
                                        {
                                            _needsCameraReset = false;
                                            Viewport.ZoomExtents();
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Set3DStatus($"Terrain failed: {ex.Message}");
                                    }
                                });
                            });
                        }
                    }

                    // 2) GAT Overlay
                    if (ChkGatOverlay?.IsChecked == true && _gat != null)
                    {
                        var gatM = GatMeshBuilder.Build(_gat, GatMeshBuilder.DefaultTypeColor, includeGatOverlay: true);
                        Viewport.Children.Add(new ModelVisual3D { Content = gatM });
                    }

                    // 3) RSW Models
                    if (_rsw != null && _vfs != null && ChkShowRsmModels?.IsChecked == true)
                    {
                        try { AddRsmModels(_rsw, roScale); }
                        catch (Exception ex) { Set3DStatus($"RSM render failed: {ex.Message}"); }
                    }

                    // 4) RSW Markers
                    if (_rsw != null)
                        AddRswMarkers(_rsw, roScale);
                        
                    if (_needsCameraReset)
                    {
                        _needsCameraReset = false;
                        Viewport.ZoomExtents();
                    }
                });
            }
            catch (Exception ex)
            {
                Set3DStatus($"RebuildMesh failed: {ex.Message}");
            }
        }

        // ====================================================================
        // VFS TEXTURE LOADING
        // ====================================================================

        private byte[]? TryLoadBytesFromVfs(string path)
        {
            if (_vfs == null)
            {
                System.Diagnostics.Debug.WriteLine($"[VFS] ERROR: _vfs is NULL!");
                return null;
            }
            
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string normPath = path.Replace('\\', '/').Trim();
            while (normPath.StartsWith("/"))
                normPath = normPath.Substring(1);

            System.Diagnostics.Debug.WriteLine($"[VFS] Loading: '{normPath}'");

            var candidates = new List<string>();

            if (normPath.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
                candidates.Add(normPath);

            candidates.Add($"data/texture/{normPath}");

            string filename = Path.GetFileName(normPath);
            if (!string.Equals(filename, normPath, StringComparison.OrdinalIgnoreCase))
                candidates.Add($"data/texture/{filename}");

            if (!candidates.Contains(normPath))
                candidates.Add(normPath);

            var withExtensions = new List<string>();
            foreach (var c in candidates)
            {
                withExtensions.Add(c);
                if (!c.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) &&
                    !c.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) &&
                    !c.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
                    !c.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                {
                    withExtensions.Add(c + ".bmp");
                    withExtensions.Add(c + ".tga");
                }
            }

            foreach (var candidate in withExtensions.Distinct())
            {
                try
                {
                    if (_vfs.Exists(candidate))
                    {
                        if (_vfs.TryReadAllBytes(candidate, out var bytes, out _))
                        {
                            if (bytes != null && bytes.Length > 0)
                            {
                                System.Diagnostics.Debug.WriteLine($"[VFS] SUCCESS: '{candidate}' ({bytes.Length} bytes)");
                                return bytes;
                            }
                        }
                    }
                }
                catch { }
            }

            System.Diagnostics.Debug.WriteLine($"[VFS] FAILED: '{path}'");
            return null;
        }

        // ====================================================================
        // RSM MODELS & MARKERS
        // ====================================================================

        private void AddRsmModels(RswFile rsw, double worldScale)
        {
            if (_vfs == null) return;
            foreach (var obj in rsw.Objects)
            {
                if (obj is not RswModel model) continue;
                if (string.IsNullOrWhiteSpace(model.FileName)) continue;

                byte[]? bytes = TryReadRsmFromVfs(model.FileName);
                if (bytes == null || bytes.Length < 16)
                    continue;

                var (ok, _, rsm) = RsmParser.TryParse(bytes);
                if (!ok || rsm == null) continue;

                var model3d = RsmMeshBuilder.BuildFromRswModel(rsm, _vfs, model, worldScale);
                if (model3d != null)
                {
                    var visual = new ModelVisual3D { Content = model3d };
                    Viewport.Children.Add(visual);
                    _visualToObj[visual] = model;
                }
            }
        }

        /// <summary>Try to read RSM bytes from VFS using BrowEdit-style path fallbacks (data/model/, data/models/, etc.).</summary>
        private byte[]? TryReadRsmFromVfs(string fileName)
        {
            if (_vfs == null || string.IsNullOrWhiteSpace(fileName)) return null;
            var name = fileName.Replace('\\', '/').Trim();
            var candidates = new List<string>();
            if (name.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
                candidates.Add(name);
            candidates.Add("data/model/" + name);
            candidates.Add("data/models/" + name);
            if (!Path.HasExtension(name))
            {
                candidates.Add("data/model/" + name + ".rsm");
                candidates.Add("data/models/" + name + ".rsm");
            }
            foreach (var path in candidates.Distinct())
            {
                if (_vfs.TryReadAllBytes(path, out var bytes, out _) && bytes != null && bytes.Length >= 16)
                    return bytes;
            }
            return null;
        }

        /// <summary>
        /// Adds RSW-based lights to the viewport (BrowEdit-style limited preview):
        /// ambient + directional sun from RSW header (Longitude/Latitude, Diffuse/Ambient),
        /// plus point lights from each RSW light object in the map.
        /// </summary>
        private void AddRswLights(RswFile rsw, double scale)
        {
            if (Viewport == null) return;

            static float Clamp01(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);
            static Color Vec3ToColor(Vec3 v) => Color.FromScRgb(1f, Clamp01(v.X), Clamp01(v.Y), Clamp01(v.Z));

            // 1) Ambient from RSW header
            var amb = rsw.Light?.Ambient ?? new Vec3(0.3f, 0.3f, 0.3f);
            Viewport.Children.Add(new ModelVisual3D { Content = new AmbientLight(Vec3ToColor(amb)) });

            // 2) Directional sun from RSW header (Longitude/Latitude -> direction, Diffuse -> color)
            var dif = rsw.Light?.Diffuse ?? new Vec3(1f, 1f, 1f);
            int lon = rsw.Light?.Longitude ?? 45;
            int lat = rsw.Light?.Latitude ?? 45;
            double lonRad = lon * Math.PI / 180.0;
            double latRad = lat * Math.PI / 180.0;
            double cosLat = Math.Cos(latRad);
            double sinLat = Math.Sin(latRad);
            double sinLon = Math.Sin(lonRad);
            double cosLon = Math.Cos(lonRad);
            var sunDir = new Vector3D(cosLat * sinLon, sinLat, cosLat * cosLon);
            if (sunDir.LengthSquared < 1e-6) sunDir = new Vector3D(0, 1, 0);
            else sunDir.Normalize();
            Viewport.Children.Add(new ModelVisual3D { Content = new DirectionalLight(Vec3ToColor(dif), sunDir) });

            // 3) Point lights from RSW light list (limited preview)
            foreach (var obj in rsw.Objects)
            {
                if (obj is not RswLight light) continue;
                double x = light.Position.X * scale;
                double y = light.Position.Y * scale;
                double z = light.Position.Z * scale;
                var pl = new PointLight(Vec3ToColor(light.Color), new Point3D(x, y, z));
                double range = Math.Max(light.Range, 1.0);
                pl.LinearAttenuation = 1.0 / range;
                pl.ConstantAttenuation = 0.5;
                pl.QuadraticAttenuation = 0;
                Viewport.Children.Add(new ModelVisual3D { Content = pl });
            }
        }

        private void AddRswMarkers(RswFile rsw, double scale)
        {
            foreach (var obj in rsw.Objects)
            {
                if (obj is RswLight light)
                {
                    var mb = new MeshBuilder(false, false);
                    double r = 0.5 * scale;
                    AddCube(mb, new Point3D(-r, -r, -r), 2 * r);
                    var mesh = mb.ToMesh();
                    var mat = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(255, 255, 200)));
                    var visual = new ModelVisual3D { Content = new GeometryModel3D(mesh, mat) };
                    var tg = new TranslateTransform3D(light.Position.X * scale, light.Position.Y * scale, light.Position.Z * scale);
                    (visual.Content as Model3D)!.Transform = tg;
                    Viewport.Children.Add(visual);
                    _visualToObj[visual] = light;
                }
                else if (obj is RswSound or RswEffect)
                {
                    var mb = new MeshBuilder(false, false);
                    double s = 0.3 * scale;
                    AddCube(mb, new Point3D(-s, -s, -s), 2 * s);
                    var mesh = mb.ToMesh();
                    var mat = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(200, 220, 255)));
                    var visual = new ModelVisual3D { Content = new GeometryModel3D(mesh, mat) };
                    var tg = new TranslateTransform3D(obj.Position.X * scale, obj.Position.Y * scale, obj.Position.Z * scale);
                    (visual.Content as Model3D)!.Transform = tg;
                    Viewport.Children.Add(visual);
                    _visualToObj[visual] = obj;
                }
            }
        }

        private static void AddCube(MeshBuilder mb, Point3D origin, double size)
        {
            double x = origin.X, y = origin.Y, z = origin.Z;
            var p0 = new Point3D(x, y, z);
            var p1 = new Point3D(x + size, y, z);
            var p2 = new Point3D(x + size, y, z + size);
            var p3 = new Point3D(x, y, z + size);
            var p4 = new Point3D(x, y + size, z);
            var p5 = new Point3D(x + size, y + size, z);
            var p6 = new Point3D(x + size, y + size, z + size);
            var p7 = new Point3D(x, y + size, z + size);
            mb.AddQuad(p0, p1, p5, p4);
            mb.AddQuad(p1, p2, p6, p5);
            mb.AddQuad(p2, p3, p7, p6);
            mb.AddQuad(p3, p0, p4, p7);
            mb.AddQuad(p4, p5, p6, p7);
            mb.AddQuad(p3, p2, p1, p0);
        }


        // ====================================================================
        // MOUSE INPUT
        // ====================================================================
        
        private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _inputRouter.OnMouseDown(e.GetPosition(Viewport));
            Viewport.CaptureMouse();
        }

        private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            SafeUi(() =>
            {
                _inputRouter.OnMouseUp();
                Viewport.ReleaseMouseCapture();

                if (e.ChangedButton != MouseButton.Left)
                    return;

                // Paint mode: paint at mouse
                if (_isPaintModeActive)
                {
                    TryPaintAtMouse(e.GetPosition(Viewport));
                    return;
                }

                // Selection mode: pick visual -> map to RSW object
                // Use HelixToolkit hit test; DO NOT access PointHit (differs across Helix versions).
                var hits = Viewport3DHelper.FindHits(Viewport.Viewport, e.GetPosition(Viewport));
                if (hits == null || hits.Count == 0)
                {
                    _selectedObject = null;
                    UpdateInspectorUi();
                    return;
                }

                // Prefer first hit that maps to a Visual3D we created
                foreach (var h in hits)
                {
                    if (h?.Visual is Visual3D v && _visualToObj.TryGetValue(v, out var obj))
                    {
                        _selectedObject = obj;
                        UpdateInspectorUi();
                        Set3DStatus($"Selected: {obj.GetType().Name}");
                        return;
                    }
                }

                // If nothing matched our visuals, clear selection safely
                _selectedObject = null;
                UpdateInspectorUi();
            }, "Viewport_MouseUp");
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            _inputRouter.OnMouseMove(e.GetPosition(Viewport), e);
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            _inputRouter.OnMouseWheel(e.Delta);
        }

        private void TryPaintAtMouse(Point mouse)
        {
            SafeUi(() =>
            {
                if (!_isPaintModeActive || _gat == null) return;

                // Use Tools.HelixHitCompat via alias
                if (!HelixHit.TryGetFirstHitPoint(Viewport.Viewport, mouse, out var p))
                    return;

                int cellX = (int)Math.Floor(p.X / GatMeshBuilder.TileSize);
                int cellY = (int)Math.Floor(p.Z / GatMeshBuilder.TileSize);

                int r = (int)RadiusSlider.Value;
                var type = (GatCellType)(TypeCombo.SelectedItem ?? GatCellType.Walkable);

                GatPainter.PaintCircle(_gat, cellX, cellY, r, type);
                RebuildMesh();
                PickInfo.Text = $"Painted at ({cellX},{cellY})";
            }, "TryPaintAtMouse");
        }

        private void ApplyCameraToRenderer()
        {
            double yawRad = _cam.Yaw * Math.PI / 180.0;
            double pitchRad = _cam.Pitch * Math.PI / 180.0;
            double cosPitch = Math.Cos(pitchRad);
            double x = _cam.TargetX + _cam.Distance * cosPitch * Math.Cos(yawRad);
            double y = _cam.TargetY + _cam.Distance * Math.Sin(pitchRad);
            double z = _cam.TargetZ + _cam.Distance * cosPitch * Math.Sin(yawRad);

            if (Viewport.Camera is PerspectiveCamera cam)
            {
                cam.Position = new Point3D(x, y, z);
                cam.LookDirection = new Vector3D(_cam.TargetX - x, _cam.TargetY - y, _cam.TargetZ - z);
                cam.UpDirection = new Vector3D(0, 1, 0);
            }
        }
    }
}