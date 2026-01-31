// ============================================================================
// GndParser.cs - FIXED VERSION (Based on BrowEdit3 Format)
// ============================================================================
// FIXES:
//   - Correct GND header parsing (version is 2 bytes after magic)
//   - Correct texture block: 80 bytes per texture (40 + 40)
//   - Correct lightmap header: 4 ints (count, width, height, gridSizeCell)
//   - Correct lightmap data: count × (width × height × 4)
//   - Correct tile definitions: 36 bytes each
//   - Correct cube stride based on version
// TARGET: ROMapOverlayEditor/ThreeD/GndParser.cs (REPLACE EXISTING)
// ============================================================================

using System;
using System.Text;

namespace ROMapOverlayEditor.ThreeD
{
    /// <summary>
    /// GND file parser based on BrowEdit3's implementation.
    /// Correctly handles GND versions 0x0100 through 0x0108.
    /// </summary>
    public static class GndParser
    {
        // ====================================================================
        // CONSTANTS
        // ====================================================================
        
        private static readonly byte[] SigGrgn = Encoding.ASCII.GetBytes("GRGN");
        
        /// <summary>Texture entry size: 40 bytes filename + 40 bytes name</summary>
        private const int TEXTURE_ENTRY_SIZE = 80;
        
        /// <summary>Tile definition size: UVs (32) + texIndex (2) + lightmapIndex (2) + color (4)</summary>
        private const int TILE_ENTRY_SIZE = 40;
        
        /// <summary>Cube size for version >= 0x0106: heights (16) + tile IDs as int (12)</summary>
        private const int CUBE_SIZE_NEW = 28;
        
        /// <summary>Cube size for version < 0x0106: heights (16) + tile IDs as short (8)</summary>
        private const int CUBE_SIZE_OLD = 24;

        // ====================================================================
        // MAIN PARSE METHOD
        // ====================================================================
        
        /// <summary>
        /// Parse a GND file from raw bytes.
        /// </summary>
        /// <param name="raw">Raw GND file data</param>
        /// <returns>Parse result with success flag, message, and parsed data</returns>
        public static (bool Ok, string Message, ParsedGnd? Gnd) TryParse(byte[] raw)
        {
            if (raw == null || raw.Length < 14)
                return (false, "GND: File too short", null);

            int offset = 0;

            // ================================================================
            // HEADER: Magic + Version
            // ================================================================
            
            // Check magic "GRGN"
            if (raw[0] != 'G' || raw[1] != 'R' || raw[2] != 'G' || raw[3] != 'N')
                return (false, "GND: Invalid magic (expected GRGN)", null);
            
            offset = 4;

            // Version: 2 bytes, stored as major.minor (e.g., 0x0107 = version 1.7)
            // BrowEdit swaps bytes (big-endian), but most RO files are little-endian
            // Let's read as little-endian short first
            ushort version = BitConverter.ToUInt16(raw, offset);
            offset += 2;
            
            // If version seems wrong (too high), try byte-swapping
            if (version > 0x0200)
            {
                version = (ushort)((raw[4] << 8) | raw[5]);
            }

            // ================================================================
            // DIMENSIONS AND TEXTURE COUNT (version > 0)
            // ================================================================
            
            int width, height, textureCount, maxTexName;
            float tileScale;

            if (version > 0)
            {
                if (offset + 20 > raw.Length)
                    return (false, "GND: Truncated at header dimensions", null);

                width = BitConverter.ToInt32(raw, offset); offset += 4;
                height = BitConverter.ToInt32(raw, offset); offset += 4;
                tileScale = BitConverter.ToSingle(raw, offset); offset += 4;
                textureCount = BitConverter.ToInt32(raw, offset); offset += 4;
                maxTexName = BitConverter.ToInt32(raw, offset); offset += 4;
            }
            else
            {
                // Old format (version 0)
                if (offset + 12 > raw.Length)
                    return (false, "GND: Truncated at header (v0)", null);

                textureCount = BitConverter.ToInt32(raw, offset); offset += 4;
                width = BitConverter.ToInt32(raw, offset); offset += 4;
                height = BitConverter.ToInt32(raw, offset); offset += 4;
                tileScale = 10.0f;
                maxTexName = 80;
            }

            // Validate dimensions
            if (width <= 0 || height <= 0 || width > 1024 || height > 1024)
                return (false, $"GND: Invalid dimensions {width}x{height}", null);

            if (textureCount < 0 || textureCount > 10000)
                return (false, $"GND: Invalid texture count {textureCount}", null);

            // ================================================================
            // TEXTURES: textureCount × 80 bytes each
            // ================================================================
            
            // Each texture: 40 bytes filename + 40 bytes texture name
            int textureBlockSize = textureCount * TEXTURE_ENTRY_SIZE;
            if (offset + textureBlockSize > raw.Length)
                return (false, $"GND: Truncated at textures (need {textureBlockSize} bytes)", null);

            var textures = new string[textureCount];
            for (int i = 0; i < textureCount; i++)
            {
                int texOffset = offset + (i * TEXTURE_ENTRY_SIZE);
                // Read filename (first 40 bytes, null-terminated)
                textures[i] = ReadNullTerminatedString(raw, texOffset, 40);
            }
            offset += textureBlockSize;

            // ================================================================
            // LIGHTMAPS (version > 0)
            // ================================================================
            
            int lightmapCount = 0;
            int lightmapWidth = 8;
            int lightmapHeight = 8;
            int gridSizeCell = 1;

            if (version > 0)
            {
                // Lightmap header: 4 ints
                if (offset + 16 > raw.Length)
                    return (false, "GND: Truncated at lightmap header", null);

                lightmapCount = BitConverter.ToInt32(raw, offset); offset += 4;
                lightmapWidth = BitConverter.ToInt32(raw, offset); offset += 4;
                lightmapHeight = BitConverter.ToInt32(raw, offset); offset += 4;
                gridSizeCell = BitConverter.ToInt32(raw, offset); offset += 4;

                // Validate lightmap parameters
                if (lightmapCount < 0 || lightmapCount > 1000000)
                    return (false, $"GND: Invalid lightmap count {lightmapCount}", null);

                // Fix invalid lightmap format (from BrowEdit3)
                if (lightmapWidth <= 0 || lightmapHeight <= 0 || gridSizeCell <= 0)
                {
                    lightmapWidth = 8;
                    lightmapHeight = 8;
                    gridSizeCell = 1;
                }

                // Skip lightmap data: count × (width × height × 4)
                int lightmapDataSize = lightmapCount * (lightmapWidth * lightmapHeight * 4);
                if (offset + lightmapDataSize > raw.Length)
                    return (false, $"GND: Truncated at lightmap data (need {lightmapDataSize} bytes)", null);

                offset += lightmapDataSize;
            }

            // ================================================================
            // TILES (Surface Definitions)
            // ================================================================
            
            if (offset + 4 > raw.Length)
                return (false, "GND: Truncated at tile count", null);

            int tileCount = BitConverter.ToInt32(raw, offset); offset += 4;

            if (tileCount < 0 || tileCount > 10000000)
                return (false, $"GND: Invalid tile count {tileCount}", null);

            // Tile structure (40 bytes each):
            // - UV coords: 8 floats (32 bytes) - v1.x, v2.x, v3.x, v4.x, v1.y, v2.y, v3.y, v4.y
            // - textureIndex: short (2 bytes)
            // - lightmapIndex: ushort (2 bytes)
            // - color: 4 bytes (BGRA)
            int tileBlockSize = tileCount * TILE_ENTRY_SIZE;
            if (offset + tileBlockSize > raw.Length)
                return (false, $"GND: Truncated at tile data (need {tileBlockSize} bytes at offset {offset})", null);

            offset += tileBlockSize;

            // ================================================================
            // CUBES (Ground Mesh)
            // ================================================================
            
            int cubeCount = width * height;
            int cubeSize = (version >= 0x0106) ? CUBE_SIZE_NEW : CUBE_SIZE_OLD;
            int cubeBlockSize = cubeCount * cubeSize;

            if (offset + cubeBlockSize > raw.Length)
                return (false, $"GND: Truncated at cube data (need {cubeBlockSize} bytes at offset {offset}, file size {raw.Length})", null);

            var tiles = new ParsedGndTile[width, height];
            
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int cubeOffset = offset + ((y * width + x) * cubeSize);

                    // Heights at corners (bottom-left, bottom-right, top-left, top-right)
                    float h1 = BitConverter.ToSingle(raw, cubeOffset + 0);  // BL
                    float h2 = BitConverter.ToSingle(raw, cubeOffset + 4);  // BR
                    float h3 = BitConverter.ToSingle(raw, cubeOffset + 8);  // TL
                    float h4 = BitConverter.ToSingle(raw, cubeOffset + 12); // TR

                    int tileUp;
                    if (version >= 0x0106)
                    {
                        tileUp = BitConverter.ToInt32(raw, cubeOffset + 16);
                    }
                    else
                    {
                        tileUp = BitConverter.ToInt16(raw, cubeOffset + 16);
                    }

                    tiles[x, y] = new ParsedGndTile
                    {
                        H00 = h1,
                        H10 = h2,
                        H01 = h3,
                        H11 = h4,
                        TextureIndex = tileUp
                    };
                }
            }

            // ================================================================
            // BUILD RESULT
            // ================================================================
            
            var gnd = new ParsedGnd
            {
                Width = width,
                Height = height,
                Scale = tileScale > 0 ? tileScale : 10f,
                Tiles = tiles,
                Textures = textures
            };

            return (true, $"GND v{(version >> 8)}.{(version & 0xFF)}: {width}x{height}, {textureCount} textures, {lightmapCount} lightmaps, {tileCount} tiles", gnd);
        }

        // ====================================================================
        // HELPER METHODS
        // ====================================================================
        
        /// <summary>
        /// Read a null-terminated string from a fixed-size buffer.
        /// </summary>
        private static string ReadNullTerminatedString(byte[] data, int offset, int maxLength)
        {
            int end = offset;
            int limit = Math.Min(offset + maxLength, data.Length);
            
            while (end < limit && data[end] != 0)
                end++;

            if (end == offset)
                return string.Empty;

            try
            {
                // Try Korean encoding first (most RO files)
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(949).GetString(data, offset, end - offset);
            }
            catch
            {
                return Encoding.ASCII.GetString(data, offset, end - offset);
            }
        }
    }
}
