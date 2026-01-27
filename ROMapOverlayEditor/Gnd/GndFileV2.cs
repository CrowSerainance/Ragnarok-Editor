// ============================================================================
// GndFileV2.cs - Optimized GND Data Models (from rsw_viewer reference)
// ============================================================================
// PURPOSE: Immutable data structures for GND (Ground) terrain files
// NOTES: Based on BrowEdit3 format with full texture/lightmap support
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ROMapOverlayEditor.Gnd
{
    public sealed class GndFileV2
    {
        public const ushort VERSION_INT_TILE_IDS = 0x0106;
        public const ushort VERSION_WATER_INFO = 0x0107;

        public ushort Version { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public float TileScale { get; init; }
        public int TileCount => Width * Height;

        /// <summary>Texture file references (V2 type to avoid clash with GndTexture)</summary>
        public IReadOnlyList<GndTextureV2> Textures { get; init; } = Array.Empty<GndTextureV2>();
        public GndLightmapInfo Lightmaps { get; init; } = new();
        public IReadOnlyList<GndSurfaceTile> Surfaces { get; init; } = Array.Empty<GndSurfaceTile>();
        public GndCubeV2_Legacy[,] Cubes { get; init; } = new GndCubeV2_Legacy[0, 0];
        public GndWaterInfo? Water { get; init; }

        public GndCubeV2_Legacy? GetCube(int x, int y) => (x >= 0 && x < Width && y >= 0 && y < Height) ? Cubes[x, y] : null;
        public GndSurfaceTile? GetSurface(int index) => (index >= 0 && index < Surfaces.Count) ? Surfaces[index] : null;

        public override string ToString() => $"GND v{Version >> 8}.{Version & 0xFF} {Width}x{Height} textures={Textures.Count} surfaces={Surfaces.Count}";
    }

    /// <summary>Texture reference for GndFileV2 (avoids clash with Gnd.GndTexture).</summary>
    public sealed class GndTextureV2
    {
        public string Filename { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
    }

    public sealed class GndLightmapInfo
    {
        public int Count { get; init; }
        public int CellWidth { get; init; }
        public int CellHeight { get; init; }
        public int GridSizeCell { get; init; }
        public byte[]? RawData { get; init; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct GndSurfaceTile
    {
        public readonly float U1, U2, U3, U4, V1, V2, V3, V4;
        public readonly short TextureIndex;
        public readonly ushort LightmapIndex;
        public readonly byte B, G, R, A;

        public GndSurfaceTile(float u1, float u2, float u3, float u4, float v1, float v2, float v3, float v4, short textureIndex, ushort lightmapIndex, byte b, byte g, byte r, byte a)
        {
            U1 = u1; U2 = u2; U3 = u3; U4 = u4; V1 = v1; V2 = v2; V3 = v3; V4 = v4;
            TextureIndex = textureIndex; LightmapIndex = lightmapIndex; B = b; G = g; R = r; A = a;
        }
        public bool HasTexture => TextureIndex >= 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct GndCubeV2_Legacy
    {
        public readonly float Height00, Height10, Height01, Height11;
        public readonly int TileUp, TileSide, TileFront;

        public GndCubeV2_Legacy(float h00, float h10, float h01, float h11, int tileUp, int tileSide, int tileFront)
        {
            Height00 = h00; Height10 = h10; Height01 = h01; Height11 = h11;
            TileUp = tileUp; TileSide = tileSide; TileFront = tileFront;
        }
        public float AverageHeight => (Height00 + Height10 + Height01 + Height11) / 4f;
        public bool HasTopSurface => TileUp >= 0;
        public bool HasSideWall => TileSide >= 0;
        public bool HasFrontWall => TileFront >= 0;
    }

    public sealed class GndWaterInfo
    {
        public float Height { get; init; }
        public int Type { get; init; }
        public float Amplitude { get; init; }
        public float WaveSpeed { get; init; }
        public float WavePitch { get; init; }
        public int AnimationSpeed { get; init; }
    }
}
