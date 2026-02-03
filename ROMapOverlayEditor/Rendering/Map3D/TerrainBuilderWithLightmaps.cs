using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using ROMapOverlayEditor.Gnd;

namespace ROMapOverlayEditor.Map3D
{
    /// <summary>
    /// Data for one terrain chunk: mesh arrays + frozen baked texture.
    /// Can be built on a background thread; use CreateModel3DGroupFromTerrainData on UI thread to create visuals.
    /// </summary>
    public sealed class TerrainChunkData
    {
        public Point3D[] Positions { get; set; } = Array.Empty<Point3D>();
        public Point[] TextureCoordinates { get; set; } = Array.Empty<Point>();
        public int[] TriangleIndices { get; set; } = Array.Empty<int>();
        /// <summary>Frozen BitmapSource (built on background thread).</summary>
        public BitmapSource? BakedTexture { get; set; }
        /// <summary>If true, use magenta material (missing texture).</summary>
        public bool UseFallbackMaterial { get; set; }
    }

    /// <summary>
    /// Result of building terrain on a background thread. Pass to CreateModel3DGroupFromTerrainData on UI thread.
    /// </summary>
    public sealed class TerrainBuildData
    {
        public List<TerrainChunkData> Chunks { get; } = new();
        public Rect3D Bounds { get; set; }
    }

    /// <summary>
    /// Enhanced terrain builder that bakes lightmaps into textures for BrowEdit3-style lighting.
    /// This creates the dramatic lighting effect seen in BrowEdit by multiplying base textures
    /// with lightmap data (shadows and colored lighting).
    /// </summary>
    public static class TerrainBuilderWithLightmaps
    {
        /// <summary>Bake at 64x64 to keep speed; no quantization so each tile gets correct lightmap (fixes glitching/black voids).</summary>
        private const int BakeResolution = 64;

        /// <summary>
        /// Builds terrain data (mesh + frozen baked textures) on any thread. Does not create WPF visuals.
        /// Call CreateModel3DGroupFromTerrainData on the UI thread to create the Model3DGroup.
        /// Use this to avoid blocking the UI during heavy lightmap baking.
        /// </summary>
        public static TerrainBuildData BuildTerrainDataOffThread(
            GndFileV2 gnd,
            Func<string, byte[]?> tryLoadTextureBytes,
            float yScale = 1.0f)
        {
            var data = new TerrainBuildData();
            var lightmaps = gnd.Lightmaps;
            bool hasLightmaps = lightmaps?.Data != null && lightmaps.Count > 0;

            if (!hasLightmaps)
            {
                // Fallback: build mesh data + load textures (no WPF visuals) so we can run on any thread
                data.Bounds = new Rect3D(0, 0, 0, gnd.Width * gnd.TileScale, 50, gnd.Height * gnd.TileScale);
                var perTex = new Dictionary<int, (List<Point3D> pos, List<Point> tc, List<int> idx)>();
                int w = gnd.Width, h = gnd.Height;
                float zoom = gnd.TileScale;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        var cube = gnd.Cubes[x, y];
                        int surfIndex = cube.TileUp;
                        if (surfIndex < 0 || surfIndex >= gnd.Surfaces.Count) continue;
                        var s = gnd.Surfaces[surfIndex];
                        int texId = s.TextureIndex;
                        if (!perTex.TryGetValue(texId, out var lists))
                        {
                            lists = (new List<Point3D>(), new List<Point>(), new List<int>());
                            perTex[texId] = lists;
                        }
                        var p1 = new Point3D(zoom * x, -cube.Height00 * yScale, zoom * y);
                        var p2 = new Point3D(zoom * (x + 1), -cube.Height10 * yScale, zoom * y);
                        var p3 = new Point3D(zoom * (x + 1), -cube.Height11 * yScale, zoom * (y + 1));
                        var p4 = new Point3D(zoom * x, -cube.Height01 * yScale, zoom * (y + 1));
                        int baseIndex = lists.pos.Count;
                        lists.pos.Add(p1); lists.pos.Add(p2); lists.pos.Add(p3); lists.pos.Add(p4);
                        lists.tc.Add(new Point(s.U1, s.V1)); lists.tc.Add(new Point(s.U2, s.V2)); lists.tc.Add(new Point(s.U3, s.V3)); lists.tc.Add(new Point(s.U4, s.V4));
                        lists.idx.Add(baseIndex + 0); lists.idx.Add(baseIndex + 2); lists.idx.Add(baseIndex + 1);
                        lists.idx.Add(baseIndex + 0); lists.idx.Add(baseIndex + 3); lists.idx.Add(baseIndex + 2);
                    }
                foreach (var kv in perTex)
                {
                    BitmapSource? tex = LoadTexture(kv.Key, gnd, tryLoadTextureBytes);
                    bool fallback = tex == null;
                    if (tex != null && tex.CanFreeze) tex.Freeze();
                    data.Chunks.Add(new TerrainChunkData
                    {
                        Positions = kv.Value.pos.ToArray(),
                        TextureCoordinates = kv.Value.tc.ToArray(),
                        TriangleIndices = kv.Value.idx.ToArray(),
                        BakedTexture = tex,
                        UseFallbackMaterial = fallback
                    });
                }
                return data;
            }

            var baseTextures = new Dictionary<int, BitmapSource>();
            for (int i = 0; i < gnd.Textures.Count; i++)
            {
                var tex = LoadTexture(i, gnd, tryLoadTextureBytes);
                if (tex != null)
                    baseTextures[i] = tex;
            }

            var perMaterial = new Dictionary<(int texId, int lmId), (List<Point3D> pos, List<Point> tc, List<int> idx)>();
            int mapW = gnd.Width;
            int mapH = gnd.Height;
            float tileZoom = gnd.TileScale;
            // Use actual lightmap index per tile (no quantization) so every tile gets correct lighting — fixes glitching/black voids.

            for (int y = 0; y < mapH; y++)
            {
                for (int x = 0; x < mapW; x++)
                {
                    var cube = gnd.Cubes[x, y];
                    int surfIndex = cube.TileUp;
                    if (surfIndex < 0 || surfIndex >= gnd.Surfaces.Count) continue;
                    var s = gnd.Surfaces[surfIndex];
                    int texId = s.TextureIndex;
                    int lmId = s.LightmapIndex;
                    var key = (texId, lmId);
                    if (!perMaterial.TryGetValue(key, out var lists))
                    {
                        lists = (new List<Point3D>(), new List<Point>(), new List<int>());
                        perMaterial[key] = lists;
                    }
                    var p1 = new Point3D(tileZoom * x, -cube.Height00 * yScale, tileZoom * y);
                    var p2 = new Point3D(tileZoom * (x + 1), -cube.Height10 * yScale, tileZoom * y);
                    var p3 = new Point3D(tileZoom * (x + 1), -cube.Height11 * yScale, tileZoom * (y + 1));
                    var p4 = new Point3D(tileZoom * x, -cube.Height01 * yScale, tileZoom * (y + 1));
                    int baseIndex = lists.pos.Count;
                    lists.pos.Add(p1); lists.pos.Add(p2); lists.pos.Add(p3); lists.pos.Add(p4);
                    lists.tc.Add(new Point(s.U1, s.V1)); lists.tc.Add(new Point(s.U2, s.V2)); lists.tc.Add(new Point(s.U3, s.V3)); lists.tc.Add(new Point(s.U4, s.V4));
                    lists.idx.Add(baseIndex + 0); lists.idx.Add(baseIndex + 2); lists.idx.Add(baseIndex + 1);
                    lists.idx.Add(baseIndex + 0); lists.idx.Add(baseIndex + 3); lists.idx.Add(baseIndex + 2);
                }
            }

            var materialList = perMaterial.ToList();
            var chunks = new TerrainChunkData[materialList.Count];
            Parallel.For(0, materialList.Count, i =>
            {
                var kv = materialList[i];
                int texId = kv.Key.texId;
                int lightmapIndex = kv.Key.lmId;
                var (pos, tc, idx) = kv.Value;
                BitmapSource? baked = null;
                bool fallback = false;
                if (baseTextures.TryGetValue(texId, out var baseTex))
                {
                    baked = BakeLightmapIntoTextureLowRes(baseTex, lightmapIndex, gnd);
                    if (baked != null && baked.CanFreeze)
                        baked.Freeze();
                }
                else
                    fallback = true;
                chunks[i] = new TerrainChunkData
                {
                    Positions = pos.ToArray(),
                    TextureCoordinates = tc.ToArray(),
                    TriangleIndices = idx.ToArray(),
                    BakedTexture = baked,
                    UseFallbackMaterial = fallback
                };
            });
            foreach (var c in chunks)
                data.Chunks.Add(c);

            data.Bounds = new Rect3D(0, 0, 0, mapW * tileZoom, 50, mapH * tileZoom);
            return data;
        }

        /// <summary>
        /// Creates a Model3DGroup from pre-built terrain data. Must be called on the UI thread.
        /// </summary>
        public static Model3DGroup CreateModel3DGroupFromTerrainData(TerrainBuildData data)
        {
            var group = new Model3DGroup();
            foreach (var chunk in data.Chunks)
            {
                var mesh = new MeshGeometry3D();
                foreach (var p in chunk.Positions) mesh.Positions.Add(p);
                foreach (var t in chunk.TextureCoordinates) mesh.TextureCoordinates.Add(t);
                foreach (var i in chunk.TriangleIndices) mesh.TriangleIndices.Add(i);
                Material mat = chunk.UseFallbackMaterial || chunk.BakedTexture == null
                    ? MaterialHelper.CreateMaterial(Brushes.Magenta)
                    : new DiffuseMaterial(new ImageBrush(chunk.BakedTexture) { Stretch = Stretch.Fill });
                if (mat is DiffuseMaterial dm2 && dm2.Brush is ImageBrush ib2)
                    ib2.Freeze();
                var geom = new GeometryModel3D { Geometry = mesh, Material = mat, BackMaterial = mat };
                group.Children.Add(geom);
            }
            return group;
        }

        /// <summary>
        /// Builds terrain with lightmap baking. Each tile gets its texture multiplied by its lightmap.
        /// This creates the BrowEdit3 look: warm glows, deep shadows, atmospheric lighting.
        /// </summary>
        public static TerrainBuildResult BuildTexturedTerrainWithLightmaps(
            GndFileV2 gnd,
            Func<string, byte[]?> tryLoadTextureBytes,
            float yScale = 1.0f)
        {
            var res = new TerrainBuildResult();
            
            // Check if we have lightmap data
            var lightmaps = gnd.Lightmaps;
            bool hasLightmaps = lightmaps?.Data != null && lightmaps.Count > 0;
            
            if (!hasLightmaps)
            {
                System.Diagnostics.Debug.WriteLine("[TerrainBuilderWithLightmaps] No lightmap data available, falling back to basic rendering");
                return TerrainBuilder.BuildTexturedTerrain(gnd, tryLoadTextureBytes, yScale);
            }

            System.Diagnostics.Debug.WriteLine($"[TerrainBuilderWithLightmaps] Building terrain with {lightmaps!.Count} lightmaps ({lightmaps.CellWidth}x{lightmaps.CellHeight})");

            // Load all base textures first
            var baseTextures = new Dictionary<int, BitmapSource>();
            for (int i = 0; i < gnd.Textures.Count; i++)
            {
                var tex = LoadTexture(i, gnd, tryLoadTextureBytes);
                if (tex != null)
                    baseTextures[i] = tex;
            }

            // Group tiles by texture AND lightmap for efficient batching
            var perMaterial = new Dictionary<(int texId, int lmId), MeshGeometry3D>();

            int w = gnd.Width;
            int h = gnd.Height;
            float zoom = gnd.TileScale;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var cube = gnd.Cubes[x, y];
                    int surfIndex = cube.TileUp;
                    
                    if (surfIndex < 0 || surfIndex >= gnd.Surfaces.Count)
                        continue;

                    var s = gnd.Surfaces[surfIndex];
                    int texId = s.TextureIndex;
                    int lmId = s.LightmapIndex;

                    var key = (texId, lmId);
                    if (!perMaterial.TryGetValue(key, out var mesh))
                    {
                        mesh = new MeshGeometry3D();
                        perMaterial[key] = mesh;
                    }

                    var p1 = new Point3D(zoom * x,       -cube.Height00 * yScale, zoom * y);
                    var p2 = new Point3D(zoom * (x + 1), -cube.Height10 * yScale, zoom * y);
                    var p3 = new Point3D(zoom * (x + 1), -cube.Height11 * yScale, zoom * (y + 1));
                    var p4 = new Point3D(zoom * x,       -cube.Height01 * yScale, zoom * (y + 1));

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
                    mesh.TriangleIndices.Add(baseIndex + 2);
                    mesh.TriangleIndices.Add(baseIndex + 1);

                    mesh.TriangleIndices.Add(baseIndex + 0);
                    mesh.TriangleIndices.Add(baseIndex + 3);
                    mesh.TriangleIndices.Add(baseIndex + 2);
                }
            }

            // Create materials: bake lightmap into texture
            foreach (var kv in perMaterial)
            {
                int texId = kv.Key.texId;
                int lmId = kv.Key.lmId;
                var mesh = kv.Value;

                Material mat;
                if (baseTextures.TryGetValue(texId, out var baseTex))
                {
                    // Bake lightmap into texture
                    var litTex = BakeLightmapIntoTexture(baseTex, lmId, gnd);
                    var brush = new ImageBrush(litTex) { Stretch = Stretch.Fill };
                    brush.Freeze();
                    mat = new DiffuseMaterial(brush);
                }
                else
                {
                    mat = MaterialHelper.CreateMaterial(Brushes.Magenta);
                }

                var geom = new GeometryModel3D
                {
                    Geometry = mesh,
                    Material = mat,
                    BackMaterial = mat
                };
                res.TerrainPieces.Add(new ModelVisual3D { Content = geom });
            }

            res.Bounds = new Rect3D(0, 0, 0, gnd.Width * zoom, 50, gnd.Height * zoom);
            System.Diagnostics.Debug.WriteLine($"[TerrainBuilderWithLightmaps] Built {res.TerrainPieces.Count} terrain pieces with lightmaps");
            return res;
        }

        /// <summary>
        /// Bakes at 64x64 only — BrowEdit does GPU sampling (one texture + one lightmap atlas); we approximate with far fewer pixels per bake for speed.
        /// </summary>
        private static BitmapSource? BakeLightmapIntoTextureLowRes(BitmapSource baseTexture, int lightmapIndex, GndFileV2 gnd)
        {
            var lm = ExtractLightmap(lightmapIndex, gnd);
            if (lm == null) return baseTexture;

            int outW = BakeResolution;
            int outH = BakeResolution;
            int lmW = gnd.Lightmaps!.CellWidth;
            int lmH = gnd.Lightmaps.CellHeight;
            int texW = baseTexture.PixelWidth;
            int texH = baseTexture.PixelHeight;

            var converted = new FormatConvertedBitmap(baseTexture, PixelFormats.Bgra32, null, 0);
            var scale = new ScaleTransform((double)outW / texW, (double)outH / texH);
            var scaled = new TransformedBitmap(converted, scale);
            var texPixels = new byte[outW * outH * 4];
            scaled.CopyPixels(texPixels, outW * 4, 0);

            for (int y = 0; y < outH; y++)
            {
                for (int x = 0; x < outW; x++)
                {
                    int lmX = (x * lmW) / outW; if (lmX >= lmW) lmX = lmW - 1;
                    int lmY = (y * lmH) / outH; if (lmY >= lmH) lmY = lmH - 1;
                    int lmIdx = lmX + lmY * lmW;
                    float r = lm[lmIdx * 4 + 2] / 255f;
                    float g = lm[lmIdx * 4 + 1] / 255f;
                    float b = lm[lmIdx * 4 + 0] / 255f;
                    float a = lm[lmIdx * 4 + 3] / 255f;
                    int texIdx = (x + y * outW) * 4;
                    texPixels[texIdx + 0] = (byte)(texPixels[texIdx + 0] * b * a);
                    texPixels[texIdx + 1] = (byte)(texPixels[texIdx + 1] * g * a);
                    texPixels[texIdx + 2] = (byte)(texPixels[texIdx + 2] * r * a);
                }
            }

            var result = BitmapSource.Create(outW, outH, 96, 96, PixelFormats.Bgra32, null, texPixels, outW * 4);
            result.Freeze();
            return result;
        }

        /// <summary>
        /// Bakes a lightmap into a texture (full resolution). Use BakeLightmapIntoTextureLowRes for fast path.
        /// </summary>
        private static BitmapSource BakeLightmapIntoTexture(BitmapSource baseTexture, int lightmapIndex, GndFileV2 gnd)
        {
            var lm = ExtractLightmap(lightmapIndex, gnd);
            if (lm == null) return baseTexture;

            int texW = baseTexture.PixelWidth;
            int texH = baseTexture.PixelHeight;
            int lmW = gnd.Lightmaps!.CellWidth;
            int lmH = gnd.Lightmaps.CellHeight;

            var converted = new FormatConvertedBitmap(baseTexture, PixelFormats.Bgra32, null, 0);
            var texPixels = new byte[texW * texH * 4];
            converted.CopyPixels(texPixels, texW * 4, 0);

            for (int y = 0; y < texH; y++)
            {
                for (int x = 0; x < texW; x++)
                {
                    int lmX = (x * lmW) / texW; if (lmX >= lmW) lmX = lmW - 1;
                    int lmY = (y * lmH) / texH; if (lmY >= lmH) lmY = lmH - 1;
                    int lmIdx = lmX + lmY * lmW;
                    float r = lm[lmIdx * 4 + 2] / 255f;
                    float g = lm[lmIdx * 4 + 1] / 255f;
                    float b = lm[lmIdx * 4 + 0] / 255f;
                    float a = lm[lmIdx * 4 + 3] / 255f;
                    int texIdx = (x + y * texW) * 4;
                    texPixels[texIdx + 0] = (byte)(texPixels[texIdx + 0] * b * a);
                    texPixels[texIdx + 1] = (byte)(texPixels[texIdx + 1] * g * a);
                    texPixels[texIdx + 2] = (byte)(texPixels[texIdx + 2] * r * a);
                }
            }

            var result = BitmapSource.Create(texW, texH, 96, 96, PixelFormats.Bgra32, null, texPixels, texW * 4);
            result.Freeze();
            return result;
        }

        /// <summary>
        /// Extracts a single lightmap from GND lightmap data.
        /// GND format: first (W*H) bytes = alpha, next (W*H*3) bytes = RGB.
        /// Returns BGRA pixel array (8x8 typically).
        /// </summary>
        private static byte[]? ExtractLightmap(int lightmapIndex, GndFileV2 gnd)
        {
            if (gnd.Lightmaps?.Data == null || lightmapIndex < 0 || lightmapIndex >= gnd.Lightmaps.Count)
                return null;

            int lmW = gnd.Lightmaps.CellWidth;
            int lmH = gnd.Lightmaps.CellHeight;
            int pixelsPerLm = lmW * lmH;
            
            // GND lightmap format: (W*H) alpha + (W*H*3) RGB = 256 bytes for 8x8
            int bytesPerLm = pixelsPerLm + (pixelsPerLm * 3);
            int offset = lightmapIndex * bytesPerLm;

            if (offset + bytesPerLm > gnd.Lightmaps.Data.Length)
                return null;

            var result = new byte[pixelsPerLm * 4]; // BGRA

            // Read alpha values (first W*H bytes)
            for (int i = 0; i < pixelsPerLm; i++)
            {
                result[i * 4 + 3] = gnd.Lightmaps.Data[offset + i]; // Alpha
            }

            // Read RGB values (next W*H*3 bytes)
            int rgbOffset = offset + pixelsPerLm;
            for (int i = 0; i < pixelsPerLm; i++)
            {
                result[i * 4 + 2] = gnd.Lightmaps.Data[rgbOffset + i * 3 + 0]; // R
                result[i * 4 + 1] = gnd.Lightmaps.Data[rgbOffset + i * 3 + 1]; // G
                result[i * 4 + 0] = gnd.Lightmaps.Data[rgbOffset + i * 3 + 2]; // B
            }

            return result;
        }

        private static BitmapSource? LoadTexture(int texId, GndFileV2 gnd, Func<string, byte[]?> tryLoadTextureBytes)
        {
            if (texId < 0 || texId >= gnd.Textures.Count)
                return null;

            string name = gnd.Textures[texId].Filename;
            if (string.IsNullOrWhiteSpace(name))
                return null;

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

                var bmp = TextureLoader.LoadTexture(bytes, c);
                if (bmp != null)
                    return bmp;
            }

            return null;
        }
    }
}
