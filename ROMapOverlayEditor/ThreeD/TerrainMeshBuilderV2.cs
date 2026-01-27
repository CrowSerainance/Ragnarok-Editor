// ============================================================================
// TerrainMeshBuilderV2.cs - Terrain Mesh from GND (from rsw_viewer reference)
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
        public bool GenerateColors { get; init; } = false;
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
        private const float DEFAULT_TILE_SIZE = 10f;

        public static TerrainMeshV2 Build(GndFileV2 gnd) => Build(gnd, TerrainMeshOptions.Default);

        public static TerrainMeshV2 Build(GndFileV2 gnd, TerrainMeshOptions options)
        {
            if (gnd == null) throw new ArgumentNullException(nameof(gnd));
            int lodStep = 1 << options.LodLevel;
            float tileSize = gnd.TileScale * lodStep;

            int maxTopFaces = ((gnd.Width + lodStep - 1) / lodStep) * ((gnd.Height + lodStep - 1) / lodStep);
            int maxWallFaces = options.IncludeWalls ? (gnd.Width + gnd.Height) * 2 : 0;
            int maxVertices = (maxTopFaces + maxWallFaces) * 4;
            int maxIndices = (maxTopFaces + maxWallFaces) * 6;

            var positions = new Vector3[maxVertices];
            var indices = new int[maxIndices];
            Vector3[]? normals = options.GenerateNormals ? new Vector3[maxVertices] : null;
            Vector2[]? uvs = options.GenerateUVs ? new Vector2[maxVertices] : null;
            Vector4[]? colors = options.GenerateColors ? new Vector4[maxVertices] : null;

            var ctx = new BuildContext { Gnd = gnd, Options = options, TileSize = tileSize, LodStep = lodStep, Positions = positions, Normals = normals, UVs = uvs, Colors = colors, Indices = indices };

            for (int y = 0; y < gnd.Height; y += lodStep)
                for (int x = 0; x < gnd.Width; x += lodStep)
                {
                    var cube = gnd.Cubes[x, y];
                    if (!cube.HasTopSurface) continue;
                    EmitTopFace(ref ctx, x, y, cube);
                }

            if (options.IncludeWalls)
                for (int y = 0; y < gnd.Height; y += lodStep)
                    for (int x = 0; x < gnd.Width; x += lodStep)
                    {
                        var cube = gnd.Cubes[x, y];
                        if (cube.HasSideWall) EmitSideWall(ref ctx, x, y, cube);
                        if (cube.HasFrontWall) EmitFrontWall(ref ctx, x, y, cube);
                    }

            var boundsMin = new Vector3(float.MaxValue);
            var boundsMax = new Vector3(float.MinValue);
            for (int i = 0; i < ctx.VertexCount; i++)
            {
                boundsMin = Vector3.Min(boundsMin, positions[i]);
                boundsMax = Vector3.Max(boundsMax, positions[i]);
            }

            return new TerrainMeshV2
            {
                Positions = Trim(positions, ctx.VertexCount),
                Normals = normals != null ? Trim(normals, ctx.VertexCount) : null,
                UVs = uvs != null ? Trim(uvs, ctx.VertexCount) : null,
                Colors = colors != null ? Trim(colors, ctx.VertexCount) : null,
                Indices = Trim(indices, ctx.IndexCount),
                MapWidth = gnd.Width,
                MapHeight = gnd.Height,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T[] Trim<T>(T[] a, int len)
        {
            if (a.Length == len) return a;
            var t = new T[len];
            Array.Copy(a, 0, t, 0, len);
            return t;
        }

        private static void EmitTopFace(ref BuildContext ctx, int x, int y, GndCubeV2_Legacy cube)
        {
            float yMult = ctx.Options.FlipYAxis ? -1f : 1f;
            float x0 = x * ctx.Gnd.TileScale, x1 = x0 + ctx.TileSize, z0 = y * ctx.Gnd.TileScale, z1 = z0 + ctx.TileSize;
            float h00 = cube.Height00 * yMult, h10 = cube.Height10 * yMult, h01 = cube.Height01 * yMult, h11 = cube.Height11 * yMult;
            int bv = ctx.VertexCount;

            ctx.Positions[bv + 0] = new Vector3(x0, h00, z0);
            ctx.Positions[bv + 1] = new Vector3(x1, h10, z0);
            ctx.Positions[bv + 2] = new Vector3(x0, h01, z1);
            ctx.Positions[bv + 3] = new Vector3(x1, h11, z1);

            if (ctx.UVs != null)
            {
                if (cube.TileUp >= 0 && cube.TileUp < ctx.Gnd.Surfaces.Count)
                {
                    var s = ctx.Gnd.Surfaces[cube.TileUp];
                    ctx.UVs[bv + 0] = new Vector2(s.U1, s.V1); ctx.UVs[bv + 1] = new Vector2(s.U2, s.V2);
                    ctx.UVs[bv + 2] = new Vector2(s.U3, s.V3); ctx.UVs[bv + 3] = new Vector2(s.U4, s.V4);
                }
                else
                {
                    ctx.UVs[bv + 0] = new Vector2(0, 0); ctx.UVs[bv + 1] = new Vector2(1, 0);
                    ctx.UVs[bv + 2] = new Vector2(0, 1); ctx.UVs[bv + 3] = new Vector2(1, 1);
                }
            }

            if (ctx.Normals != null)
            {
                var e1 = ctx.Positions[bv + 1] - ctx.Positions[bv + 0];
                var e2 = ctx.Positions[bv + 2] - ctx.Positions[bv + 0];
                var n = Vector3.Normalize(Vector3.Cross(e1, e2));
                ctx.Normals[bv + 0] = ctx.Normals[bv + 1] = ctx.Normals[bv + 2] = ctx.Normals[bv + 3] = n;
            }

            if (ctx.Colors != null)
            {
                Vector4 c = Vector4.One;
                if (cube.TileUp >= 0 && cube.TileUp < ctx.Gnd.Surfaces.Count)
                {
                    var s = ctx.Gnd.Surfaces[cube.TileUp];
                    c = new Vector4(s.R / 255f, s.G / 255f, s.B / 255f, s.A / 255f);
                }
                ctx.Colors[bv + 0] = ctx.Colors[bv + 1] = ctx.Colors[bv + 2] = ctx.Colors[bv + 3] = c;
            }

            int bi = ctx.IndexCount;
            ctx.Indices[bi + 0] = bv + 0; ctx.Indices[bi + 1] = bv + 1; ctx.Indices[bi + 2] = bv + 2;
            ctx.Indices[bi + 3] = bv + 2; ctx.Indices[bi + 4] = bv + 1; ctx.Indices[bi + 5] = bv + 3;
            ctx.VertexCount += 4;
            ctx.IndexCount += 6;
        }

        private static void EmitSideWall(ref BuildContext ctx, int x, int y, GndCubeV2_Legacy cube) { /* Stub: east wall */ }
        private static void EmitFrontWall(ref BuildContext ctx, int x, int y, GndCubeV2_Legacy cube) { /* Stub: south wall */ }

        private ref struct BuildContext
        {
            public GndFileV2 Gnd;
            public TerrainMeshOptions Options;
            public float TileSize;
            public int LodStep;
            public Vector3[] Positions;
            public Vector3[]? Normals;
            public Vector2[]? UVs;
            public Vector4[]? Colors;
            public int[] Indices;
            public int VertexCount;
            public int IndexCount;
        }
    }
}
