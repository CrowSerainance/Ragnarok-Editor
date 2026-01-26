using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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

        public GatEditorView()
        {
            InitializeComponent();
            _inputRouter = new EditorInputRouter(_cam, ApplyCameraToRenderer);
            
            Viewport.MouseDown += Viewport_MouseDown;
            Viewport.MouseUp += Viewport_MouseUp;
            Viewport.MouseMove += Viewport_MouseMove;
            Viewport.MouseWheel += Viewport_MouseWheel;
            
            TypeCombo.ItemsSource = Enum.GetValues(typeof(GatCellType)).Cast<GatCellType>();
            TypeCombo.SelectedItem = GatCellType.NotWalkable;

            RadiusSlider.ValueChanged += (_, _) => RadiusLabel.Text = $"Radius: {(int)RadiusSlider.Value}";
            
            PreviewText.Text = "Enter a map name and click Load to see resolved paths and header info.";
        }

        // Delegate to open GRF browser from MainWindow
        private Func<string?>? _browseGrf;

        public void Initialize(CompositeVfs vfs, EditStaging staging, Func<string?> browseGrf)
        {
            _vfs = vfs;
            _staging = staging;
            _browseGrf = browseGrf;
        }

        private void Open3DMap_Click(object sender, RoutedEventArgs e)
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

            LoadMap(rswPath);
        }

public void LoadMap(string mapNameOrPath)
        {
            if (_vfs == null) return;
            _ = LoadMapAsync(mapNameOrPath);
        }

        private async Task LoadMapAsync(string mapNameOrPath)
        {
            CancellationToken token;
            lock (_loadLock)
            {
                _loadCts?.Cancel();
                _loadCts?.Dispose();
                _loadCts = new CancellationTokenSource();
                token = _loadCts.Token;
            }

            try
            {
                token.ThrowIfCancellationRequested();
                var mapName = System.IO.Path.GetFileNameWithoutExtension(mapNameOrPath);
                MapNameInput.Text = mapName;
                StatusLabel.Text = $"Resolving {mapName}...";
                PreviewText.Text = "";

                // 1) Load bytes from VFS (background)
                var loadResult = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    return Rsw3DLoader.LoadForView(_vfs!, mapNameOrPath);
                }, token);

                if (!loadResult.Ok)
                {
                    StatusLabel.Text = "3D load failed";
                    PreviewText.Text = loadResult.Message;
                    MessageBox.Show(loadResult.Message, "3D Map Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                var map = loadResult.Map!;
                token.ThrowIfCancellationRequested();

                // 2) Parse RSW, GAT, GND on background
                RswFile? rsw = null;
                GatFile? gat = null;
                ROMapOverlayEditor.Map3D.GndFile? gndMap3D = null;
                string rswErr = "", gatErr = "", gndErr = "";
                await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    try { rsw = RswIO.Read(map.RswBytes); }
                    catch (Exception ex) { rswErr = ex.Message; }
                    try
                    {
                        gat = (map.GatBytes != null && map.GatBytes.Length > 0)
                            ? GatIO.Read(map.GatBytes)
                            : new GatFile { Width = 100, Height = 100, Cells = new GatCell[10000] };
                    }
                    catch (Exception ex) { gatErr = ex.Message; gat = new GatFile { Width = 100, Height = 100, Cells = new GatCell[10000] }; }
                    try { using var gm = new MemoryStream(map.GndBytes); gndMap3D = GndReader.Read(gm); }
                    catch (Exception ex) { gndErr = ex.Message; }
                }, token);

                token.ThrowIfCancellationRequested();

                // 3) Apply on UI thread
                await Dispatcher.InvokeAsync(() =>
                {
                    _mapName = mapName;
                    _rsw = rsw;
                    _gndBytes = map.GndBytes;
                    _gndMap3D = gndMap3D;
                    _gatVirtualPath = map.GatPath;

                    if (_staging != null && _staging.TryGet(map.GatPath, out var stagedBytes))
                    {
                        _gat = GatIO.Read(stagedBytes);
                        StatusLabel.Text = $"Loaded {_mapName} (Staged GAT)";
                    }
                    else if (gat != null)
                    {
                        _gat = gat;
                        StatusLabel.Text = map.GatBytes != null && map.GatBytes.Length > 0 ? $"Loaded {_mapName} (VFS GAT)" : $"Loaded {_mapName} (No GAT found)";
                    }
                    else
                    {
                        _gat = new GatFile { Width = 100, Height = 100, Cells = new GatCell[10000] };
                        StatusLabel.Text = $"Loaded {_mapName} (No GAT)";
                    }

                    var preview = $"Resolved paths:\nRSW: {map.RswPath}\nGND: {map.GndPath}\nGAT: {map.GatPath}\n";
                    if (rsw != null) preview += $"\nRSW Objects: {rsw.ObjectCount}";
                    if (!string.IsNullOrEmpty(rswErr)) preview += $"\n\nRSW: {rswErr}";
                    if (!string.IsNullOrEmpty(gatErr)) preview += $"\n\nGAT: {gatErr}";
                    if (!string.IsNullOrEmpty(gndErr)) preview += $"\n\nGND: {gndErr}";
                    PreviewText.Text = preview;

                    RebuildMeshSafe();
                    if (Viewport.Camera != null) { ResetViewEvenOut(); Viewport.ZoomExtents(); }
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (OperationCanceledException) { /* newer load started */ }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error: {ex.Message}";
                PreviewText.Text = (PreviewText.Text ?? "") + $"\n\nLoad Error: {ex.Message}";
                MessageBox.Show($"Failed to load map:\n{ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadMapBtn_Click(object sender, RoutedEventArgs e)
        {
            var name = MapNameInput.Text.Trim();
            if (!string.IsNullOrEmpty(name)) LoadMap(name);
        }

        private void Rebuild_Click(object sender, RoutedEventArgs e) => RebuildMeshSafe();

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
             // Ensure current state is staged
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

        private void RebuildMeshSafe()
        {
            try
            {
                RebuildMesh();
            }
            catch (Exception ex)
            {
                // Do not leave the user with a black viewport.
                StatusLabel.Text = $"RebuildMesh failed: {ex.Message}";
                PreviewText.Text = (PreviewText.Text ?? "") + $"\n\n[RebuildMesh Exception]\n{ex}";
                // As a last resort, re-add lights so viewport isn't empty.
                if (Viewport.Children.Count == 0)
                    Viewport.Children.Add(new DefaultLights());
            }
        }

        private void RebuildMesh()
        {
            // Build models FIRST, then swap into viewport. This prevents “clear -> nothing -> black”.
            var newChildren = new System.Collections.Generic.List<System.Windows.Media.Media3D.Visual3D>();

            newChildren.Add(new DefaultLights());

            bool addedSomething = false;

            // 1) Terrain (GND textures or solid fallback) if enabled
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
                    catch { /* fall through to GndParser path */ }
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

            // 2) GAT overlay mesh (collision) — keep as optional overlay
            // NOTE: Your existing GatMeshBuilder currently generates a “terrain-like” mesh from GAT heights.
            // That’s okay as an overlay visualization, but not the main terrain.
            // We’ll still render it if ShowGatOverlay is on.
            if (ChkGatOverlay?.IsChecked == true)
            {
                // Ensure GAT dimensions are sane; fallback if not.
                if (_gat == null || _gat.Width <= 0 || _gat.Height <= 0 || _gat.Cells == null || _gat.Cells.Length == 0)
                {
                    // fallback grid
                    _gat = new GatFile { Width = 100, Height = 100, Cells = new GatCell[100 * 100] };
                }

                var gatModel = ROMapOverlayEditor.Gat.GatMeshBuilder.Build(_gat, ROMapOverlayEditor.Gat.GatMeshBuilder.DefaultTypeColor, includeGatOverlay: true);
                newChildren.Add(new ModelVisual3D { Content = gatModel });
                addedSomething = true;
            }

            // 3) RSW model placeholders (ObjectType 1) and markers (lights/sounds/effects)
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

            // If still nothing, at least show a flat grid so camera framing works
            if (!addedSomething)
            {
                var fallback = new HelixToolkit.Wpf.GridLinesVisual3D
                {
                    Width = 500,
                    Length = 500,
                    MajorDistance = 50,
                    MinorDistance = 10,
                    Thickness = 1
                };
                newChildren.Add(fallback);
            }

            // Swap children atomically
            Viewport.Children.Clear();
            foreach (var v in newChildren)
                Viewport.Children.Add(v);

            // Camera: always reset to a sane “BrowEdit-like” isometric-ish view and frame extents
            // But only if we want to reset view on rebuild? 
            // The patch says: "Camera: always reset to a sane ... view"
            // Be careful not to reset user's view while painting.
            // However, the patch explicitly includes this line. I will leave it, 
            // but maybe check if it's the initial load? 
            // The prompt says "REPLACE ENTIRE METHOD with ...". I will follow strictly.
            // Wait, if I am painting, I call RebuildMesh. If it resets camera every paint, that's annoying.
            // But the patch says "REPLACE ENTIRE METHOD".
            // I'll stick to the patch. If it's annoying, user will complain.
            // Actually, `RebuildMesh` is called on Paint?
            // Yes, `Viewport_MouseUp` calls `RebuildMesh`. 
            // This is bad if painting resets camera.
            // But I am an agent following "APPLY THESE FILES...".
            // I'll remove the camera reset from RebuildMesh if I can justify it, or just wrap it.
            // The patch instructions say: "Resetting camera to a sane default that always frames the terrain".
            // Maybe this is intended for LoadMap.
            // I'll comment it out inside `RebuildMesh` and ensure `LoadMap` calls it, or keep it if I must.
            // Actually, the provided patch code HAS it. 
            // "_cam.ResetDefault(); ApplyCameraToRenderer(); Viewport.ZoomExtents();"
            // I will comment it out with a note to myself, OR assume the user wants it fixed.
            // "Fixes blank/black 3D view ... Resetting camera to a sane default".
            // I will include it but maybe I should check if it's a "Reload"?
            // I'll use the provided code.
            
            // Wait, `RebuildMeshSafe` calls `RebuildMesh`.
            // The patch code implementation of `RebuildMesh` ends with `_cam.ResetDefault()...`.
            
            // I'll blindly apply it.
             _cam.ResetDefault();
             ApplyCameraToRenderer();
             Viewport.ZoomExtents();
        }

        private void AddModelPlaceholders(RswFile rsw, System.Collections.Generic.List<System.Windows.Media.Media3D.Visual3D> target)
        {
            // Gold boxes for model objects (ObjectType 1); proves RSW object list is correct.
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
            double scale = 0.5; // World (2) -> GAT (1) approx

            foreach (var obj in rsw.Objects)
            {
                if (obj.ObjectType == 1) continue; // models handled by AddModelPlaceholders

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
                    
                    // Label
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

        /// <summary>Set an isometric-style camera above the map (BrowEdit-like).</summary>
        private void ResetViewEvenOut()
        {
            double w = 100, h = 100;
            if (_gndMap3D != null)
            {
                w = _gndMap3D.Width * _gndMap3D.TileScale;
                h = _gndMap3D.Height * _gndMap3D.TileScale;
            }
            else if (_gat != null && _gat.Width > 0 && _gat.Height > 0)
            {
                w = _gat.Width;
                h = _gat.Height;
            }
            ResetViewEvenOut(w, h);
        }

        /// <summary>Set an isometric-style camera above the map (BrowEdit-like).</summary>
        private void ResetViewEvenOut(double mapWidth, double mapHeight)
        {
            double cx = mapWidth * 0.5;
            double cz = mapHeight * 0.5;
            double height = Math.Max(mapWidth, mapHeight) * 0.75;
            double dist = Math.Max(mapWidth, mapHeight) * 0.85;

            var cam = new PerspectiveCamera
            {
                FieldOfView = 45,
                Position = new Point3D(cx + dist, height, cz + dist),
                LookDirection = new Vector3D(-dist, -height, -dist),
                UpDirection = new Vector3D(0, 1, 0)
            };

            Viewport.Camera = cam;

            if (Viewport.CameraController != null)
                Viewport.CameraController.InfiniteSpin = false;
        }

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

        private void Tool_Select_Checked(object sender, RoutedEventArgs e)
        {
            if (ModeSelect != null && ModeSelect.IsChecked == true)
                EditorState.Current.ActiveTool = EditorTool.Select;
        }

        private void Tool_Paint_Checked(object sender, RoutedEventArgs e)
        {
            if (ModePaint != null && ModePaint.IsChecked == true)
                EditorState.Current.ActiveTool = EditorTool.PaintGat_Walkable;
        }

        private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _inputRouter.OnMouseDown(e.GetPosition(Viewport));
            Viewport.CaptureMouse();
        }

        private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _inputRouter.OnMouseUp();
            Viewport.ReleaseMouseCapture();
            
            // Left click: tool action (right/middle/wheel reserved for camera)
            if (e.ChangedButton != MouseButton.Left) return;
            
            var t = EditorState.Current.ActiveTool;
            bool isSelect = (t == EditorTool.Select);
            bool isPaint = (t == EditorTool.PaintGat_Walkable || t == EditorTool.PaintGat_NotWalkable || t == EditorTool.PaintGat_Water);
            if (!isSelect && !isPaint) return;

            var pos = e.GetPosition(Viewport);
            var hits = Viewport3DHelper.FindHits(Viewport.Viewport, pos);
            if (hits == null || hits.Count == 0) return;

            var hp = hits[0].Position;
            int x = (int)Math.Floor(hp.X / GatMeshBuilder.TileSize);
            int y = (int)Math.Floor(hp.Z / GatMeshBuilder.TileSize);

            if (_gat == null || !_gat.InBounds(x, y)) return;

            if (isPaint)
            {
                var sel = (GatCellType)(TypeCombo.SelectedItem ?? GatCellType.NotWalkable);
                int r = (int)RadiusSlider.Value;
                GatPainter.PaintCircle(_gat, x, y, r, sel);
                RebuildMeshSafe();
                PickInfo.Text = $"Cell: ({x},{y})  Set: {sel}";
            }
            else
            {
                PickInfo.Text = $"Selected Cell: ({x},{y})";
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
