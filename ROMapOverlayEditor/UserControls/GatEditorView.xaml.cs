// ============================================================================
// GatEditorView.xaml.cs - FIXED VERSION with Paint GAT Toggle
// ============================================================================
// TARGET: F:\2026 PROJECT\ROMapOverlayEditor\ROMapOverlayEditor\UserControls\GatEditorView.xaml.cs
// ACTION: REPLACE ENTIRE FILE
// ============================================================================
// CHANGES:
//   1. Paint GAT Cells is now a toggle button (PaintModeToggle)
//   2. Paint controls enable/disable based on toggle state
//   3. GAT overlay auto-enables when paint mode is active
//   4. Removed old RadioButton tool mode selection
// ============================================================================

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;  // For ToggleButton
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
using ROMapOverlayEditor.Vfs;
using ROMapOverlayEditor.Map3D;

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
        private GatFile _gat = new();
        private RswFile? _rsw;
        private string _gatVirtualPath = "";
        
        private ROMapOverlayEditor.Map3D.GndFile? _gndMap3D;
        private byte[]? _gndBytes;

        private CancellationTokenSource? _loadCts;
        private readonly object _loadLock = new();

        // Camera: Right=Orbit, Middle=Pan, Wheel=Zoom
        private readonly ROMapOverlayEditor.ThreeD.BrowEditCameraController _cam = new();
        private readonly EditorInputRouter _inputRouter;

        private int _rebuildSeq = 0;
        private bool _needsCameraReset = true;
        
        // NEW: Paint mode state (controlled by toggle button)
        private bool _isPaintModeActive = false;

        // ====================================================================
        // CONSTRUCTOR
        // ====================================================================
        
        public GatEditorView()
        {
            InitializeComponent();
            _inputRouter = new EditorInputRouter(_cam, ApplyCameraToRenderer);
            
            // Wire up viewport events
            Viewport.MouseDown += Viewport_MouseDown;
            Viewport.MouseUp += Viewport_MouseUp;
            Viewport.MouseMove += Viewport_MouseMove;
            Viewport.MouseWheel += Viewport_MouseWheel;
            
            // Populate GAT cell type dropdown
            TypeCombo.ItemsSource = Enum.GetValues(typeof(GatCellType)).Cast<GatCellType>();
            TypeCombo.SelectedItem = GatCellType.NotWalkable;

            // Wire up radius slider label update
            RadiusSlider.ValueChanged += (_, _) => RadiusLabel.Text = $"Brush Radius: {(int)RadiusSlider.Value}";
            
            PreviewText.Text = "Enter a map name and click Load to see resolved paths and header info.";
        }

        // ====================================================================
        // PAINT MODE TOGGLE HANDLERS (NEW)
        // ====================================================================
        
        /// <summary>
        /// Called when the Paint Mode toggle button is checked (enabled).
        /// Enables paint controls and shows GAT overlay.
        /// </summary>
        private void PaintModeToggle_Checked(object sender, RoutedEventArgs e)
        {
            _isPaintModeActive = true;
            
            // Enable paint controls panel
            if (PaintControlsPanel != null)
            {
                PaintControlsPanel.IsEnabled = true;
                PaintControlsPanel.Opacity = 1.0;
            }
            
            // Update toggle button text
            if (PaintModeToggle != null)
            {
                PaintModeToggle.Content = "🎨 Paint Mode ACTIVE";
            }
            
            // Auto-enable GAT overlay when painting (so user can see what they're painting)
            if (ChkGatOverlay != null && ChkGatOverlay.IsChecked != true)
            {
                ChkGatOverlay.IsChecked = true;
                // This will trigger ViewOptionChanged which rebuilds the mesh
            }
            
            // Update editor state for compatibility with existing code
            EditorState.Current.ActiveTool = EditorTool.PaintGat_Walkable;
            
            PickInfo.Text = "Paint mode enabled. Left-click on terrain to paint cells.";
        }
        
        /// <summary>
        /// Called when the Paint Mode toggle button is unchecked (disabled).
        /// Disables paint controls.
        /// </summary>
        private void PaintModeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _isPaintModeActive = false;
            
            // Disable paint controls panel
            if (PaintControlsPanel != null)
            {
                PaintControlsPanel.IsEnabled = false;
                PaintControlsPanel.Opacity = 0.5;
            }
            
            // Update toggle button text
            if (PaintModeToggle != null)
            {
                PaintModeToggle.Content = "🎨 Enable Paint Mode";
            }
            
            // Update editor state
            EditorState.Current.ActiveTool = EditorTool.Select;
            
            PickInfo.Text = "Paint mode disabled. Click to select cells.";
        }

        // ====================================================================
        // INITIALIZATION
        // ====================================================================
        
        // Delegate to open GRF browser from MainWindow
        private Func<string?>? _browseGrf;

        public void Initialize(CompositeVfs vfs, EditStaging staging, Func<string?> browseGrf)
        {
            _vfs = vfs;
            _staging = staging;
            _browseGrf = browseGrf;
        }

        // ====================================================================
        // MAP LOADING
        // ====================================================================
        
        private async void Open3DMap_Click(object sender, RoutedEventArgs e)
        {
            if (_vfs == null || _browseGrf == null)
            {
                MessageBox.Show("Please open a GRF file first.", "3D Map", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var rswPath = _browseGrf();
            if (string.IsNullOrWhiteSpace(rswPath)) return;

            if (!rswPath.EndsWith(".rsw", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Please select an .rsw file.", "3D Map", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await Load3DMapSafeAsync(rswPath);
        }

        public void LoadMap(string mapNameOrPath)
        {
            if (_vfs == null) return;
            _ = Load3DMapSafeAsync(mapNameOrPath);
        }

        private async Task TryLoadMapAsync(string mapNameOrPath)
        {
            var mapName = System.IO.Path.GetFileNameWithoutExtension(mapNameOrPath);
            
            // 1) Load bytes from VFS
            var loadResult = Rsw3DLoader.LoadForView(_vfs!, mapNameOrPath);
            if (!loadResult.Ok)
                throw new Exception(loadResult.Message ?? "Failed to load map data.");

            var map = loadResult.Map!;

            // 2) Parse RSW, GAT, GND
            RswFile? rsw = null;
            GatFile? gat = null;
            ROMapOverlayEditor.Map3D.GndFile? gndMap3D = null;
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
                using var gm = new MemoryStream(map.GndBytes); 
                gndMap3D = GndReader.Read(gm); 
            }
            catch (Exception ex) { gndErr = ex.Message; }

            // 3) Update state
            _mapName = mapName;
            _rsw = rsw;
            _gndBytes = map.GndBytes;
            _gndMap3D = gndMap3D;
            _gatVirtualPath = map.GatPath;

            // Handle Staged GAT
            if (_staging != null && _staging.TryGet(map.GatPath, out var stagedBytes))
            {
                _gat = GatIO.Read(stagedBytes);
            }
            else if (gat != null)
            {
                _gat = gat;
            }
            else
            {
                _gat = new GatFile { Width = 100, Height = 100, Cells = new GatCell[10000] };
            }

            // Update UI text (Preview)
            await Dispatcher.InvokeAsync(() =>
            {
                 MapNameInput.Text = mapName;
                 var preview = $"Resolved paths:\nRSW: {map.RswPath}\nGND: {map.GndPath}\nGAT: {map.GatPath}\n";
                 if (rsw != null) preview += $"\nRSW Objects: {rsw.ObjectCount}";
                 if (!string.IsNullOrEmpty(rswErr)) preview += $"\n\nRSW: {rswErr}";
                 if (!string.IsNullOrEmpty(gatErr)) preview += $"\n\nGAT: {gatErr}";
                 if (!string.IsNullOrEmpty(gndErr)) preview += $"\n\nGND parse failed: {gndErr}";
                 PreviewText.Text = preview;
                 
                 StatusLabel.Text = (_gat != null && map.GatBytes != null) ? $"Loaded {_mapName}" : $"Loaded {_mapName} (No GAT)";
            });
        }

        private async Task Load3DMapSafeAsync(string mapNameOrBase)
        {
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            var ct = _loadCts.Token;

            try
            {
                Set3DStatus($"Loading '{mapNameOrBase}' ...");

                await Task.Run(async () =>
                {
                    ct.ThrowIfCancellationRequested();
                    await TryLoadMapAsync(mapNameOrBase);
                    ct.ThrowIfCancellationRequested();
                }, ct);

                _needsCameraReset = true;
                RebuildMesh();
                Set3DStatus($"Loaded '{mapNameOrBase}' OK.");
            }
            catch (OperationCanceledException)
            {
                Set3DStatus("Load canceled.");
            }
            catch (System.Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    try { Viewport.Children.Clear(); }
                    catch { }
                });

                Set3DStatus($"Load failed: {ex.GetType().Name}: {ex.Message}");
                MessageBox.Show(
                    $"Failed to load 3D map '{mapNameOrBase}'.\n\n{ex.GetType().Name}: {ex.Message}",
                    "3D Map Load Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void Set3DStatus(string text)
        {
            Dispatcher.Invoke(() => { StatusLabel.Text = text; });
        }

        private async void LoadMapBtn_Click(object sender, RoutedEventArgs e)
        {
            string mapName = MapNameInput.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(mapName)) return;
            await Load3DMapSafeAsync(mapName);
        }

        // ====================================================================
        // MESH BUILDING
        // ====================================================================
        
        private void Rebuild_Click(object sender, RoutedEventArgs e) => RebuildMeshSafe();

        private async void RebuildMeshSafe()
        {
            int seq = ++_rebuildSeq;

            try
            {
                var map = new { GndBytes = _gndBytes, GatBytes = _gat != null ? GatIO.Write(_gat) : null };
                var vfs = _vfs;

                if (vfs == null) return;

                var result = await System.Threading.Tasks.Task.Run(() =>
                {
                    if (seq != _rebuildSeq) return null;

                    var models = new List<Model3D>();

                    // Lights
                    var lightGroup = new Model3DGroup();
                    lightGroup.Children.Add(new AmbientLight(System.Windows.Media.Color.FromRgb(90, 90, 90)));
                    lightGroup.Children.Add(new DirectionalLight(System.Windows.Media.Color.FromRgb(255, 255, 255), new Vector3D(-0.35, -1.0, -0.25)));
                    models.Add(lightGroup);

                    // 1) Terrain from GND (TEXTURED)
                    if (ChkTerrainTextures?.IsChecked == true && map.GndBytes != null)
                    {
                        try
                        {
                            var gnd = ROMapOverlayEditor.ThreeD.GndV2Parser.Parse(map.GndBytes);
                            var atlas = ROMapOverlayEditor.ThreeD.TextureAtlasBuilder.BuildAtlas(vfs, gnd.Textures);
                            var terrainModels = ROMapOverlayEditor.ThreeD.GndTexturedTerrainBuilder.Build(gnd, atlas, chunkSize: 32);
                            models.AddRange(terrainModels);
                        }
                        catch (Exception ex)
                        {
                            models.Add(MakeDebugTextModel($"GND textured build failed: {ex.Message}"));
                        }
                    }

                    // 2) GAT overlay
                    if (ChkGatOverlay?.IsChecked == true && _gat != null && _gat.Width > 0 && _gat.Height > 0)
                    {
                        try
                        {
                            var gatOverlay = ROMapOverlayEditor.Gat.GatMeshBuilder.Build(_gat, ROMapOverlayEditor.Gat.GatMeshBuilder.DefaultTypeColor, includeGatOverlay: true);
                            if (gatOverlay != null)
                                models.Add(gatOverlay);
                        }
                        catch { }
                    }

                    if (seq != _rebuildSeq) return null;
                    return models;
                });

                if (seq != _rebuildSeq || result == null) return;

                Viewport.Children.Clear();
                foreach (var m in result)
                {
                    if (m is Model3DGroup mg)
                        Viewport.Children.Add(new ModelVisual3D { Content = mg });
                    else
                        Viewport.Children.Add(new ModelVisual3D { Content = m });
                }

                // RSW markers
                if (_rsw != null)
                {
                    try
                    {
                        var rswVisuals = new System.Collections.Generic.List<System.Windows.Media.Media3D.Visual3D>();
                        AddModelPlaceholders(_rsw, rswVisuals);
                        AddRswMarkers(_rsw, rswVisuals);
                        foreach (var v in rswVisuals)
                            Viewport.Children.Add(v);
                    }
                    catch (Exception ex)
                    {
                        PreviewText.Text = (PreviewText.Text ?? "") + $"\n\nRSW markers failed: {ex.Message}";
                    }
                }

                // Fallback grid
                if (Viewport.Children.Count == 0)
                {
                    var fallback = new HelixToolkit.Wpf.GridLinesVisual3D
                    {
                        Width = 500, Length = 500, MajorDistance = 50, MinorDistance = 10, Thickness = 1
                    };
                    Viewport.Children.Add(fallback);
                }

                // Camera reset only on first load
                if (_needsCameraReset)
                {
                    _needsCameraReset = false;
                    if (Viewport.CameraController != null)
                        Viewport.CameraController.ResetCamera();
                }
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Rebuild failed: {ex.Message}";
            }
        }

        private Model3D MakeDebugTextModel(string message)
        {
            return new Model3DGroup();
        }

        private void RebuildMesh()
        {
            var newChildren = new System.Collections.Generic.List<System.Windows.Media.Media3D.Visual3D>();
            newChildren.Add(new DefaultLights());
            bool addedSomething = false;

            // 1) Terrain
            if (ChkTerrainTextures?.IsChecked == true && (_gndMap3D != null || _gndBytes != null))
            {
                bool terrainDone = false;

                if (_gndMap3D != null && _vfs != null)
                {
                    try
                    {
                        var terrain = TerrainBuilder.BuildTexturedTerrain(_gndMap3D, TryLoadBytesFromVfs, 1.0f);
                        foreach (var vis in terrain.TerrainPieces)
                            newChildren.Add(vis);
                        addedSomething = true;
                        terrainDone = true;
                    }
                    catch { }
                }

                if (!terrainDone && _gndBytes != null && _gndBytes.Length > 0)
                {
                    var (ok, msg, parsed) = ROMapOverlayEditor.ThreeD.GndParser.TryParse(_gndBytes);
                    if (ok && parsed != null)
                    {
                        var terrain = ROMapOverlayEditor.ThreeD.GndHelixModelBuilder.BuildSolidTerrain(parsed);
                        newChildren.Add(new ModelVisual3D { Content = terrain });
                        addedSomething = true;
                    }
                    else
                        PreviewText.Text = (PreviewText.Text ?? "") + $"\n\nGND parse failed: {msg}";
                }
            }

            // 2) GAT overlay
            if (ChkGatOverlay?.IsChecked == true)
            {
                if (_gat == null || _gat.Width <= 0 || _gat.Height <= 0 || _gat.Cells == null || _gat.Cells.Length == 0)
                {
                    _gat = new GatFile { Width = 100, Height = 100, Cells = new GatCell[100 * 100] };
                }

                var gatModel = ROMapOverlayEditor.Gat.GatMeshBuilder.Build(_gat, ROMapOverlayEditor.Gat.GatMeshBuilder.DefaultTypeColor, includeGatOverlay: true);
                newChildren.Add(new ModelVisual3D { Content = gatModel });
                addedSomething = true;
            }

            // 3) RSW markers
            if (_rsw != null)
            {
                try
                {
                    AddModelPlaceholders(_rsw, newChildren);
                    AddRswMarkers(_rsw, newChildren);
                }
                catch (Exception ex)
                {
                    PreviewText.Text = (PreviewText.Text ?? "") + $"\n\nRSW markers failed: {ex.Message}";
                }
            }

            // Fallback
            if (!addedSomething)
            {
                var fallback = new HelixToolkit.Wpf.GridLinesVisual3D
                {
                    Width = 500, Length = 500, MajorDistance = 50, MinorDistance = 10, Thickness = 1
                };
                newChildren.Add(fallback);
            }

            Viewport.Children.Clear();
            foreach (var v in newChildren)
                Viewport.Children.Add(v);

            // Only reset camera on initial load
            if (_needsCameraReset)
            {
                _needsCameraReset = false;
                _cam.ResetDefault();
                ApplyCameraToRenderer();
                Viewport.ZoomExtents();
            }
        }

        // ====================================================================
        // RSW MARKERS
        // ====================================================================
        
        private void AddModelPlaceholders(RswFile rsw, System.Collections.Generic.List<System.Windows.Media.Media3D.Visual3D> target)
        {
            double scale = 1.0 / 10.0;
            foreach (var o in rsw.Objects)
            {
                if (o.ObjectType != 1) continue;
                var mesh = new MeshBuilder();
                mesh.AddBox(
                    new Point3D(o.Position.X * scale, o.Position.Y * scale, o.Position.Z * scale),
                    0.5, 2.0, 0.5);
                var geom = new GeometryModel3D
                {
                    Geometry = mesh.ToMesh(),
                    Material = new DiffuseMaterial(new SolidColorBrush(Colors.Gold))
                };
                target.Add(new ModelVisual3D { Content = geom });
            }
        }

        private void AddRswMarkers(RswFile rsw, System.Collections.Generic.List<System.Windows.Media.Media3D.Visual3D> target)
        {
            double scale = 0.5;

            foreach (var obj in rsw.Objects)
            {
                if (obj.ObjectType == 1) continue;

                var mesh = new MeshBuilder();
                Color c = Colors.White;
                Vec3 p = new Vec3(0,0,0);
                string label = "";
                double size = 1.0;

                switch (obj)
                {
                    case RswLight l:
                        c = Colors.Yellow; p = l.Position; label = l.Name; size = 1.0;
                        mesh.AddSphere(new Point3D(p.X * scale, p.Y * scale, p.Z * scale), size);
                        break;
                    case RswSound s:
                        c = Colors.Orange; p = s.Position; label = s.Name; size = 1.0;
                        mesh.AddBox(new Point3D(p.X * scale, p.Y * scale, p.Z * scale), size, size, size);
                        break;
                    case RswEffect e:
                        c = Colors.Red; p = e.Position; label = e.Name; size = 1.0;
                        mesh.AddCone(new Point3D(p.X*scale, (p.Y*scale)+2, p.Z*scale), new Point3D(p.X*scale, p.Y*scale, p.Z*scale), size, true, 12);
                        break;
                }

                if (obj is RswLight || obj is RswSound || obj is RswEffect)
                {
                    var geom = new GeometryModel3D { Geometry = mesh.ToMesh(), Material = new DiffuseMaterial(new SolidColorBrush(c)) };
                    target.Add(new ModelVisual3D { Content = geom });
                    
                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        var text = new BillboardTextVisual3D
                        {
                            Text = label,
                            Position = new Point3D(p.X * scale, (p.Y * scale) + size + 1.5, p.Z * scale),
                            Foreground = new SolidColorBrush(c),
                            Background = Brushes.Transparent,
                            FontSize = 10 
                        };
                        target.Add(text);
                    }
                }
            }
        }

        // ====================================================================
        // STAGING & EXPORT
        // ====================================================================
        
        private void SaveToStaging_Click(object sender, RoutedEventArgs e)
        {
            if (_staging == null) return;
            var bytes = GatIO.Write(_gat);
            _staging.Put(_gatVirtualPath, bytes);
            StatusLabel.Text = "Saved to Staging.";
        }

        private void ExportZip_Click(object sender, RoutedEventArgs e)
        {
            if (_staging == null) return;
            SaveToStaging_Click(sender, e);

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "ZIP Patch (*.zip)|*.zip",
                FileName = $"{_mapName}_patch.zip"
            };
            if (dlg.ShowDialog() != true) return;

            var manifest = $"ROMapOverlayEditor Patch\nMap: {_mapName}\nGenerated: {DateTime.Now}\n";
            PatchWriter.WriteZip(dlg.FileName, _staging.Files, manifest);
        }

        // ====================================================================
        // CAMERA & VIEW
        // ====================================================================
        
        private void ResetView_Click(object sender, RoutedEventArgs e)
        {
            if (_gndMap3D != null)
            {
                double cx = (_gndMap3D.Width * 0.5) * _gndMap3D.TileScale;
                double cz = (_gndMap3D.Height * 0.5) * _gndMap3D.TileScale;
                double dist = Math.Max(_gndMap3D.Width * _gndMap3D.TileScale, _gndMap3D.Height * _gndMap3D.TileScale) * 1.25;

                _cam.ResetEvenOut(suggestedDistance: dist, tx: cx, ty: 40, tz: cz);
                ApplyCameraToRenderer();
                Viewport.ZoomExtents();
                return;
            }

            _cam.ResetEvenOut(suggestedDistance: 220);
            ApplyCameraToRenderer();
            Viewport.ZoomExtents();
        }

        private void ViewOptionChanged(object sender, RoutedEventArgs e)
        {
            RebuildMeshSafe();
        }

        private byte[]? TryLoadBytesFromVfs(string path)
        {
            if (_vfs == null) return null;
            return _vfs.TryReadAllBytes(path, out var b, out _) ? b : null;
        }

        private void CameraSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (CameraSpeedSlider == null || RotateSensSlider == null || PanSensSlider == null || ZoomSensSlider == null) return;
            var v = CameraSpeedSlider.Value;
            RotateSensSlider.Value = v;
            PanSensSlider.Value = v;
            ZoomSensSlider.Value = v;
        }

        private void SensSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (RotateSensSlider == null || PanSensSlider == null || ZoomSensSlider == null) return;
            var st = EditorState.Current;
            st.RotateSensitivity = RotateSensSlider.Value;
            st.PanSensitivity = PanSensSlider.Value;
            st.ZoomSensitivity = ZoomSensSlider.Value;
        }

        // ====================================================================
        // MOUSE INPUT (Viewport interaction)
        // ====================================================================
        
        private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _inputRouter.OnMouseDown(e.GetPosition(Viewport));
            Viewport.CaptureMouse();
        }

        private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _inputRouter.OnMouseUp();
            Viewport.ReleaseMouseCapture();
            
            // Only process left-clicks (right/middle/wheel are for camera)
            if (e.ChangedButton != MouseButton.Left) return;
            
            // Raycast to find clicked cell
            var pos = e.GetPosition(Viewport);
            var hits = Viewport3DHelper.FindHits(Viewport.Viewport, pos);
            if (hits == null || hits.Count == 0) return;

            var hp = hits[0].Position;
            int x = (int)Math.Floor(hp.X / GatMeshBuilder.TileSize);
            int y = (int)Math.Floor(hp.Z / GatMeshBuilder.TileSize);

            if (_gat == null || !_gat.InBounds(x, y)) return;

            // PAINT MODE: paint the cell if toggle is active
            if (_isPaintModeActive)
            {
                var sel = (GatCellType)(TypeCombo.SelectedItem ?? GatCellType.NotWalkable);
                int r = (int)RadiusSlider.Value;
                GatPainter.PaintCircle(_gat, x, y, r, sel);
                RebuildMeshSafe();
                PickInfo.Text = $"Painted cell ({x},{y}) = {sel}";
            }
            else
            {
                // SELECT MODE: just show cell info
                var cell = _gat.Cells[y * _gat.Width + x];
                PickInfo.Text = $"Selected: ({x},{y}) Type={cell.Type}";
            }
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            _inputRouter.OnMouseMove(e.GetPosition(Viewport), e);
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            _inputRouter.OnMouseWheel(e.Delta);
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
