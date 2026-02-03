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
    /// Blending model (matches BrowEdit GND shader):
    /// 1. Lightmap alpha = shadow multiplier: baseRGB *= lightmap.a (occlusion/shadows).
    /// 2. Lightmap RGB = additive term: baseRGB += lightmap.rgb (baked colored lighting).
    /// 3. Per-tile vertex color (BGRA from GND surface) = tint: baseRGB *= vertexColor.rgb, baseA *= vertexColor.a.
    /// Final: color = (baseTex * vertexColor * lightmapAlpha) + lightmapRGB; alpha = baseAlpha * vertexAlpha.
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
        /// <param name="gnd">Must be GndFileV2 from GndReaderV2 (populates Cubes with full Height00/10/01/11). Do not use ParsedGnd — it does not carry full cube height data.</param>
        /// <param name="yScale">Scale applied to vertex Y (elevation). Use 1.0f to preserve GND heights; do not zero or force flat.</param>
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
                var (minY, maxY) = gnd.GetTerrainHeightExtent(yScale);
                data.Bounds = new Rect3D(0, minY, 0, gnd.Width * gnd.TileScale, maxY - minY, gnd.Height * gnd.TileScale);
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

            var perMaterial = new Dictionary<(int texId, int lmId, int packedColor), (List<Point3D> pos, List<Point> tc, List<int> idx)>();
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
                    // Pack BGRA into an int for the dictionary key
                    int packedColor = (s.A << 24) | (s.R << 16) | (s.G << 8) | s.B;

                    var key = (texId, lmId, packedColor);
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
                int packedColor = kv.Key.packedColor;
                
                // Unpack color for bake
                byte b = (byte)(packedColor & 0xFF);
                byte g = (byte)((packedColor >> 8) & 0xFF);
                byte r = (byte)((packedColor >> 16) & 0xFF);
                byte a = (byte)((packedColor >> 24) & 0xFF);

                var (pos, tc, idx) = kv.Value;
                BitmapSource? baked = null;
                bool fallback = false;
                if (baseTextures.TryGetValue(texId, out var baseTex))
                {
                    baked = BakeLightmapIntoTextureLowRes(baseTex, lightmapIndex, r, g, b, a, gnd);
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

            var (extentMinY, extentMaxY) = gnd.GetTerrainHeightExtent(yScale);
            data.Bounds = new Rect3D(0, extentMinY, 0, mapW * tileZoom, extentMaxY - extentMinY, mapH * tileZoom);
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

            // Group tiles by texture AND lightmap AND vertex color for efficient batching
            var perMaterial = new Dictionary<(int texId, int lmId, int packedColor), MeshGeometry3D>();

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
                    // Pack BGRA into an int
                    int packedColor = (s.A << 24) | (s.R << 16) | (s.G << 8) | s.B;

                    var key = (texId, lmId, packedColor);
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

                    // =========================================================
                    // WALLS (Front & Side) - Filling the gaps between tiles
                    // =========================================================
                    
                    // 1) FRONT WALL (Faces +Z, connects Height11/01 to neighbor's Height10/00)
                    if (cube.TileFront >= 0 && cube.TileFront < gnd.Surfaces.Count)
                    {
                        var sF = gnd.Surfaces[cube.TileFront];
                        // If neighbor (y+1) exists, we connect to its top edge. 
                        // If map edge, we extend down or ignore? 
                        // BrowEdit uses Height01/Height11 of current tile for the top of the wall
                        // and Height00/Height10 of the neighbor (y+1) for the bottom of the wall (visually).
                        
                        // We need to add this wall to the batch corresponding to its texture
                        int fPack = (sF.A << 24) | (sF.R << 16) | (sF.G << 8) | sF.B;
                        var fKey = (sF.TextureIndex, (int)sF.LightmapIndex, fPack);

                        if (!perMaterial.TryGetValue(fKey, out var fMesh))
                        {
                            fMesh = new MeshGeometry3D();
                            perMaterial[fKey] = fMesh;
                        }

                        // Geometry:
                        // Top-Left (p4 in main tile):     x,   Height01, y+1
                        // Top-Right (p3 in main tile):    x+1, Height11, y+1
                        // Bottom-Left (neighbor's p1):    x,   Neighbor.Height00, y+1
                        // Bottom-Right (neighbor's p2):   x+1, Neighbor.Height10, y+1
                        
                        // Check neighbor for height reference
                        float hBL = cube.Height01; // Current tile bottom-left (relative to mesh top)
                        float hBR = cube.Height11; // Current tile bottom-right
                        float hNextBL = hBL; // Default if no neighbor
                        float hNextBR = hBR; 

                        if (y < h - 1)
                        {
                            var nextCube = gnd.Cubes[x, y + 1];
                            hNextBL = nextCube.Height00;
                            hNextBR = nextCube.Height10;
                        }
                        
                        // Vertices
                        // 0: Top-Left (x, -hBL, y+1)
                        // 1: Top-Right (x+1, -hBR, y+1)
                        // 2: Btm-Right (x+1, -hNextBR, y+1)
                        // 3: Btm-Left (x, -hNextBL, y+1)
                        
                        var wp0 = new Point3D(zoom * x,       -hBL * yScale, zoom * (y + 1));
                        var wp1 = new Point3D(zoom * (x + 1), -hBR * yScale, zoom * (y + 1));
                        var wp2 = new Point3D(zoom * (x + 1), -hNextBR * yScale, zoom * (y + 1));
                        var wp3 = new Point3D(zoom * x,       -hNextBL * yScale, zoom * (y + 1));

                        int fIdx = fMesh.Positions.Count;
                        fMesh.Positions.Add(wp0);
                        fMesh.Positions.Add(wp1);
                        fMesh.Positions.Add(wp2);
                        fMesh.Positions.Add(wp3);

                        // UVs
                        fMesh.TextureCoordinates.Add(new Point(sF.U1, sF.V1));
                        fMesh.TextureCoordinates.Add(new Point(sF.U2, sF.V2));
                        fMesh.TextureCoordinates.Add(new Point(sF.U4, sF.V4)); // BrowEdit typically maps U3/V3 to bottom-left? Standard quad mapping
                        fMesh.TextureCoordinates.Add(new Point(sF.U3, sF.V3)); 

                        // Indices (0-1-2, 0-2-3)
                        fMesh.TriangleIndices.Add(fIdx + 0);
                        fMesh.TriangleIndices.Add(fIdx + 1);
                        fMesh.TriangleIndices.Add(fIdx + 2);

                        fMesh.TriangleIndices.Add(fIdx + 0);
                        fMesh.TriangleIndices.Add(fIdx + 2);
                        fMesh.TriangleIndices.Add(fIdx + 3);
                    }

                    // 2) SIDE WALL (Faces +X, connects Height10/11 to neighbor's Height00/01)
                    if (cube.TileSide >= 0 && cube.TileSide < gnd.Surfaces.Count)
                    {
                        var sS = gnd.Surfaces[cube.TileSide];
                        int sPack = (sS.A << 24) | (sS.R << 16) | (sS.G << 8) | sS.B;
                        var sKey = (sS.TextureIndex, (int)sS.LightmapIndex, sPack);

                        if (!perMaterial.TryGetValue(sKey, out var sMesh))
                        {
                            sMesh = new MeshGeometry3D();
                            perMaterial[sKey] = sMesh;
                        }

                        // Heights
                        float hTR = cube.Height10; // Top-Right of current
                        float hBR = cube.Height11; // Bottom-Right of current
                        float hNextTL = hTR;
                        float hNextBL = hBR;

                        if (x < w - 1)
                        {
                            var nextCube = gnd.Cubes[x + 1, y];
                            hNextTL = nextCube.Height00;
                            hNextBL = nextCube.Height01;
                        }

                        // Vertices (vertical plane at x+1)
                        // 0: Top-Left (x+1, -hTR, y)
                        // 1: Top-Right (x+1, -hBR, y+1)
                        // 2: Btm-Right (x+1, -hNextBL, y+1)
                        // 3: Btm-Left (x+1, -hNextTL, y)

                        var wp0 = new Point3D(zoom * (x + 1), -hTR * yScale, zoom * y);
                        var wp1 = new Point3D(zoom * (x + 1), -hBR * yScale, zoom * (y + 1));
                        var wp2 = new Point3D(zoom * (x + 1), -hNextBL * yScale, zoom * (y + 1));
                        var wp3 = new Point3D(zoom * (x + 1), -hNextTL * yScale, zoom * y);

                        int sIdx = sMesh.Positions.Count;
                        sMesh.Positions.Add(wp0);
                        sMesh.Positions.Add(wp1);
                        sMesh.Positions.Add(wp2);
                        sMesh.Positions.Add(wp3);

                        // UVs
                        sMesh.TextureCoordinates.Add(new Point(sS.U1, sS.V1));
                        sMesh.TextureCoordinates.Add(new Point(sS.U2, sS.V2));
                        sMesh.TextureCoordinates.Add(new Point(sS.U4, sS.V4));
                        sMesh.TextureCoordinates.Add(new Point(sS.U3, sS.V3));

                        // Indices
                        sMesh.TriangleIndices.Add(sIdx + 0);
                        sMesh.TriangleIndices.Add(sIdx + 1);
                        sMesh.TriangleIndices.Add(sIdx + 2);

                        sMesh.TriangleIndices.Add(sIdx + 0);
                        sMesh.TriangleIndices.Add(sIdx + 2);
                        sMesh.TriangleIndices.Add(sIdx + 3);
                    }
                }
            }

            // Create materials: bake lightmap into texture
            foreach (var kv in perMaterial)
            {
                int texId = kv.Key.texId;
                int lmId = kv.Key.lmId;
                int packedColor = kv.Key.packedColor;
                
                // Unpack color for bake
                byte b = (byte)(packedColor & 0xFF);
                byte g = (byte)((packedColor >> 8) & 0xFF);
                byte r = (byte)((packedColor >> 16) & 0xFF);
                byte a = (byte)((packedColor >> 24) & 0xFF);

                var mesh = kv.Value;

                Material mat;
                if (baseTextures.TryGetValue(texId, out var baseTex))
                {
                    // Bake lightmap into texture
                    var litTex = BakeLightmapIntoTexture(baseTex, lmId, r, g, b, a, gnd);
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

            var (minY, maxY) = gnd.GetTerrainHeightExtent(yScale);
            res.Bounds = new Rect3D(0, minY, 0, gnd.Width * zoom, maxY - minY, gnd.Height * zoom);
            System.Diagnostics.Debug.WriteLine($"[TerrainBuilderWithLightmaps] Built {res.TerrainPieces.Count} terrain pieces with lightmaps");
            return res;
        }

        /// <summary>
        /// Bakes at 64x64 only — BrowEdit does GPU sampling (one texture + one lightmap atlas); we approximate with far fewer pixels per bake for speed.
        /// Now applies vertex color multiplication and ADDITIVE lightmap logic (match BrowEdit shader).
        /// </summary>
        private static BitmapSource? BakeLightmapIntoTextureLowRes(
            BitmapSource baseTexture, int lightmapIndex, 
            byte vR, byte vG, byte vB, byte vA,
            GndFileV2 gnd)
        {
            var lm = ExtractLightmap(lightmapIndex, gnd);
            if (lm == null) return baseTexture;

            int outW = BakeResolution;
            int outH = BakeResolution;
            int lmW = gnd.Lightmaps!.CellWidth;
            int lmH = gnd.Lightmaps.CellHeight;
            int texW = baseTexture.PixelWidth;
            int texH = baseTexture.PixelHeight;

            // Base texture to 64x64
            var converted = new FormatConvertedBitmap(baseTexture, PixelFormats.Bgra32, null, 0);
            var scale = new ScaleTransform((double)outW / texW, (double)outH / texH);
            var scaled = new TransformedBitmap(converted, scale);
            var texPixels = new byte[outW * outH * 4];
            scaled.CopyPixels(texPixels, outW * 4, 0);

            // Per-tile vertex color (BGRA from GND surface) — tint and alpha
            float fVr = vR / 255f;
            float fVg = vG / 255f;
            float fB = vB / 255f;
            float fVa = vA / 255f;

            for (int y = 0; y < outH; y++)
            {
                for (int x = 0; x < outW; x++)
                {
                    int lmX = (x * lmW) / outW; if (lmX >= lmW) lmX = lmW - 1;
                    int lmY = (y * lmH) / outH; if (lmY >= lmH) lmY = lmH - 1;
                    int lmIdx = lmX + lmY * lmW;
                    
                    // Lightmap RGBA (GND format is A, RGB in 256 byte blocks, our ExtractLightmap returns BGRA)
                    // ExtractLightmap returns BGRA
                    float lmB = lm[lmIdx * 4 + 0] / 255f;
                    float lmG = lm[lmIdx * 4 + 1] / 255f;
                    float lmR = lm[lmIdx * 4 + 2] / 255f;
                    float lmShadow = lm[lmIdx * 4 + 3] / 255f; // Alpha channel is shadow/occlusion

                    int texIdx = (x + y * outW) * 4;
                    float texB = texPixels[texIdx + 0] / 255f;
                    float texG = texPixels[texIdx + 1] / 255f;
                    float texR = texPixels[texIdx + 2] / 255f;
                    float texA = texPixels[texIdx + 3] / 255f;

                    // BrowEdit shader blending: (Base * VertexColor * LightmapAlpha) + LightmapRGB; Alpha = BaseAlpha * VertexAlpha
                    // 1. Multiply base by per-tile vertex color (BGRA tint)
                    float baseB = texB * fB;
                    float baseG = texG * fVg;
                    float baseR = texR * fVr;

                    // 2. Apply lightmap alpha as shadow multiplier
                    baseB *= lmShadow;
                    baseG *= lmShadow;
                    baseR *= lmShadow;

                    // 3. Add lightmap RGB (additive term)
                    float finalB = baseB + lmB;
                    float finalG = baseG + lmG;
                    float finalR = baseR + lmR;

                    if (finalB > 1f) finalB = 1f;
                    if (finalG > 1f) finalG = 1f;
                    if (finalR > 1f) finalR = 1f;

                    texPixels[texIdx + 0] = (byte)(finalB * 255f);
                    texPixels[texIdx + 1] = (byte)(finalG * 255f);
                    texPixels[texIdx + 2] = (byte)(finalR * 255f);
                    texPixels[texIdx + 3] = (byte)((texA * fVa) * 255f);
                }
            }

            var result = BitmapSource.Create(outW, outH, 96, 96, PixelFormats.Bgra32, null, texPixels, outW * 4);
            result.Freeze();
            return result;
        }

        /// <summary>
        /// Bakes a lightmap into a texture (full resolution). Use BakeLightmapIntoTextureLowRes for fast path.
        /// Now applies vertex color multiplication and ADDITIVE lightmap logic.
        /// </summary>
        private static BitmapSource BakeLightmapIntoTexture(
            BitmapSource baseTexture, int lightmapIndex, 
            byte vR, byte vG, byte vB, byte vA,
            GndFileV2 gnd)
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

            // Per-tile vertex color (BGRA from GND surface)
            float fVr = vR / 255f;
            float fVg = vG / 255f;
            float fB = vB / 255f;
            float fVa = vA / 255f;

            for (int y = 0; y < texH; y++)
            {
                for (int x = 0; x < texW; x++)
                {
                    int lmX = (x * lmW) / texW; if (lmX >= lmW) lmX = lmW - 1;
                    int lmY = (y * lmH) / texH; if (lmY >= lmH) lmY = lmH - 1;
                    int lmIdx = lmX + lmY * lmW;
                    
                    float lmB = lm[lmIdx * 4 + 0] / 255f;
                    float lmG = lm[lmIdx * 4 + 1] / 255f;
                    float lmR = lm[lmIdx * 4 + 2] / 255f;
                    float lmShadow = lm[lmIdx * 4 + 3] / 255f;

                    int texIdx = (x + y * texW) * 4;
                    float texB = texPixels[texIdx + 0] / 255f;
                    float texG = texPixels[texIdx + 1] / 255f;
                    float texR = texPixels[texIdx + 2] / 255f;
                    float texA = texPixels[texIdx + 3] / 255f;

                    // 1. Base * vertex color (tint)
                    float baseB = texB * fB;
                    float baseG = texG * fVg;
                    float baseR = texR * fVr;

                    // 2. Lightmap alpha = shadow multiplier
                    baseB *= lmShadow;
                    baseG *= lmShadow;
                    baseR *= lmShadow;

                    // 3. Lightmap RGB = additive
                    float finalB = baseB + lmB;
                    float finalG = baseG + lmG;
                    float finalR = baseR + lmR;

                    if (finalB > 1f) finalB = 1f;
                    if (finalG > 1f) finalG = 1f;
                    if (finalR > 1f) finalR = 1f;

                    texPixels[texIdx + 0] = (byte)(finalB * 255f);
                    texPixels[texIdx + 1] = (byte)(finalG * 255f);
                    texPixels[texIdx + 2] = (byte)(finalR * 255f);
                    texPixels[texIdx + 3] = (byte)((texA * fVa) * 255f);
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
