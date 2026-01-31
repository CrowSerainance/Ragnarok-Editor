using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace ROMapOverlayEditor.Gnd
{
    public static class GndMeshBuilder
    {
        // BrowEdit uses tileScale in GND; we map 1 tile -> 1 world unit in your editor for now.
        // If you want “true” scaling, you can multiply XY by (gnd.TileScale / 10f) etc later.
        private const double TileSize = 1.0;

        public static Model3DGroup BuildTerrain(
            GndFile gnd,
            Func<int, ImageSource?> textureByIndex,
            bool includeUntexturedFallback = true)
        {
            var group = new Model3DGroup();
            if (gnd.Width <= 0 || gnd.Height <= 0) return group;

            // Batch quads by texture index so each batch can have one material/brush.
            var batches = new Dictionary<int, MeshBuilder>();

            for (int y = 0; y < gnd.Height; y++)
            {
                for (int x = 0; x < gnd.Width; x++)
                {
                    var cube = gnd.Cubes[x, y];
                    int tileId = cube.TileUp;

                    // -1 means no tile; still can render as plain if desired.
                    int texIndex = -1;
                    GndTile? tile = null;

                    if (tileId >= 0 && tileId < gnd.Tiles.Count)
                    {
                        tile = gnd.Tiles[tileId];
                        texIndex = tile.TextureIndex;
                    }

                    if (texIndex < 0 && !includeUntexturedFallback)
                        continue;

                    if (!batches.TryGetValue(texIndex, out var mb))
                    {
                        mb = new MeshBuilder(true, true);
                        batches[texIndex] = mb;
                    }

                    // Heights (GND uses real float heights)
                    // We map:
                    // corner order in your editor:
                    // sw = (x, y), se=(x+1,y), nw=(x,y+1), ne=(x+1,y+1)
                    var sw = new Point3D(x * TileSize, cube.H1, y * TileSize);
                    var se = new Point3D((x + 1) * TileSize, cube.H2, y * TileSize);
                    var nw = new Point3D(x * TileSize, cube.H3, (y + 1) * TileSize);
                    var ne = new Point3D((x + 1) * TileSize, cube.H4, (y + 1) * TileSize);

                    // UVs
                    // If tile missing, use a simple full-quad UV.
                    var uv_sw = tile != null ? new System.Windows.Point(tile.U1, tile.V1) : new System.Windows.Point(0, 0);
                    var uv_se = tile != null ? new System.Windows.Point(tile.U2, tile.V2) : new System.Windows.Point(1, 0);
                    var uv_ne = tile != null ? new System.Windows.Point(tile.U3, tile.V3) : new System.Windows.Point(1, 1);
                    var uv_nw = tile != null ? new System.Windows.Point(tile.U4, tile.V4) : new System.Windows.Point(0, 1);

                    // Tri 1: sw-se-ne
                    mb.AddTriangle(sw, se, ne, uv_sw, uv_se, uv_ne);
                    // Tri 2: sw-ne-nw
                    mb.AddTriangle(sw, ne, nw, uv_sw, uv_ne, uv_nw);
                }
            }

            foreach (var kv in batches)
            {
                int texIndex = kv.Key;
                var mesh = kv.Value.ToMesh(true);

                if (mesh.Positions == null || mesh.Positions.Count == 0)
                    continue;

                Material mat;

                var img = textureByIndex(texIndex);
                if (img != null)
                {
                    var brush = new ImageBrush(img)
                    {
                        ViewportUnits = BrushMappingMode.Absolute,
                        TileMode = TileMode.None,
                        Stretch = Stretch.Fill
                    };
                    mat = new DiffuseMaterial(brush);
                }
                else
                {
                    // fallback color if texture not found
                    mat = MaterialHelper.CreateMaterial(Color.FromRgb(90, 90, 90));
                }

                var gm = new GeometryModel3D(mesh, mat) { BackMaterial = mat };
                group.Children.Add(gm);
            }

            return group;
        }
    }
}
