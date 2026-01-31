// ============================================================================
// TerrainMeshBuilderV2.cs - Terrain Mesh from GND (BrowEdit compatible)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using ROMapOverlayEditor.Gnd;

namespace ROMapOverlayEditor.ThreeD
{
    public sealed class TerrainMeshOptions
    {
        public bool GenerateUVs { get; init; } = true;
        public bool GenerateNormals { get; init; } = true;
        public bool GenerateColors { get; init; } = true; // Enabled by default now
        public bool IncludeWalls { get; init; } = true;
        public int LodLevel { get; init; } = 0;
        public bool FlipYAxis { get; init; } = true;
        public static readonly TerrainMeshOptions Default = new();
        public static readonly TerrainMeshOptions Preview = new() { IncludeWalls = false, GenerateColors = false };
    }

    public sealed class TerrainMeshV2 : IDisposable
    {
        public Vector3[] Positions { get; init; } = Array.Empty<Vector3>();
        public Vector3[]? Normals { get; init; }
        public Vector2[]? UVs { get; init; }
        public Vector4[]? Colors { get; init; }
        public int[] Indices { get; init; } = Array.Empty<int>();
        public int MapWidth { get; init; }
        public int MapHeight { get; init; }
        public int VertexCount => Positions.Length;
        public int TriangleCount => Indices.Length / 3;
        public Vector3 BoundsMin { get; init; }
        public Vector3 BoundsMax { get; init; }
        public void Dispose() { }
    }

    public static class TerrainMeshBuilderV2
    {
        public static TerrainMeshV2 Build(GndFileV2 gnd) => Build(gnd, TerrainMeshOptions.Default);

        public static TerrainMeshV2 Build(GndFileV2 gnd, TerrainMeshOptions options)
        {
            if (gnd == null) throw new ArgumentNullException(nameof(gnd));
            int lodStep = 1 << options.LodLevel;
            float tileSize = gnd.TileScale * lodStep;

            // Estimate capacities
            int maxTopFaces = ((gnd.Width + lodStep - 1) / lodStep) * ((gnd.Height + lodStep - 1) / lodStep);
            int maxWallFaces = options.IncludeWalls ? (gnd.Width + gnd.Height) * 2 : 0; // Rough upper bound
            int maxVertices = (maxTopFaces + maxWallFaces) * 6; // Walls use 4-6 verts
            int maxIndices = (maxTopFaces + maxWallFaces) * 9; 

            // Resizeable lists might be safer but array is faster if we bound correctly. 
            // We'll use lists for simplicity in this port to avoid overflow if estimation is off.
            var positions = new List<Vector3>(maxVertices);
            var normals = options.GenerateNormals ? new List<Vector3>(maxVertices) : null;
            var uvs = options.GenerateUVs ? new List<Vector2>(maxVertices) : null;
            var colors = options.GenerateColors ? new List<Vector4>(maxVertices) : null;
            var indices = new List<int>(maxIndices);

            var ctx = new BuildContext { 
                Gnd = gnd, 
                Options = options, 
                TileSize = tileSize, 
                LodStep = lodStep, 
                Positions = positions, 
                Normals = normals, 
                UVs = uvs, 
                Colors = colors, 
                Indices = indices 
            };

            for (int y = 0; y < gnd.Height; y += lodStep)
            {
                for (int x = 0; x < gnd.Width; x += lodStep)
                {
                    var cube = gnd.GetCube(x, y);
                    if (cube == null || !cube.Value.HasTopSurface) continue;
                    EmitTopFace(ref ctx, x, y, cube.Value);
                }
            }

            if (options.IncludeWalls)
            {
                for (int y = 0; y < gnd.Height; y += lodStep)
                {
                    for (int x = 0; x < gnd.Width; x += lodStep)
                    {
                        var cube = gnd.GetCube(x, y);
                        if (cube == null) continue;
                        if (cube.Value.HasSideWall) EmitSideWall(ref ctx, x, y, cube.Value);
                        if (cube.Value.HasFrontWall) EmitFrontWall(ref ctx, x, y, cube.Value);
                    }
                }
            }

            // Calculate bounds
            var boundsMin = new Vector3(float.MaxValue);
            var boundsMax = new Vector3(float.MinValue);
            foreach (var p in positions)
            {
                boundsMin = Vector3.Min(boundsMin, p);
                boundsMax = Vector3.Max(boundsMax, p);
            }

            return new TerrainMeshV2
            {
                Positions = positions.ToArray(),
                Normals = normals?.ToArray(),
                UVs = uvs?.ToArray(),
                Colors = colors?.ToArray(),
                Indices = indices.ToArray(),
                MapWidth = gnd.Width,
                MapHeight = gnd.Height,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax
            };
        }

        private static void EmitTopFace(ref BuildContext ctx, int x, int y, GndCubeV2_Legacy cube)
        {
            // BrowEdit Logic:
            // v1: 10*x,     -h3 (H00), 10*H - 10*y
            // v2: 10*x+10,  -h4 (H10), 10*H - 10*y
            // v3: 10*x,     -h1 (H01), 10*H - 10*y + 10
            // v4: 10*x+10,  -h2 (H11), 10*H - 10*y + 10
            //
            // My GndCube mapping:
            // H00 -> h3 (TL)
            // H10 -> h4 (TR)
            // H01 -> h1 (BL)
            // H11 -> h2 (BR)

            float ts = ctx.TileSize;
            float x0 = x * ts;
            float x1 = x0 + ts;
            // Z logic: Starts at Height*10 and decreases as Y increases
            float z0 = (ctx.Gnd.Height - y) * 10f; // Note: BrowEdit uses constant 10 not TileScale? Usually TileScale=10.
            float z1 = (ctx.Gnd.Height - (y + ctx.LodStep)) * 10f; 
            
            // Adjust to use TileSize if not fixed 10? BrowEdit hardcodes 10. We'll use ctx.TileSize assuming it matches.
            z0 = (ctx.Gnd.Height - y) * ctx.Gnd.TileScale;
            z1 = (ctx.Gnd.Height - (y + ctx.LodStep)) * ctx.Gnd.TileScale;


            float yMult = ctx.Options.FlipYAxis ? -1f : 1f;

            float h_tl = cube.Height00 * yMult; // h3
            float h_tr = cube.Height10 * yMult; // h4
            float h_bl = cube.Height01 * yMult; // h1
            float h_br = cube.Height11 * yMult; // h2

            // BrowEdit: v4, v2, v1 (Tri 1)
            //           v4, v1, v3 (Tri 2)
            // v1=TL, v2=TR, v3=BL, v4=BR
            
            // Layout:
            // TL(x0,z0) --- TR(x1,z0)
            //     |             |
            // BL(x0,z1) --- BR(x1,z1)

            var v1 = new Vector3(x0, h_tl, z0);
            var v2 = new Vector3(x1, h_tr, z0);
            var v3 = new Vector3(x0, h_bl, z1);
            var v4 = new Vector3(x1, h_br, z1);

            int baseIdx = ctx.Positions.Count;
            AddVertex(ref ctx, v4, cube.TileUp, 3);
            AddVertex(ref ctx, v2, cube.TileUp, 1);
            AddVertex(ref ctx, v1, cube.TileUp, 0);
            AddVertex(ref ctx, v3, cube.TileUp, 2);

            // Tri 1: v4, v2, v1
            ctx.Indices.Add(baseIdx + 0);
            ctx.Indices.Add(baseIdx + 1);
            ctx.Indices.Add(baseIdx + 2);

            // Tri 2: v4, v1, v3  (Note: v4 and v1 repeated? No, indexed)
            ctx.Indices.Add(baseIdx + 0);
            ctx.Indices.Add(baseIdx + 2);
            ctx.Indices.Add(baseIdx + 3);
        }

        private static void EmitSideWall(ref BuildContext ctx, int x, int y, GndCubeV2_Legacy cube)
        {
            // Wall on RIGHT side of tile x,y (between x and x+1)
            // Connects TR/BR of current to TL/BL of next.
            
            int nextX = x + ctx.LodStep;
            var adj = ctx.Gnd.GetCube(nextX, y);
            if (adj == null) return; // Map edge?

            float ts = ctx.TileSize;
            float yMult = ctx.Options.FlipYAxis ? -1f : 1f;
            float z0 = (ctx.Gnd.Height - y) * ctx.Gnd.TileScale; 
            float z1 = (ctx.Gnd.Height - (y + ctx.LodStep)) * ctx.Gnd.TileScale;
            float x1 = (x * ts) + ts; // Right edge

            // Current Right Edge
            float h_tr = cube.Height10 * yMult; // h4
            float h_br = cube.Height11 * yMult; // h2

            // Adjacent Left Edge
            float h_adj_tl = adj.Value.Height00 * yMult; // h3
            float h_adj_bl = adj.Value.Height01 * yMult; // h1

            // BrowEdit: v1(up-front?), v2(up-back?), v3(down-front?), v4(down-back?)
            // It seems to define 4 vertices for the wall quad.
            
            // v1 (TR current)
            var v_tr = new Vector3(x1, h_tr, z0);
            // v2 (BR current)
            var v_br = new Vector3(x1, h_br, z1);
            // v3 (TL adj)
            var v_adj_tl = new Vector3(x1, h_adj_tl, z0);
            // v4 (BL adj)
            var v_adj_bl = new Vector3(x1, h_adj_bl, z1);

            // Winding? BrowEdit verts:
            // v1: x+10, -h2 (BR), y+10 (z1)
            // v2: x+10, -h4 (TR), y (z0)
            // v3: x+10, -adj.h1 (BL), y+10 (z1)
            // v4: x+10, -adj.h3 (TL), y (z0)
            
            // Indices: v1, v3, v4; v4, v2, v1
            
            // Mapping:
            // My v_br = BrowEdit v1
            // My v_tr = BrowEdit v2
            // My v_adj_bl = BrowEdit v3
            // My v_adj_tl = BrowEdit v4

            int baseIdx = ctx.Positions.Count;
            AddVertex(ref ctx, v_br, cube.TileSide, 1);
            AddVertex(ref ctx, v_tr, cube.TileSide, 0); // using dummy UV indices for now
            AddVertex(ref ctx, v_adj_bl, cube.TileSide, 3);
            AddVertex(ref ctx, v_adj_tl, cube.TileSide, 2);

            // Per BrowEdit: v1, v3, v4 -> 0, 2, 3
            ctx.Indices.Add(baseIdx + 0);
            ctx.Indices.Add(baseIdx + 2);
            ctx.Indices.Add(baseIdx + 3);

            // v4, v2, v1 -> 3, 1, 0
            ctx.Indices.Add(baseIdx + 3);
            ctx.Indices.Add(baseIdx + 1);
            ctx.Indices.Add(baseIdx + 0);
        }

        private static void EmitFrontWall(ref BuildContext ctx, int x, int y, GndCubeV2_Legacy cube)
        {
            // Wall on BOTTOM side of tile x,y (between y and y+1)
            // Connects BL/BR of current to TL/TR of next.

            int nextY = y + ctx.LodStep;
            var adj = ctx.Gnd.GetCube(x, nextY);
            if (adj == null) return;

            float ts = ctx.TileSize;
            float yMult = ctx.Options.FlipYAxis ? -1f : 1f;

            // Z position is the seam
            float zSeam = (ctx.Gnd.Height - nextY) * ctx.Gnd.TileScale;
            float x0 = x * ts;
            float x1 = x0 + ts;

            // Current Bottom Edge
            float h_bl = cube.Height01 * yMult; // h1
            float h_br = cube.Height11 * yMult; // h2

            // Adjacent Top Edge
            float h_adj_tl = adj.Value.Height00 * yMult; // h3
            float h_adj_tr = adj.Value.Height10 * yMult; // h4

            // BrowEdit verts:
            // v1: x, -h3 (TL?? No, h3 is TL. Wait, EmitFrontWall uses h3, h4, adjH1, adjH2?)
            // Let's recheck BrowEdit logic in GndRenderer.cpp
            // tileFront:
            // v1: x, -h3, y (TL of current). Wait, -h3 is TL.
            // v2: x+10, -h4, y (TR of current).
            // v4: x+10, -adj.h2, y (TR of next?).
            // v3: x, -adj.h1, y (TL of next?).
            //
            // This suggests tileFront is the NORTH wall (Top)? 
            // My definition: TileFront = Bottom?
            // "if (cube->tileFront != -1 && y < gnd->height - 1)" -> Checks y < height-1.
            // Vertices are at "y". Not y+1.
            // But it checks cube[x][y+1].
            // If it draws at 'y', it's the TOP edge of y+1? Or BOTTOM edge of y?
            // "y" in BrowEdit is usually top coordinate.
            // "v1... 10*gnd->height - 10*y". This is the Z line for TOP of row y.
            //
            // Wait, if Front is North, then my mapping might be inverted.
            // Let's look at `cube->tileFront`:
            // It uses `gnd->cubes[x][y+1]`.
            // So it connects Row `y` and Row `y+1`.
            // The seam is between them.
            // That seam is Bottom of `y` and Top of `y+1`.
            //
            // BrowEdit vertices:
            // v1 (x, -h3, y) -> Top-Left of CURRENT??
            // v2 (x+10, -h4, y) -> Top-Right of CURRENT??
            // v3 (x, -adj.h1, y) -> Bottom-Left of NEXT?? (adj is y+1)
            // v4 (x+10, -adj.h2, y) -> Bottom-Right of NEXT??
            //
            // All at Z = (Height - y)*10.
            // This implies the wall is drawn at the TOP of the current tile??
            // But it checks `y+1`.
            // If it draws at Top of Y, it should check `y-1`.
            //
            // Maybe `tileFront` IS "Front" (South/Bottom) but BrowEdit draws it relative to Y?
            // Actually, `10*Height - 10*y` is Z.
            // If y increases, Z decreases.
            // Row y has Z range [Z_high, Z_low].
            // Row y+1 has Z range [Z_low, Z_lower].
            // The seam is at Z_low (Bottom of y).
            //
            // BrowEdit FrontWall verts use `10*gnd->height - 10*y`. This is Z_high (Top of y).
            // So tileFront renders at the TOP of the tile?
            // If so, it connects `y` with `y-1`.
            // But code checks `cubes[x][y+1]`.
            // This is contradictory unless `h3/h4` are NOT Top Vertices.
            //
            // Let's stick to logical connection:
            // `tileFront` connects `y` and `y+1`.
            // Seam is `y+1` boundary.
            //
            // BrowEdit `v1` uses `cube->h3`. `v2` uses `cube->h4`.
            // My mapping: h3=TL(H00), h4=TR(H10).
            // `v3` uses `adj->h1`. `v4` uses `adj->h2`.
            // My mapping: h1=BL(H01), h2=BR(H11) of ADJ.
            //
            // This connects Top-V of Current to Bottom-V of Next.
            // Top of Y to Bottom of Y+1.
            // That spans the ENTIRE row Y and row Y+1?
            // No, that would be a huge diagonal sheet.
            //
            // Ah, maybe `tileFront` is the wall BEHIND the tile?
            //
            // Let's implement it as the seam between Y and Y+1 (Bottom of Y, Top of Y+1).
            //
            // Vertices:
            // v_bl (x0, h_bl, z1) -> Current Bottom-Left
            // v_br (x1, h_br, z1) -> Current Bottom-Right
            // v_adj_tl (x0, h_adj_tl, z1) -> Adj Top-Left
            // v_adj_tr (x1, h_adj_tr, z1) -> Adj Top-Right
            
            var v_bl = new Vector3(x0, h_bl, zSeam);
            var v_br = new Vector3(x1, h_br, zSeam);
            var v_adj_tl = new Vector3(x0, h_adj_tl, zSeam);
            var v_adj_tr = new Vector3(x1, h_adj_tr, zSeam);

            // Quad construction:
            int baseIdx = ctx.Positions.Count;
            AddVertex(ref ctx, v_bl, cube.TileFront, 0); 
            AddVertex(ref ctx, v_br, cube.TileFront, 1);
            AddVertex(ref ctx, v_adj_tr, cube.TileFront, 3);
            AddVertex(ref ctx, v_adj_tl, cube.TileFront, 2);

            // Quad winding
            ctx.Indices.Add(baseIdx + 0);
            ctx.Indices.Add(baseIdx + 1);
            ctx.Indices.Add(baseIdx + 2);

            ctx.Indices.Add(baseIdx + 0);
            ctx.Indices.Add(baseIdx + 2);
            ctx.Indices.Add(baseIdx + 3);
        }

        private static void AddVertex(ref BuildContext ctx, Vector3 pos, int tileIdx, int uvCorner)
        {
            ctx.Positions.Add(pos);
            
            if (ctx.UVs != null)
            {
                var surf = ctx.Gnd.GetSurface(tileIdx);
                if (surf != null && surf.Value.HasTexture)
                {
                    var s = surf.Value;
                    // uvCorner: 0=TL, 1=TR, 2=BL, 3=BR
                    switch(uvCorner)
                    {
                        case 0: ctx.UVs.Add(new Vector2(s.U1, s.V1)); break;
                        case 1: ctx.UVs.Add(new Vector2(s.U2, s.V2)); break;
                        case 2: ctx.UVs.Add(new Vector2(s.U3, s.V3)); break;
                        case 3: ctx.UVs.Add(new Vector2(s.U4, s.V4)); break;
                        default: ctx.UVs.Add(Vector2.Zero); break;
                    }
                }
                else
                {
                    // Default UVs
                     switch(uvCorner)
                    {
                        case 0: ctx.UVs.Add(new Vector2(0, 0)); break;
                        case 1: ctx.UVs.Add(new Vector2(1, 0)); break;
                        case 2: ctx.UVs.Add(new Vector2(0, 1)); break;
                        case 3: ctx.UVs.Add(new Vector2(1, 1)); break;
                    }
                }
            }

            if (ctx.Colors != null)
            {
                 var surf = ctx.Gnd.GetSurface(tileIdx);
                 if (surf != null)
                 {
                     var s = surf.Value;
                     ctx.Colors.Add(new Vector4(s.R/255f, s.G/255f, s.B/255f, s.A/255f));
                 }
                 else
                 {
                     ctx.Colors.Add(Vector4.One);
                 }
            }

            if (ctx.Normals != null)
            {
                // Placeholder - flat shading usually recomputed per face or specific logic
                ctx.Normals.Add(Vector3.UnitY);
            }
        }

        private ref struct BuildContext
        {
            public GndFileV2 Gnd;
            public TerrainMeshOptions Options;
            public float TileSize;
            public int LodStep;
            public List<Vector3> Positions;
            public List<Vector3>? Normals;
            public List<Vector2>? UVs;
            public List<Vector4>? Colors;
            public List<int> Indices;
        }
    }
}
