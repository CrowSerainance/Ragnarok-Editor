// ============================================================================
// GndFileV2.cs - Optimized GND Data Models
// ============================================================================
// PURPOSE: Immutable data structures for GND (Ground) terrain files
// INTEGRATION: Drop into ROMapOverlayEditor/Gnd/ folder
// NOTES: Based on BrowEdit3 format with full texture/lightmap support
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ROMapOverlayEditor.Gnd
{
    /// <summary>
    /// GND (Ground) file containing terrain mesh, textures, and lightmaps.
    /// </summary>
    public sealed class GndFileV2
    {
        // ====================================================================
        // VERSION CONSTANTS
        // ====================================================================
        
        /// <summary>Version where cube tile IDs became int32 instead of uint16</summary>
        public const ushort VERSION_INT_TILE_IDS = 0x0106;
        
        /// <summary>Version where water info was moved from RSW</summary>
        public const ushort VERSION_WATER_INFO = 0x0107;

        // ====================================================================
        // PROPERTIES
        // ====================================================================
        
        /// <summary>Version as 0xMMmm (major.minor)</summary>
        public ushort Version { get; init; }
        
        /// <summary>Map width in tiles</summary>
        public int Width { get; init; }
        
        /// <summary>Map height in tiles</summary>
        public int Height { get; init; }
        
        /// <summary>Tile scale factor (typically 10.0)</summary>
        public float TileScale { get; init; }
        
        /// <summary>Total tile count (Width * Height)</summary>
        public int TileCount => Width * Height;

        // --------------------------------------------------------------------
        // Textures
        // --------------------------------------------------------------------
        
        /// <summary>Texture file references</summary>
        public IReadOnlyList<GndTexture> Textures { get; init; } = Array.Empty<GndTexture>();

        // --------------------------------------------------------------------
        // Lightmaps
        // --------------------------------------------------------------------
        
        /// <summary>Lightmap configuration</summary>
        public GndLightmapInfo Lightmaps { get; init; } = new();

        // --------------------------------------------------------------------
        // Surface Tiles
        // --------------------------------------------------------------------
        
        /// <summary>Surface tile definitions (UV, texture, color)</summary>
        public IReadOnlyList<GndSurfaceTile> Surfaces { get; init; } = Array.Empty<GndSurfaceTile>();

        // --------------------------------------------------------------------
        // Cubes (Height Grid)
        // --------------------------------------------------------------------
        
        /// <summary>
        /// 2D array of terrain cubes [x, y].
        /// Each cube contains 4 corner heights and surface references.
        /// </summary>
        public GndCubeV2[,] Cubes { get; init; } = new GndCubeV2[0, 0];

        // --------------------------------------------------------------------
        // Water (version >= 0x0107)
        // --------------------------------------------------------------------
        
        /// <summary>Water configuration (null for older versions)</summary>
        public GndWaterInfo? Water { get; init; }

        // ====================================================================
        // HELPER METHODS
        // ====================================================================
        
        /// <summary>Get cube at tile coordinates (with bounds check)</summary>
        public GndCubeV2? GetCube(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                return null;
            return Cubes[x, y];
        }
        
        /// <summary>Get surface tile by index</summary>
        public GndSurfaceTile? GetSurface(int index)
        {
            if (index < 0 || index >= Surfaces.Count)
                return null;
            return Surfaces[index];
        }
        
        /// <summary>Get height at world position (interpolated)</summary>
        public float GetHeightAt(float worldX, float worldZ)
        {
            // Convert world coords to tile coords
            float tileX = worldX / TileScale;
            float tileZ = worldZ / TileScale;
            
            int x = (int)tileX;
            int y = (int)tileZ;
            
            if (x < 0 || x >= Width - 1 || y < 0 || y >= Height - 1)
                return 0;
            
            // Get fractional position within tile
            float fx = tileX - x;
            float fz = tileZ - y;
            
            var cube = Cubes[x, y];
            
            // Bilinear interpolation of heights
            float h00 = cube.Height00;
            float h10 = cube.Height10;
            float h01 = cube.Height01;
            float h11 = cube.Height11;
            
            float h0 = h00 + (h10 - h00) * fx;
            float h1 = h01 + (h11 - h01) * fx;
            
            return h0 + (h1 - h0) * fz;
        }

        public override string ToString()
            => $"GND v{Version >> 8}.{Version & 0xFF} {Width}x{Height} " +
               $"textures={Textures.Count} surfaces={Surfaces.Count}";
    }

    // ========================================================================
    // TEXTURE
    // ========================================================================
    
    /// <summary>
    /// Texture file reference.
    /// </summary>
    public sealed class GndTexture
    {
        /// <summary>Texture filename (e.g., "prontera\\texture.bmp")</summary>
        public string Filename { get; init; } = string.Empty;
        
        /// <summary>Texture name (often same as filename)</summary>
        public string Name { get; init; } = string.Empty;
        
        public override string ToString() => Filename;
    }

    // ========================================================================
    // LIGHTMAP INFO
    // ========================================================================
    
    /// <summary>
    /// Lightmap configuration and data.
    /// </summary>
    public sealed class GndLightmapInfo
    {
        /// <summary>Number of lightmap entries</summary>
        public int Count { get; init; }
        
        /// <summary>Lightmap cell width</summary>
        public int CellWidth { get; init; }
        
        /// <summary>Lightmap cell height</summary>
        public int CellHeight { get; init; }
        
        /// <summary>Grid size per cell</summary>
        public int GridSizeCell { get; init; }
        
        /// <summary>
        /// Raw lightmap data. Each entry is 256 bytes:
        /// - 64 bytes: 8x8 shadow intensity (1 byte each)
        /// - 192 bytes: 8x8 color (3 bytes RGB each)
        /// </summary>
        public byte[]? RawData { get; init; }
        
        /// <summary>Get lightmap entry by index (256 bytes)</summary>
        public ReadOnlySpan<byte> GetLightmap(int index)
        {
            if (RawData == null || index < 0 || index >= Count)
                return ReadOnlySpan<byte>.Empty;
            
            int offset = index * 256;
            return new ReadOnlySpan<byte>(RawData, offset, 256);
        }
    }

    // ========================================================================
    // SURFACE TILE
    // ========================================================================
    
    /// <summary>
    /// Surface tile definition with UV coordinates and color.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct GndSurfaceTile
    {
        // UV coordinates for 4 corners (counter-clockwise from bottom-left)
        public readonly float U1, U2, U3, U4;
        public readonly float V1, V2, V3, V4;
        
        /// <summary>Index into texture array (-1 = no texture)</summary>
        public readonly short TextureIndex;
        
        /// <summary>Index into lightmap array (-1 = no lightmap)</summary>
        public readonly ushort LightmapIndex;
        
        /// <summary>Vertex color (BGRA format as stored in file)</summary>
        public readonly byte B, G, R, A;
        
        public GndSurfaceTile(
            float u1, float u2, float u3, float u4,
            float v1, float v2, float v3, float v4,
            short textureIndex, ushort lightmapIndex,
            byte b, byte g, byte r, byte a)
        {
            U1 = u1; U2 = u2; U3 = u3; U4 = u4;
            V1 = v1; V2 = v2; V3 = v3; V4 = v4;
            TextureIndex = textureIndex;
            LightmapIndex = lightmapIndex;
            B = b; G = g; R = r; A = a;
        }
        
        /// <summary>Get color as 32-bit ARGB integer</summary>
        public int ColorArgb => (A << 24) | (R << 16) | (G << 8) | B;
        
        /// <summary>Check if this surface has a texture</summary>
        public bool HasTexture => TextureIndex >= 0;
        
        public override string ToString()
            => $"Surface tex={TextureIndex} lm={LightmapIndex} color=#{R:X2}{G:X2}{B:X2}";
    }

    // ========================================================================
    // CUBE (HEIGHT CELL)
    // ========================================================================
    
    /// <summary>
    /// Terrain cube containing 4 corner heights and surface references.
    /// Heights are typically negative (below origin).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct GndCubeV2
    {
        /// <summary>Height at corner (0,0) - bottom-left</summary>
        public readonly float Height00;
        
        /// <summary>Height at corner (1,0) - bottom-right</summary>
        public readonly float Height10;
        
        /// <summary>Height at corner (0,1) - top-left</summary>
        public readonly float Height01;
        
        /// <summary>Height at corner (1,1) - top-right</summary>
        public readonly float Height11;
        
        /// <summary>Top surface tile index (-1 = none)</summary>
        public readonly int TileUp;
        
        /// <summary>Side (east) surface tile index (-1 = none)</summary>
        public readonly int TileSide;
        
        /// <summary>Front (south) surface tile index (-1 = none)</summary>
        public readonly int TileFront;
        
        public GndCubeV2(
            float h00, float h10, float h01, float h11,
            int tileUp, int tileSide, int tileFront)
        {
            Height00 = h00;
            Height10 = h10;
            Height01 = h01;
            Height11 = h11;
            TileUp = tileUp;
            TileSide = tileSide;
            TileFront = tileFront;
        }
        
        /// <summary>Average height of all 4 corners</summary>
        public float AverageHeight => (Height00 + Height10 + Height01 + Height11) / 4f;
        
        /// <summary>Check if this cube has a walkable top surface</summary>
        public bool HasTopSurface => TileUp >= 0;
        
        /// <summary>Check if this cube has an east wall</summary>
        public bool HasSideWall => TileSide >= 0;
        
        /// <summary>Check if this cube has a south wall</summary>
        public bool HasFrontWall => TileFront >= 0;
        
        public override string ToString()
            => $"Cube h=({Height00:F1},{Height10:F1},{Height01:F1},{Height11:F1}) " +
               $"tiles=({TileUp},{TileSide},{TileFront})";
    }

    // ========================================================================
    // WATER INFO (GND v1.7+)
    // ========================================================================
    
    /// <summary>
    /// Water configuration stored in GND (version >= 0x0107).
    /// </summary>
    public sealed class GndWaterInfo
    {
        /// <summary>Water surface height</summary>
        public float Height { get; init; }
        
        /// <summary>Water texture type</summary>
        public int Type { get; init; }
        
        /// <summary>Wave amplitude</summary>
        public float Amplitude { get; init; }
        
        /// <summary>Wave speed</summary>
        public float WaveSpeed { get; init; }
        
        /// <summary>Wave pitch</summary>
        public float WavePitch { get; init; }
        
        /// <summary>Texture animation speed</summary>
        public int AnimationSpeed { get; init; }
    }
}
