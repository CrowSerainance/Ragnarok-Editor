using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace ROMapOverlayEditor.ThreeD
{
    public static class GndTexturedTerrainBuilder
    {
        // WPF performance: build in chunks to avoid one mega-mesh freezing UI.
        public static List<Model3D> Build(GndV2 gnd, TextureAtlas atlas, int chunkSize = 32)
        {
            var models = new List<Model3D>();
            double zBase = gnd.Zoom * gnd.Height;

            for (int cy = 0; cy < gnd.Height; cy += chunkSize)
            {
                for (int cx = 0; cx < gnd.Width; cx += chunkSize)
                {
                    int w = Math.Min(chunkSize, gnd.Width - cx);
                    int h = Math.Min(chunkSize, gnd.Height - cy);

                    var mesh = new MeshGeometry3D
                    {
                        Positions = new Point3DCollection(),
                        TriangleIndices = new Int32Collection(),
                        TextureCoordinates = new PointCollection()
                    };

                    int baseIndex = 0;

                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            int gx = cx + x;
                            int gy = cy + y;

                            var cube = gnd.CubeAt(gx, gy);
                            if (cube == null) continue;
                            if (cube.TileUp < 0 || cube.TileUp >= gnd.Tiles.Count) continue;

                            var tile = gnd.Tiles[cube.TileUp];
                            int texId = tile.TextureIndex;

                            var rect = atlas.TextureRects01.TryGetValue(texId, out var r) ? r : new Rect(0, 0, 1, 1);

                            // BrowEdit3 position basis:
                            // (10*x, -h1, 10*height - 10*y + 10)
                            double xx = gnd.Zoom * gx;
                            double zz = zBase - (gnd.Zoom * gy);

                            var p1 = new Point3D(xx, -cube.H1, zz);                 // h1
                            var p2 = new Point3D(xx + gnd.Zoom, -cube.H2, zz);      // h2
                            var p3 = new Point3D(xx, -cube.H3, zz - gnd.Zoom);      // h3
                            var p4 = new Point3D(xx + gnd.Zoom, -cube.H4, zz - gnd.Zoom); // h4

                            // Tile UVs are [0..1] within the texture; remap into atlas rect.
                            var t1 = Remap(tile.V1, rect);
                            var t2 = Remap(tile.V2, rect);
                            var t3 = Remap(tile.V3, rect);
                            var t4 = Remap(tile.V4, rect);

                            // Add 4 verts
                            mesh.Positions.Add(p1);
                            mesh.Positions.Add(p2);
                            mesh.Positions.Add(p4);
                            mesh.Positions.Add(p3);

                            mesh.TextureCoordinates.Add(t1);
                            mesh.TextureCoordinates.Add(t2);
                            mesh.TextureCoordinates.Add(t4);
                            mesh.TextureCoordinates.Add(t3);

                            // Two triangles (p1,p2,p4) + (p1,p4,p3)
                            mesh.TriangleIndices.Add(baseIndex + 0);
                            mesh.TriangleIndices.Add(baseIndex + 1);
                            mesh.TriangleIndices.Add(baseIndex + 2);

                            mesh.TriangleIndices.Add(baseIndex + 0);
                            mesh.TriangleIndices.Add(baseIndex + 2);
                            mesh.TriangleIndices.Add(baseIndex + 3);

                            baseIndex += 4;
                        }
                    }

                    if (mesh.Positions.Count == 0)
                        continue;

                    mesh.Freeze();

                    var brush = new ImageBrush(atlas.AtlasBitmap)
                    {
                        ViewportUnits = BrushMappingMode.Absolute,
                        TileMode = TileMode.None,
                        Stretch = Stretch.Fill
                    };
                    brush.Freeze();

                    var material = new DiffuseMaterial(brush);
                    material.Freeze();

                    var gm = new GeometryModel3D(mesh, material)
                    {
                        BackMaterial = material
                    };
                    gm.Freeze();

                    models.Add(gm);
                }
            }

            return models;
        }

        private static Point Remap(Vector uv, Rect rect01)
        {
            // WPF texture coords origin is top-left; our decoder already handled origin flag.
            double u = rect01.X + (uv.X * rect01.Width);
            double v = rect01.Y + (uv.Y * rect01.Height);
            return new Point(u, v);
        }
    }
}
