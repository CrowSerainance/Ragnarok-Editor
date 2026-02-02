// =============================================================================
// GndFileV2.cs - Ground/Terrain Data Model for Ragnarok Online GND Files
// =============================================================================
// This file defines the data structures that hold parsed GND (ground) file data.
// GND files contain terrain heightmaps, texture references, lightmaps, and surface
// tile definitions used to render the 3D ground in Ragnarok Online maps.
//
// IMPORTANT: This version uses MUTABLE collections (List<T>) instead of immutable
// ones (IReadOnlyList<T>) because GndReaderV2.cs needs to populate these collections
// during the parsing process using .Add() method calls.
//
// Reference: BrowEdit3's Gnd.h/Gnd.cpp for format details
// =============================================================================

using System;
using System.Collections.Generic;

namespace ROMapOverlayEditor.Gnd
{
    /// <summary>
    /// Represents a complete GND (ground) file containing all terrain data.
    /// This is the root container for all ground/terrain information in a Ragnarok Online map.
    /// </summary>
    /// <remarks>
    /// GND File Structure (simplified):
    /// 1. Header: Magic "GRGN" + version + dimensions
    /// 2. Textures: List of texture filenames (80 bytes each: 40 file + 40 name)
    /// 3. Lightmaps: Baked lighting data for each cell
    /// 4. Surfaces: UV coordinates, texture indices, and colors for tile faces
    /// 5. Cubes: Height values and surface references for each terrain cell
    /// </remarks>
    public sealed class GndFileV2
    {
        // =================================================================
        // VERSION CONSTANTS
        // =================================================================
        // These constants identify when certain features were added to the GND format
        
        /// <summary>
        /// Version 0x0106 (1.6): Tile indices changed from short (2 bytes) to int (4 bytes).
        /// This allows more than 32,767 surface tiles per map.
        /// </summary>
        public const ushort VERSION_INT_TILE_IDS = 0x0106;
        
        /// <summary>
        /// Version 0x0107 (1.7): Water information was added to the GND file.
        /// Earlier versions stored water data in the RSW file instead.
        /// </summary>
        public const ushort VERSION_WATER_INFO = 0x0107;

        // =================================================================
        // FILE HEADER PROPERTIES
        // =================================================================
        
        /// <summary>
        /// GND file version as a packed ushort.
        /// High byte = major version, Low byte = minor version.
        /// Example: 0x0107 = version 1.7
        /// </summary>
        /// <remarks>
        /// Common versions:
        /// - 0x0103 (1.3): Early format
        /// - 0x0106 (1.6): 32-bit tile indices
        /// - 0x0107 (1.7): Embedded water info
        /// - 0x0204 (2.4): Latest known version
        /// </remarks>
        public ushort Version { get; set; }
        
        /// <summary>
        /// Map width in tiles/cubes (X dimension).
        /// Each tile is TileScale units wide (default 10.0).
        /// </summary>
        public int Width { get; set; }
        
        /// <summary>
        /// Map height in tiles/cubes (Y/Z dimension in world space).
        /// Each tile is TileScale units deep (default 10.0).
        /// </summary>
        public int Height { get; set; }
        
        /// <summary>
        /// Scale factor for converting grid coordinates to world coordinates.
        /// Default is 10.0 units per tile (standard RO scale).
        /// World X = gridX * TileScale
        /// World Z = (Height - gridY) * TileScale
        /// </summary>
        public float TileScale { get; set; }
        
        /// <summary>
        /// Total number of terrain cells (Width * Height).
        /// Useful for pre-allocating arrays or progress tracking.
        /// </summary>
        public int TileCount => Width * Height;

        // =================================================================
        // DATA COLLECTIONS
        // =================================================================
        // NOTE: These are List<T> (mutable) rather than IReadOnlyList<T> (immutable)
        // because GndReaderV2.cs needs to call .Add() during parsing.
        
        /// <summary>
        /// List of texture file references used by this terrain.
        /// Each entry contains a filename (path in GRF) and display name.
        /// Surfaces reference textures by index into this list.
        /// </summary>
        /// <remarks>
        /// Must be List&lt;T&gt; (not IReadOnlyList) to allow .Add() during parsing.
        /// </remarks>
        public List<GndTextureV2> Textures { get; set; } = new();
        
        /// <summary>
        /// Lightmap data containing pre-baked lighting for each terrain cell.
        /// Lightmaps are 8x8 pixel grids of shadow/ambient occlusion data.
        /// </summary>
        /// <remarks>
        /// Must have 'set' accessor (not 'init') to allow assignment during parsing.
        /// </remarks>
        public GndLightmapInfo Lightmaps { get; set; } = new();
        
        /// <summary>
        /// List of surface/tile definitions with UV coordinates and colors.
        /// Each surface defines how a face of a cube should be textured.
        /// Cubes reference surfaces by index into this list.
        /// </summary>
        /// <remarks>
        /// Must be List&lt;T&gt; (not IReadOnlyList) to allow .Add() during parsing.
        /// </remarks>
        public List<GndSurfaceTile> Surfaces { get; set; } = new();
        
        /// <summary>
        /// 2D array of terrain cubes indexed by [x, y] grid position.
        /// Each cube has 4 corner heights and references to surface tiles.
        /// </summary>
        public GndCubeV2_Legacy[,] Cubes { get; set; } = new GndCubeV2_Legacy[0, 0];
        
        /// <summary>
        /// Optional water configuration (present in version 0x0107+).
        /// Contains water height, animation, and visual properties.
        /// </summary>
        public GndWaterInfo? Water { get; set; }

        // =================================================================
        // ACCESSOR METHODS
        // =================================================================
        
        /// <summary>
        /// Safely retrieves a cube at the specified grid position.
        /// Returns null if coordinates are out of bounds.
        /// </summary>
        /// <param name="x">X grid coordinate (0 to Width-1)</param>
        /// <param name="y">Y grid coordinate (0 to Height-1)</param>
        /// <returns>The cube at (x,y) or null if out of bounds</returns>
        public GndCubeV2_Legacy? GetCube(int x, int y)
        {
            // Bounds check to prevent IndexOutOfRangeException
            if (x >= 0 && x < Width && y >= 0 && y < Height)
            {
                return Cubes[x, y];
            }
            return null;
        }
        
        /// <summary>
        /// Safely retrieves a surface by index.
        /// Returns null if index is out of bounds.
        /// </summary>
        /// <param name="index">Surface index (0 to Surfaces.Count-1)</param>
        /// <returns>The surface at the index or null if invalid</returns>
        public GndSurfaceTile? GetSurface(int index)
        {
            if (index >= 0 && index < Surfaces.Count)
            {
                return Surfaces[index];
            }
            return null;
        }

        /// <summary>
        /// Returns a human-readable summary of this GND file.
        /// Format: "GND vMajor.Minor WxH textures=N surfaces=N"
        /// </summary>
        public override string ToString()
        {
            // Extract major and minor version from packed ushort
            int major = Version >> 8;      // High byte
            int minor = Version & 0xFF;    // Low byte
            return $"GND v{major}.{minor} {Width}x{Height} textures={Textures.Count} surfaces={Surfaces.Count}";
        }
    }
    
    // =========================================================================
    // SUPPORTING DATA STRUCTURES
    // =========================================================================
    
    /// <summary>
    /// Represents a texture reference in the GND file.
    /// Contains the file path (within GRF archive) and a display name.
    /// </summary>
    public sealed class GndTextureV2
    {
        /// <summary>
        /// Texture file path within the GRF archive.
        /// Example: "texture\prontera\prt_ground01.bmp"
        /// </summary>
        public string Filename { get; set; } = string.Empty;
        
        /// <summary>
        /// Display name for the texture (often same as filename or empty).
        /// </summary>
        public string Name { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// Represents a surface/tile face with UV texture coordinates and vertex color.
    /// Each face of a terrain cube can reference a surface to define its appearance.
    /// </summary>
    /// <remarks>
    /// This is a readonly struct for performance - surfaces are immutable once created.
    /// UV Layout (BrowEdit3 convention):
    ///   U1,V1 = vertex 1 (bottom-left)
    ///   U2,V2 = vertex 2 (bottom-right)
    ///   U3,V3 = vertex 3 (top-left)
    ///   U4,V4 = vertex 4 (top-right)
    /// </remarks>
    public readonly struct GndSurfaceTile
    {
        // UV coordinates for the 4 corners of this tile face
        // These map texture pixels to the quad vertices
        public readonly float U1, U2, U3, U4;  // Horizontal texture coordinates (0.0 to 1.0)
        public readonly float V1, V2, V3, V4;  // Vertical texture coordinates (0.0 to 1.0)
        
        /// <summary>
        /// Index into the GND's texture list. -1 means no texture (invisible face).
        /// </summary>
        public readonly short TextureIndex;
        
        /// <summary>
        /// Index into the lightmap array for this surface's lighting data.
        /// </summary>
        public readonly ushort LightmapIndex;
        
        /// <summary>
        /// Vertex color components (BGRA order as stored in file).
        /// These tint the texture and are often used for color variation.
        /// </summary>
        public readonly byte B, G, R, A;  // Note: File stores as BGRA, not RGBA
        
        /// <summary>
        /// Creates a new surface tile with all properties specified.
        /// </summary>
        public GndSurfaceTile(
            float u1, float u2, float u3, float u4,
            float v1, float v2, float v3, float v4,
            short texIdx, ushort lmIdx,
            byte b, byte g, byte r, byte a)
        {
            U1 = u1; U2 = u2; U3 = u3; U4 = u4;
            V1 = v1; V2 = v2; V3 = v3; V4 = v4;
            TextureIndex = texIdx;
            LightmapIndex = lmIdx;
            B = b; G = g; R = r; A = a;
        }
        
        /// <summary>
        /// True if this surface has a valid texture (TextureIndex >= 0).
        /// Surfaces with TextureIndex = -1 are invisible/unused.
        /// </summary>
        public bool HasTexture => TextureIndex >= 0;
    }
    
    /// <summary>
    /// Container for lightmap data and dimensions.
    /// Lightmaps provide pre-baked ambient lighting/shadows for terrain.
    /// </summary>
    public sealed class GndLightmapInfo
    {
        /// <summary>Number of lightmap entries in the file.</summary>
        public int Count { get; set; }
        
        /// <summary>Width of each lightmap cell in pixels (typically 8).</summary>
        public int CellWidth { get; set; }
        
        /// <summary>Height of each lightmap cell in pixels (typically 8).</summary>
        public int CellHeight { get; set; }
        
        /// <summary>Grid subdivision size (typically 1).</summary>
        public int GridSizeCell { get; set; }
        
        /// <summary>
        /// Raw lightmap pixel data. Each lightmap is CellWidth * CellHeight * 4 bytes.
        /// Format: 1 byte intensity + 3 bytes RGB color per pixel.
        /// Total size: Count * CellWidth * CellHeight * 4 bytes.
        /// </summary>
        public byte[] RawData { get; set; } = Array.Empty<byte>();
    }
    
    /// <summary>
    /// Represents a single terrain cell (cube) with 4 corner heights and surface references.
    /// This is the fundamental building block of RO terrain.
    /// </summary>
    /// <remarks>
    /// Height Mapping (BrowEdit3 convention):
    ///   h1 = Bottom-Left (BL) corner  -> Height01
    ///   h2 = Bottom-Right (BR) corner -> Height11
    ///   h3 = Top-Left (TL) corner     -> Height00
    ///   h4 = Top-Right (TR) corner    -> Height10
    /// 
    /// The naming convention Height{X}{Y} uses:
    ///   First digit: 0=left, 1=right
    ///   Second digit: 0=top, 1=bottom
    /// </remarks>
    public readonly struct GndCubeV2_Legacy
    {
        // =================================================================
        // CORNER HEIGHTS
        // =================================================================
        // Heights are in world units (negative values = higher terrain due to RO's inverted Y)
        
        /// <summary>Height at Top-Left corner (h3 in BrowEdit3).</summary>
        public readonly float Height00;
        
        /// <summary>Height at Top-Right corner (h4 in BrowEdit3).</summary>
        public readonly float Height10;
        
        /// <summary>Height at Bottom-Left corner (h1 in BrowEdit3).</summary>
        public readonly float Height01;
        
        /// <summary>Height at Bottom-Right corner (h2 in BrowEdit3).</summary>
        public readonly float Height11;
        
        // =================================================================
        // SURFACE INDICES
        // =================================================================
        // -1 means no surface (invisible/no rendering for that face)
        
        /// <summary>
        /// Surface index for the TOP face (horizontal ground).
        /// -1 if this cube has no visible top surface.
        /// </summary>
        public readonly int TileUp;
        
        /// <summary>
        /// Surface index for the SIDE wall (left edge, facing -X).
        /// Used when adjacent cube has different height.
        /// </summary>
        public readonly int TileSide;
        
        /// <summary>
        /// Surface index for the FRONT wall (bottom edge, facing +Z in BrowEdit coords).
        /// Used when adjacent cube has different height.
        /// </summary>
        public readonly int TileFront;
        
        /// <summary>
        /// Creates a new terrain cube with specified heights and surface references.
        /// </summary>
        /// <param name="h1">Height at Bottom-Left (BL) corner</param>
        /// <param name="h2">Height at Bottom-Right (BR) corner</param>
        /// <param name="h3">Height at Top-Left (TL) corner</param>
        /// <param name="h4">Height at Top-Right (TR) corner</param>
        /// <param name="tileUp">Surface index for top face (-1 = none)</param>
        /// <param name="tileSide">Surface index for side wall (-1 = none)</param>
        /// <param name="tileFront">Surface index for front wall (-1 = none)</param>
        public GndCubeV2_Legacy(
            float h1, float h2, float h3, float h4,
            int tileUp, int tileSide, int tileFront)
        {
            // Map BrowEdit's h1-h4 to our Height00-Height11 naming
            // BrowEdit: h1=BL, h2=BR, h3=TL, h4=TR
            // Our naming: Height{X}{Y} where X=0/1 for left/right, Y=0/1 for top/bottom
            Height01 = h1;  // BL = left(0) + bottom(1)
            Height11 = h2;  // BR = right(1) + bottom(1)
            Height00 = h3;  // TL = left(0) + top(0)
            Height10 = h4;  // TR = right(1) + top(0)
            
            TileUp = tileUp;
            TileSide = tileSide;
            TileFront = tileFront;
        }
        
        /// <summary>
        /// Average height of all 4 corners. Useful for center-point calculations.
        /// </summary>
        public float AverageHeight => (Height00 + Height10 + Height01 + Height11) / 4f;
        
        /// <summary>True if this cube has a visible top surface.</summary>
        public bool HasTopSurface => TileUp >= 0;
        
        /// <summary>True if this cube has a visible side wall.</summary>
        public bool HasSideWall => TileSide >= 0;
        
        /// <summary>True if this cube has a visible front wall.</summary>
        public bool HasFrontWall => TileFront >= 0;
    }

    /// <summary>
    /// Water configuration data (added in GND version 0x0107).
    /// Defines water plane height, type, and animation parameters.
    /// </summary>
    public sealed class GndWaterInfo
    {
        /// <summary>Water surface height in world units.</summary>
        public float Height { get; set; }
        
        /// <summary>Water type/material ID (determines texture set).</summary>
        public int Type { get; set; }
        
        /// <summary>Wave amplitude (height of wave peaks).</summary>
        public float Amplitude { get; set; }
        
        /// <summary>Wave animation speed.</summary>
        public float WaveSpeed { get; set; }
        
        /// <summary>Wave wavelength/pitch.</summary>
        public float WavePitch { get; set; }
        
        /// <summary>Texture animation frame rate.</summary>
        public int AnimationSpeed { get; set; }
    }
}
