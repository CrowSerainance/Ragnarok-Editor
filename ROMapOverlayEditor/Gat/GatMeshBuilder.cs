using System;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace ROMapOverlayEditor.Gat
{
    public static class GatMeshBuilder
    {
        public const double TileSize = 1.0;
        public const double HeightScale = 0.1;

        public static Model3DGroup Build(GatFile gf, Func<GatCellType, Color> colorByType)
        {
            var group = new Model3DGroup();
            var mb = new MeshBuilder(false, false);

            for (int y = 0; y < gf.Height; y++)
            {
                for (int x = 0; x < gf.Width; x++)
                {
                    var c = gf.Get(x, y);
                    var sw = new Point3D(x * TileSize, c.H1 * HeightScale, y * TileSize);
                    var se = new Point3D((x + 1) * TileSize, c.H2 * HeightScale, y * TileSize);
                    var nw = new Point3D(x * TileSize, c.H3 * HeightScale, (y + 1) * TileSize);
                    var ne = new Point3D((x + 1) * TileSize, c.H4 * HeightScale, (y + 1) * TileSize);

                    mb.AddTriangle(sw, se, ne);
                    mb.AddTriangle(sw, ne, nw);
                }
            }

            var mesh = mb.ToMesh();
            var baseMat = MaterialHelper.CreateMaterial(Colors.LightGray);
            var baseModel = new GeometryModel3D(mesh, baseMat) { BackMaterial = baseMat };
            group.Children.Add(baseModel);

            foreach (GatCellType t in Enum.GetValues(typeof(GatCellType)))
            {
                var mbt = new MeshBuilder(false, false);
                for (int y = 0; y < gf.Height; y++)
                {
                    for (int x = 0; x < gf.Width; x++)
                    {
                        var c = gf.Get(x, y);
                        if (c.Type != t) continue;

                        double h = c.AvgHeight * HeightScale + 0.01;
                        var p0 = new Point3D(x * TileSize, h, y * TileSize);
                        var p1 = new Point3D((x + 1) * TileSize, h, y * TileSize);
                        var p2 = new Point3D((x + 1) * TileSize, h, (y + 1) * TileSize);
                        var p3 = new Point3D(x * TileSize, h, (y + 1) * TileSize);
                        mbt.AddQuad(p0, p1, p2, p3);
                    }
                }

                var m = mbt.ToMesh();
                if (m.Positions == null || m.Positions.Count == 0) continue;

                var ccol = colorByType(t);
                var mat = MaterialHelper.CreateMaterial(Color.FromArgb(120, ccol.R, ccol.G, ccol.B));
                var gm = new GeometryModel3D(m, mat) { BackMaterial = mat };
                group.Children.Add(gm);
            }

            return group;
        }

        public static Color DefaultTypeColor(GatCellType t) => t switch
        {
            GatCellType.Walkable => Colors.LimeGreen,
            GatCellType.NotWalkable => Colors.Red,
            GatCellType.Water => Colors.DodgerBlue,
            GatCellType.Cliff => Colors.Orange,
            _ => Colors.Gray
        };
    }
}
