// File: ROMapOverlayEditor/ROMapOverlayEditor/ThreeD/GndHelixModelBuilder.cs
using System;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace ROMapOverlayEditor.ThreeD
{
    public static class GndHelixModelBuilder
    {
        // Match RO-ish scale used elsewhere (GND tile ~ 10 units)
        private const double TileSize = 10.0;

        // Optional: bring GND heights into a more visible range
        private const double HeightScale = 1.0;

        public static Model3DGroup BuildSolidTerrain(ParsedGnd gnd, Color? color = null)
        {
            var group = new Model3DGroup();
            var mb = new MeshBuilder(false, false);

            int w = gnd.Width;
            int h = gnd.Height;

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var t = gnd.Tiles[x, y];

                // Heights in GND are already “world” units; scale if needed.
                double h00 = t.H00 * HeightScale;
                double h10 = t.H10 * HeightScale;
                double h01 = t.H01 * HeightScale;
                double h11 = t.H11 * HeightScale;

                double x0 = x * TileSize;
                double x1 = (x + 1) * TileSize;
                double z0 = y * TileSize;
                double z1 = (y + 1) * TileSize;

                var p00 = new Point3D(x0, h00, z0);
                var p10 = new Point3D(x1, h10, z0);
                var p01 = new Point3D(x0, h01, z1);
                var p11 = new Point3D(x1, h11, z1);

                // Two triangles
                mb.AddTriangle(p00, p10, p01);
                mb.AddTriangle(p01, p10, p11);
            }

            var mesh = mb.ToMesh();
            var matColor = color ?? Color.FromRgb(70, 70, 70);
            var mat = MaterialHelper.CreateMaterial(new SolidColorBrush(matColor));

            group.Children.Add(new GeometryModel3D
            {
                Geometry = mesh,
                Material = mat,
                BackMaterial = mat
            });

            return group;
        }
    }
}
