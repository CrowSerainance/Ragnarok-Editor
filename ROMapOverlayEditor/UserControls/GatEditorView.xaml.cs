using System;
using System.Linq;
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
        private bool _viewGatOverlay = true;

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
             
             _mapName = System.IO.Path.GetFileNameWithoutExtension(mapNameOrPath);
             MapNameInput.Text = _mapName;
             StatusLabel.Text = $"Resolving {_mapName}...";
             PreviewText.Text = "";

             try
             {
                 // Use VfsPathResolver to find RSW/GND/GAT paths
                 var (rswPath, gndPath, gatPath) = ROMapOverlayEditor.Sources.VfsPathResolver.ResolveMapTriplet(_vfs, mapNameOrPath);

                 if (rswPath == null)
                 {
                     StatusLabel.Text = $"RSW not found for '{_mapName}'";
                     PreviewText.Text = $"Failed to resolve map files.\n\nTried:\n- {mapNameOrPath}.rsw\n- data/{mapNameOrPath}.rsw\n\nTry:\n- Opening additional GRF files\n- Mounting pack files\n- Checking if map exists in your GRF";
                     return;
                 }

                 // Show resolved paths in preview
                 var preview = $"Resolved paths:\nRSW: {rswPath}\n";
                 if (gndPath != null)
                     preview += $"GND: {gndPath}\n";
                 else
                     preview += $"GND: (missing - required for 3D)\n";
                 
                 if (gatPath != null)
                     preview += $"GAT: {gatPath}\n";
                 else
                     preview += $"GAT: (missing - recommended)\n";

                 PreviewText.Text = preview;

                 // Validate RSW header before full load
                 byte[] rswBytes;
                 try
                 {
                     rswBytes = _vfs.ReadAllBytes(rswPath);
                 }
                 catch (Exception ex)
                 {
                     StatusLabel.Text = $"Failed to read RSW: {ex.Message}";
                     PreviewText.Text = preview + $"\nRead error: {ex.Message}";
                     return;
                 }

                 var (headerOk, headerMsg, headerInfo) = ROMapOverlayEditor.ThreeD.RswHeaderReader.TryRead(rswBytes);
                 if (!headerOk)
                 {
                     StatusLabel.Text = "RSW header validation failed";
                     PreviewText.Text = preview + $"\n\nHeader Error:\n{headerMsg}";
                     MessageBox.Show($"RSW header validation failed:\n\n{headerMsg}\n\nCannot proceed with 3D load.", "RSW Header Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                     return;
                 }

                 preview += $"\nRSW Header:\nSig: {headerInfo!.Signature}\nVer: {headerInfo.Major}.{headerInfo.Minor}\nObjects: {headerInfo.ObjectCount} @ 0x{headerInfo.ObjectCountOffset:X}";
                 PreviewText.Text = preview;

                 if (gndPath == null)
                 {
                     StatusLabel.Text = "GND file required but not found";
                     PreviewText.Text = preview + "\n\nGND is required for 3D rendering. Cannot proceed.";
                     MessageBox.Show($"GND file is required for 3D rendering but was not found.\n\nResolved RSW: {rswPath}\n\nPlease ensure the GND file exists in your GRF or mounted sources.", "Missing GND", MessageBoxButton.OK, MessageBoxImage.Warning);
                     return;
                 }

                 StatusLabel.Text = $"Loading {_mapName}...";

                 var result = ROMapOverlayEditor.ThreeD.ThreeDMapLoader.Load(_vfs, rswPath);

                 if (!result.Ok)
                 {
                     StatusLabel.Text = result.Message;
                     MessageBox.Show(result.Message, "3D Map Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                     return;
                 }
                 
                 var map = result.Map!;
                 
                 // Try to load RSW, but don't fail completely if it fails
                 try
                 {
                     _rsw = RswIO.Read(map.RswBytes);
                     preview += $"\nRSW Objects: {_rsw.ObjectCount} loaded successfully";
                 }
                 catch (Exception rswEx)
                 {
                     _rsw = null;
                     preview += $"\n\nRSW Objects: Failed to parse ({rswEx.Message})\nGAT editing still available.";
                     StatusLabel.Text = $"RSW objects not parsed; GAT still editable";
                 }
                 
                 PreviewText.Text = preview;
                 _gatVirtualPath = map.GatPath;

                 // Check staging for GAT
                 if (_staging != null && _staging.TryGet(map.GatPath, out var stagedBytes))
                 {
                     _gat = GatIO.Read(stagedBytes);
                     StatusLabel.Text = $"Loaded {_mapName} (Staged GAT)";
                 }
                 else if (map.GatBytes.Length > 0)
                 {
                     _gat = GatIO.Read(map.GatBytes);
                     StatusLabel.Text = $"Loaded {_mapName} (VFS GAT)";
                 }
                 else
                 {
                     // Fallback empty GAT?
                     StatusLabel.Text = $"Loaded {_mapName} (No GAT found)";
                     _gat = new GatFile { Width = 100, Height = 100, Cells = new GatCell[10000] }; 
                 }

                 RebuildMesh();
                 
                 // Reset camera after loading
                 if (Viewport.Camera != null)
                 {
                     _cam.ResetDefault();
                     ApplyCameraToRenderer();
                     Viewport.ZoomExtents();
                 }
             }
             catch (Exception ex)
             {
                 StatusLabel.Text = $"Error: {ex.Message}";
                 PreviewText.Text = (PreviewText.Text ?? "") + $"\n\nLoad Error: {ex.Message}";
                 MessageBox.Show($"Failed to load map '{_mapName}':\n{ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
             }
         }

        private void LoadMapBtn_Click(object sender, RoutedEventArgs e)
        {
            var name = MapNameInput.Text.Trim();
            if (!string.IsNullOrEmpty(name)) LoadMap(name);
        }

        private void Rebuild_Click(object sender, RoutedEventArgs e) => RebuildMesh();

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

        private void RebuildMesh()
        {
            Viewport.Children.Clear();
            Viewport.Children.Add(new DefaultLights());

            var model = GatMeshBuilder.Build(_gat, GatMeshBuilder.DefaultTypeColor, _viewGatOverlay);
            var vis = new ModelVisual3D { Content = model };
            Viewport.Children.Add(vis);

            // Add RSW markers
            if (_rsw != null) AddRswMarkers(_rsw);

            // If no RSW, zoom extents. If RSW exists, objects might be far apart, but usually zooming to user's focused area is better.
            // For now, simple ZoomExtents is safe.
            // Viewport.ZoomExtents(); 
        }

        private void AddRswMarkers(RswFile rsw)
        {
            double scale = 0.5; // World (2) -> GAT (1) approx

            foreach (var obj in rsw.Objects)
            {
                var mesh = new MeshBuilder();
                Color c = Colors.White;
                Vec3 p = new Vec3(0,0,0);
                string label = "";
                double size = 1.0;

                switch (obj)
                {
                    case RswModel m:
                        c = Colors.Aqua; p = m.Position; label = m.Name; size = 2.5;
                        mesh.AddBox(new Point3D(p.X * scale, p.Y * scale, p.Z * scale), size, size, size);
                        break;
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

                if (obj is RswModel || obj is RswLight || obj is RswSound || obj is RswEffect)
                {
                    var geom = new GeometryModel3D { Geometry = mesh.ToMesh(), Material = new DiffuseMaterial(new SolidColorBrush(c)) };
                    Viewport.Children.Add(new ModelVisual3D { Content = geom });
                    
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
                         Viewport.Children.Add(text);
                    }
                }
            }
        }

        private void ResetView_Click(object sender, RoutedEventArgs e)
        {
            _cam.ResetDefault();
            ApplyCameraToRenderer();
            Viewport.ZoomExtents();
        }

        private void GatOverlay_Changed(object sender, RoutedEventArgs e)
        {
            _viewGatOverlay = GatOverlayCheck?.IsChecked == true;
            RebuildMesh();
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
                RebuildMesh();
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
