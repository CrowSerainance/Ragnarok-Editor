// GndFileV2.cs - Mutable GND file representation

using System.Collections.Generic;

namespace ROMapOverlayEditor.Gnd
{
    /// <summary>
    /// Represents a parsed GND (ground) file.
    /// </summary>
    public sealed class GndFileV2
    {
        public ushort Version { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public float TileScale { get; set; } = 10f;
        
        /// <summary>Texture definitions</summary>
        public List<GndTextureV2> Textures { get; set; } = new();
        
        /// <summary>Surface/tile definitions (UV coords, texture index, colors)</summary>
        public List<GndSurfaceTile> Surfaces { get; set; } = new();
        
        /// <summary>Lightmap data (optional)</summary>
        public GndLightmapInfo? Lightmaps { get; set; }
        
        /// <summary>Cube grid [x, y] - terrain height and surface references</summary>
        public GndCubeV2_Legacy[,] Cubes { get; set; } = new GndCubeV2_Legacy[0, 0];
        
        /// <summary>Water info (optional)</summary>
        public GndWaterInfo? Water { get; set; }

        /// <summary>Get cube at position, or null if out of bounds</summary>
        public GndCubeV2_Legacy? GetCube(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                return null;
            return Cubes[x, y];
        }
        
        /// <summary>Get surface by index, or null if invalid</summary>
        public GndSurfaceTile? GetSurface(int index)
        {
            if (index < 0 || index >= Surfaces.Count)
                return null;
            return Surfaces[index];
        }

        public override string ToString() => $"GND v{Version >> 8}.{Version & 0xFF} {Width}x{Height} textures={Textures.Count} surfaces={Surfaces.Count}";
    }
    
    /// <summary>Texture reference in GND</summary>
    public sealed class GndTextureV2
    {
        public string Filename { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
    
    /// <summary>Surface/tile with UV coordinates and color</summary>
    public readonly struct GndSurfaceTile
    {
        public readonly float U1, U2, U3, U4;
        public readonly float V1, V2, V3, V4;
        public readonly short TextureIndex;
        public readonly ushort LightmapIndex;
        public readonly byte B, G, R, A;
        
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
        
        public bool HasTexture => TextureIndex >= 0;
    }
    
    /// <summary>Lightmap storage info</summary>
    public sealed class GndLightmapInfo
    {
        public int Count { get; set; }
        public int CellWidth { get; set; }
        public int CellHeight { get; set; }
        public int GridSizeCell { get; set; }
        public byte[] RawData { get; set; } = System.Array.Empty<byte>();
    }
    
    /// <summary>Terrain cube (cell) with 4 corner heights and surface references</summary>
    public readonly struct GndCubeV2_Legacy
    {
        /// <summary>Height at corners: H1=BL(01), H2=BR(11), H3=TL(00), H4=TR(10)</summary>
        public readonly float Height00, Height10, Height01, Height11;
        
        /// <summary>Surface index for top face (-1 = none)</summary>
        public readonly int TileUp;
        
        /// <summary>Surface index for side wall (-1 = none)</summary>
        public readonly int TileSide;
        
        /// <summary>Surface index for front wall (-1 = none)</summary>
        public readonly int TileFront;
        
        public GndCubeV2_Legacy(
            float h1, float h2, float h3, float h4,
            int tileUp, int tileSide, int tileFront)
        {
            // BrowEdit mapping: h1=BL, h2=BR, h3=TL, h4=TR
            // Note: GndReaderV2 assigns: h1->Height01 (BL), h2->Height11 (BR), h3->Height00 (TL), h4->Height10 (TR)
            Height01 = h1; // BL
            Height11 = h2; // BR
            Height00 = h3; // TL
            Height10 = h4; // TR
            TileUp = tileUp;
            TileSide = tileSide;
            TileFront = tileFront;
        }
        
        public float AverageHeight => (Height00 + Height10 + Height01 + Height11) / 4f;
        public bool HasTopSurface => TileUp >= 0;
        public bool HasSideWall => TileSide >= 0;
        public bool HasFrontWall => TileFront >= 0;
    }

    /// <summary>Water info</summary>
    public sealed class GndWaterInfo
    {
        public float Height { get; set; }
        public int Type { get; set; }
        public float Amplitude { get; set; }
        public float WaveSpeed { get; set; }
        public float WavePitch { get; set; }
        public int AnimationSpeed { get; set; }
    }
}
