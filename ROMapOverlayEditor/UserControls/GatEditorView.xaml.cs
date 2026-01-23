using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using ROMapOverlayEditor.Gat;
using ROMapOverlayEditor.MapAssets;
using ROMapOverlayEditor.Patching;
using ROMapOverlayEditor.Rsw;
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

        public GatEditorView()
        {
            InitializeComponent();
            
            // Wire up MouseDown on the Viewport manually since it's inside the UserControl
            Viewport.MouseDown += Viewport_MouseDown;
            
            TypeCombo.ItemsSource = Enum.GetValues(typeof(GatCellType)).Cast<GatCellType>();
            TypeCombo.SelectedItem = GatCellType.NotWalkable;

            RadiusSlider.ValueChanged += (_, _) => RadiusLabel.Text = $"Radius: {(int)RadiusSlider.Value}";
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
             
             // If input is just "prontera", assume "prontera.rsw" (or try resolve).
             // If input is "data/prontera.rsw", use as is.
             string loadPath = mapNameOrPath;
             if (!loadPath.Contains(".") && !loadPath.Contains("/") && !loadPath.Contains("\\"))
             {
                 loadPath = RswResolver.ResolveRswPath(_vfs, mapNameOrPath) ?? mapNameOrPath;
                 if (!loadPath.EndsWith(".rsw", StringComparison.OrdinalIgnoreCase))
                 {
                      loadPath = mapNameOrPath + ".rsw";
                 }
             }

             _mapName = System.IO.Path.GetFileNameWithoutExtension(mapNameOrPath);
             MapNameInput.Text = _mapName;
             StatusLabel.Text = $"Loading {_mapName}...";

             try
             {
                 var result = ROMapOverlayEditor.ThreeD.ThreeDMapLoader.Load(_vfs, loadPath);

                 if (!result.Ok)
                 {
                     StatusLabel.Text = result.Message;
                     MessageBox.Show(result.Message, "3D Map Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                     return;
                 }
                 
                 var map = result.Map!;
                 _rsw = RswIO.Read(map.RswBytes);
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
             }
             catch (Exception ex)
             {
                 StatusLabel.Text = $"Error: {ex.Message}";
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

            var model = GatMeshBuilder.Build(_gat, GatMeshBuilder.DefaultTypeColor);
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

        private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            
            var pos = e.GetPosition(Viewport);
            var hits = Viewport3DHelper.FindHits(Viewport.Viewport, pos);
            if (hits == null || hits.Count == 0) return;

            var hp = hits[0].Position;
            int x = (int)Math.Floor(hp.X / GatMeshBuilder.TileSize);
            int y = (int)Math.Floor(hp.Z / GatMeshBuilder.TileSize);

            if (_gat != null && _gat.InBounds(x, y))
            {
                 var sel = (GatCellType)(TypeCombo.SelectedItem ?? GatCellType.NotWalkable);
                 int r = (int)RadiusSlider.Value;
                 GatPainter.PaintCircle(_gat, x, y, r, sel);
                 
                 // Partial rebuild or full rebuild
                 RebuildMesh();
                 
                 PickInfo.Text = $"Cell: ({x},{y})  Set: {sel}";
            }
        }
    }
}
