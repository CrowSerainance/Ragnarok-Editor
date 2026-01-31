using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
namespace ROMapOverlayEditor.Map3D
{
    public sealed class TerrainBuildResult
    {
        public readonly List<ModelVisual3D> TerrainPieces = new();
        public Rect3D Bounds;
    }

    public static class TerrainBuilder
    {
        // Builds one MeshGeometry3D per textureId. Only TOP faces (terrain) are rendered.
        // COORDINATE SYSTEM: BrowEdit3 style (zoom×x, -h, zoom×y)
        public static TerrainBuildResult BuildTexturedTerrain(
            GndFile gnd,
            Func<string, byte[]?> tryLoadTextureBytes,
            float yScale = 1.0f)
        {
            var res = new TerrainBuildResult();
            var perTex = new Dictionary<int, MeshGeometry3D>();

            int w = gnd.Width;
            int h = gnd.Height;
            float zoom = gnd.TileScale; // BrowEdit3 uses this as zoom/scale factor

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    var cube = gnd.Cubes[idx];
                    int surfIndex = cube.TopSurfaceIndex;
                    if (surfIndex < 0 || surfIndex >= gnd.Surfaces.Length)
                        continue;

                    var s = gnd.Surfaces[surfIndex];
                    int texId = s.TextureId;

                    if (!perTex.TryGetValue(texId, out var mesh))
                    {
                        mesh = new MeshGeometry3D();
                        perTex[texId] = mesh;
                    }

                    // BrowEdit3 coordinate system: (zoom×x, -h, zoom×y)
                    // Heights are negated because BrowEdit stores them inverted
                    var p1 = new Point3D(zoom * x,       -cube.H1 * yScale, zoom * y);
                    var p2 = new Point3D(zoom * (x + 1), -cube.H2 * yScale, zoom * y);
                    var p3 = new Point3D(zoom * (x + 1), -cube.H3 * yScale, zoom * (y + 1));
                    var p4 = new Point3D(zoom * x,       -cube.H4 * yScale, zoom * (y + 1));

                    int baseIndex = mesh.Positions.Count;
                    mesh.Positions.Add(p1);
                    mesh.Positions.Add(p2);
                    mesh.Positions.Add(p3);
                    mesh.Positions.Add(p4);

                    mesh.TextureCoordinates.Add(new Point(s.U1, s.V1));
                    mesh.TextureCoordinates.Add(new Point(s.U2, s.V2));
                    mesh.TextureCoordinates.Add(new Point(s.U3, s.V3));
                    mesh.TextureCoordinates.Add(new Point(s.U4, s.V4));

                    mesh.TriangleIndices.Add(baseIndex + 0);
                    mesh.TriangleIndices.Add(baseIndex + 1);
                    mesh.TriangleIndices.Add(baseIndex + 2);

                    mesh.TriangleIndices.Add(baseIndex + 0);
                    mesh.TriangleIndices.Add(baseIndex + 2);
                    mesh.TriangleIndices.Add(baseIndex + 3);
                }
            }

            foreach (var kv in perTex)
            {
                int texId = kv.Key;
                var mesh = kv.Value;
                var mat = CreateMaterialForTexture(texId, gnd, tryLoadTextureBytes);
                var geom = new GeometryModel3D
                {
                    Geometry = mesh,
                    Material = mat,
                    BackMaterial = mat
                };
                res.TerrainPieces.Add(new ModelVisual3D { Content = geom });
            }

            res.Bounds = new Rect3D(0, 0, 0, gnd.Width * zoom, 50, gnd.Height * zoom);
            return res;
        }

        private static Material CreateMaterialForTexture(int texId, GndFile gnd, Func<string, byte[]?> tryLoadTextureBytes)
        {
            if (texId < 0 || texId >= gnd.Textures.Count)
                return MaterialHelper.CreateMaterial(Brushes.Magenta);

            string name = gnd.Textures[texId];
            if (string.IsNullOrWhiteSpace(name))
                return MaterialHelper.CreateMaterial(Brushes.Magenta);

            // BrowEdit3: "data/texture/" + texture->file (Gnd.cpp line 69)
            string[] candidates =
            {
                $"data/texture/{name}",
                $"data/texture/{name}.tga",
                $"data/texture/{name}.bmp",
                $"data/texture/{name}.png",
                $"data/texture/{name}.jpg",
                name
            };

            foreach (var c in candidates)
            {
                var bytes = tryLoadTextureBytes(c);
                if (bytes == null) continue;

                // Use centralized TextureLoader for consistent TGA/PNG/BMP handling
                var bmp = TextureLoader.LoadTexture(bytes, c);
                if (bmp == null) continue;

                var brush = new ImageBrush(bmp) { Stretch = Stretch.Fill };
                brush.Freeze();
                return new DiffuseMaterial(brush);
            }

            // Fallback: Magenta so missing textures are obvious (per pipeline fix instructions)
            return MaterialHelper.CreateMaterial(Brushes.Magenta);
        }
    }
}
