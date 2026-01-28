// ============================================================================
// GndReader.cs - FIXED VERSION (Based on BrowEdit3 Format)
// ============================================================================
// THIS IS THE FILE THAT IS ACTUALLY USED BY YOUR PROJECT
// TARGET: F:\2026 PROJECT\ROMapOverlayEditor\ROMapOverlayEditor\Map3D\GndReader.cs
// ACTION: REPLACE ENTIRE FILE
// ============================================================================
// CRITICAL FIXES:
//   1. Version is 2 bytes (ushort), NOT 4 bytes (uint)
//   2. After textures: lightmap header (4 ints) + lightmap data (count × w × h × 4)
//   3. Surface: UVs first (8 floats), then texIndex/lightmap/color
//   4. Cube size: 28 bytes for version >= 0x0106, else 24 bytes
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ROMapOverlayEditor.Map3D
{
    // ========================================================================
    // GND FILE DATA STRUCTURES
    // ========================================================================
    
    /// <summary>
    /// Complete GND file structure for textured terrain rendering.
    /// Based on BrowEdit3's Gnd.cpp implementation.
    /// </summary>
    public sealed class GndFile
    {
        public string Signature = "";
        public ushort Version;           // FIXED: was uint, should be ushort (2 bytes)
        public int Width;
        public int Height;
        public float TileScale = 10f;
        public List<string> Textures = new();
        public Surface[] Surfaces = Array.Empty<Surface>();
        public Cube[] Cubes = Array.Empty<Cube>();
        
        // Lightmap info (for debugging/future use)
        public int LightmapCount;
        public int LightmapWidth = 8;
        public int LightmapHeight = 8;
    }

    /// <summary>
    /// Surface definition (tile face) with UVs, texture, and color.
    /// BrowEdit3 calls this "Tile" in Gnd.h
    /// </summary>
    public readonly struct Surface
    {
        public readonly int TextureId;
        public readonly int LightmapIndex;
        public readonly float U1, V1, U2, V2, U3, V3, U4, V4;
        public readonly byte ColorR, ColorG, ColorB, ColorA;
        
        public Surface(int tex, int lightmap, 
                       float u1, float v1, float u2, float v2, 
                       float u3, float v3, float u4, float v4,
                       byte r, byte g, byte b, byte a)
        {
            TextureId = tex;
            LightmapIndex = lightmap;
            U1 = u1; V1 = v1;
            U2 = u2; V2 = v2;
            U3 = u3; V3 = v3;
            U4 = u4; V4 = v4;
            ColorR = r; ColorG = g; ColorB = b; ColorA = a;
        }
    }

    /// <summary>
    /// Ground cube (cell) with 4 corner heights and surface indices.
    /// BrowEdit3 calls this "Cube" in Gnd.h
    /// </summary>
    public readonly struct Cube
    {
        // Corner heights (negative = higher in world)
        // h1=SW(bottom-left), h2=SE(bottom-right), h3=NW(top-left), h4=NE(top-right)
        public readonly float H1, H2, H3, H4;
        
        // Surface indices (-1 = no surface)
        public readonly int TopSurfaceIndex;   // tileUp in BrowEdit
        public readonly int FrontSurfaceIndex; // tileFront
        public readonly int SideSurfaceIndex;  // tileSide
        
        public Cube(float h1, float h2, float h3, float h4, int top, int front, int side)
        {
            H1 = h1; H2 = h2; H3 = h3; H4 = h4;
            TopSurfaceIndex = top;
            FrontSurfaceIndex = front;
            SideSurfaceIndex = side;
        }
    }

    // ========================================================================
    // GND READER - Based on BrowEdit3's Gnd.cpp
    // ========================================================================
    
    public static class GndReader
    {
        private static Encoding? _koreanEncoding;
        
        /// <summary>
        /// Read a GND file from a stream.
        /// </summary>
        public static GndFile Read(Stream s)
        {
            // Initialize Korean encoding for texture names
            if (_koreanEncoding == null)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                try { _koreanEncoding = Encoding.GetEncoding(949); }
                catch { _koreanEncoding = Encoding.GetEncoding(1252); }
            }
            
            using var br = new BinaryReader(s, Encoding.ASCII, leaveOpen: true);
            
            var g = new GndFile();
            
            // ================================================================
            // HEADER (BrowEdit3: lines 30-50)
            // ================================================================
            
            // Magic "GRGN" (4 bytes)
            g.Signature = Encoding.ASCII.GetString(br.ReadBytes(4));
            if (g.Signature != "GRGN")
                throw new InvalidDataException($"Not a GND file (signature='{g.Signature}')");
            
            // Version (2 bytes, big-endian in BrowEdit but we'll try both)
            // BrowEdit does: version = util::swapShort(version)
            byte vLo = br.ReadByte();
            byte vHi = br.ReadByte();
            g.Version = (ushort)((vHi << 8) | vLo);  // Try big-endian first (BrowEdit style)
            
            // Dimensions and texture info
            g.Width = br.ReadInt32();
            g.Height = br.ReadInt32();
            g.TileScale = br.ReadSingle();
            
            // Validate dimensions
            if (g.Width <= 0 || g.Height <= 0 || g.Width > 1024 || g.Height > 1024)
                throw new InvalidDataException($"GND: Invalid dimensions {g.Width}x{g.Height}");
            
            int textureCount = br.ReadInt32();
            int maxTexName = br.ReadInt32();  // Usually 80 (40+40)
            
            if (textureCount < 0 || textureCount > 10000)
                throw new InvalidDataException($"GND: Invalid texture count {textureCount}");
            
            // ================================================================
            // TEXTURES (BrowEdit3: lines 57-65)
            // Each texture: filename (40 bytes) + texname (40 bytes) = 80 bytes
            // ================================================================
            
            for (int i = 0; i < textureCount; i++)
            {
                // Read 40 bytes for filename
                byte[] fileBytes = br.ReadBytes(40);
                // Read 40 bytes for texture name (skip it)
                br.ReadBytes(40);
                
                // Find null terminator in filename
                int nullPos = Array.IndexOf(fileBytes, (byte)0);
                if (nullPos < 0) nullPos = fileBytes.Length;
                
                string name = "";
                if (nullPos > 0)
                {
                    try { name = _koreanEncoding!.GetString(fileBytes, 0, nullPos); }
                    catch { name = Encoding.ASCII.GetString(fileBytes, 0, nullPos); }
                }
                
                g.Textures.Add(name.Trim());
            }
            
            // ================================================================
            // LIGHTMAPS (BrowEdit3: lines 68-95)
            // Header: count, width, height, gridSizeCell (4 ints = 16 bytes)
            // Data: count × (width × height × 4) bytes
            // ================================================================
            
            g.LightmapCount = br.ReadInt32();
            g.LightmapWidth = br.ReadInt32();
            g.LightmapHeight = br.ReadInt32();
            int gridSizeCell = br.ReadInt32();
            
            // Fix invalid lightmap format (from BrowEdit3)
            if (g.LightmapWidth <= 0 || g.LightmapHeight <= 0 || gridSizeCell <= 0)
            {
                g.LightmapWidth = 8;
                g.LightmapHeight = 8;
            }
            
            // Validate and skip lightmap data
            if (g.LightmapCount > 0 && g.LightmapCount < 10000000)
            {
                long lightmapDataSize = (long)g.LightmapCount * g.LightmapWidth * g.LightmapHeight * 4;
                if (br.BaseStream.Position + lightmapDataSize <= br.BaseStream.Length)
                {
                    br.BaseStream.Seek(lightmapDataSize, SeekOrigin.Current);
                }
            }
            
            // ================================================================
            // SURFACES / TILES (BrowEdit3: lines 97-124)
            // Each surface: 40 bytes
            //   - u1,u2,u3,u4 (4 floats = 16 bytes)
            //   - v1,v2,v3,v4 (4 floats = 16 bytes)
            //   - textureIndex (short = 2 bytes)
            //   - lightmapIndex (ushort = 2 bytes)
            //   - color BGRA (4 bytes)
            // Total: 40 bytes
            // ================================================================
            
            int surfaceCount = br.ReadInt32();
            
            if (surfaceCount < 0 || surfaceCount > 10000000)
                throw new InvalidDataException($"GND: Invalid surface count {surfaceCount}");
            
            g.Surfaces = new Surface[surfaceCount];
            
            for (int i = 0; i < surfaceCount; i++)
            {
                // UVs: u1,u2,u3,u4 then v1,v2,v3,v4
                float u1 = br.ReadSingle();
                float u2 = br.ReadSingle();
                float u3 = br.ReadSingle();
                float u4 = br.ReadSingle();
                float v1 = br.ReadSingle();
                float v2 = br.ReadSingle();
                float v3 = br.ReadSingle();
                float v4 = br.ReadSingle();
                
                short texIndex = br.ReadInt16();
                ushort lightmapIndex = br.ReadUInt16();
                
                byte colorB = br.ReadByte();
                byte colorG = br.ReadByte();
                byte colorR = br.ReadByte();
                byte colorA = br.ReadByte();
                
                // Validate texture index
                if (texIndex < 0 || texIndex >= textureCount)
                    texIndex = 0;
                
                g.Surfaces[i] = new Surface(
                    texIndex, lightmapIndex,
                    u1, v1, u2, v2, u3, v3, u4, v4,
                    colorR, colorG, colorB, colorA
                );
            }
            
            // ================================================================
            // CUBES / GROUND MESH (BrowEdit3: lines 126-180)
            // Version >= 0x0106: 28 bytes (4 heights + 3 int indices)
            // Version < 0x0106:  24 bytes (4 heights + 4 short indices)
            // ================================================================
            
            int cubeCount = g.Width * g.Height;
            g.Cubes = new Cube[cubeCount];
            
            bool useIntIndices = g.Version >= 0x0106;
            
            for (int y = 0; y < g.Height; y++)
            {
                for (int x = 0; x < g.Width; x++)
                {
                    int idx = y * g.Width + x;
                    
                    float h1 = br.ReadSingle();  // SW / bottom-left
                    float h2 = br.ReadSingle();  // SE / bottom-right
                    float h3 = br.ReadSingle();  // NW / top-left
                    float h4 = br.ReadSingle();  // NE / top-right
                    
                    int tileUp, tileFront, tileSide;
                    
                    if (useIntIndices)
                    {
                        tileUp = br.ReadInt32();
                        tileFront = br.ReadInt32();
                        tileSide = br.ReadInt32();
                    }
                    else
                    {
                        tileUp = br.ReadInt16();
                        tileFront = br.ReadInt16();
                        tileSide = br.ReadInt16();
                        br.ReadInt16();  // unknown/padding
                    }
                    
                    // Validate indices (BrowEdit3 lines 162-176)
                    if (tileUp < -1 || tileUp >= surfaceCount) tileUp = -1;
                    if (tileFront < -1 || tileFront >= surfaceCount) tileFront = -1;
                    if (tileSide < -1 || tileSide >= surfaceCount) tileSide = -1;
                    
                    g.Cubes[idx] = new Cube(h1, h2, h3, h4, tileUp, tileFront, tileSide);
                }
            }
            
            return g;
        }
        
        /// <summary>
        /// Read a GND file from byte array.
        /// </summary>
        public static GndFile Read(byte[] data)
        {
            using var ms = new MemoryStream(data);
            return Read(ms);
        }
    }
}
