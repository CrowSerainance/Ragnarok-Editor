// ============================================================================
// ParsedGnd.cs - GND Data Models
// ============================================================================
// TARGET: ROMapOverlayEditor/ThreeD/ParsedGnd.cs (REPLACE EXISTING)
// ============================================================================

namespace ROMapOverlayEditor.ThreeD
{
    /// <summary>
    /// Output of GND parsing. Used by terrain builders.
    /// </summary>
    public sealed class ParsedGnd
    {
        /// <summary>Map width in tiles</summary>
        public int Width { get; set; }
        
        /// <summary>Map height in tiles</summary>
        public int Height { get; set; }
        
        /// <summary>Tile scale factor (default 10.0)</summary>
        public float Scale { get; set; } = 10f;

        /// <summary>2D array of tile data [x, y]</summary>
        public ParsedGndTile[,] Tiles { get; set; } = new ParsedGndTile[1, 1];
        
        /// <summary>Texture filenames (from GND texture block)</summary>
        public string[] Textures { get; set; } = System.Array.Empty<string>();
    }

    /// <summary>
    /// Single ground tile (cube) with corner heights and texture reference.
    /// </summary>
    public sealed class ParsedGndTile
    {
        // ================================================================
        // CORNER HEIGHTS
        // ================================================================
        // GND coordinate system:
        //   - X increases to the right (East)
        //   - Y/Z increases upward (into the screen / North)
        //   - Heights are typically negative (below origin)
        //
        // Corner naming:
        //   H00 = Bottom-Left  (SW corner, x=0, y=0)
        //   H10 = Bottom-Right (SE corner, x=1, y=0)
        //   H01 = Top-Left     (NW corner, x=0, y=1)
        //   H11 = Top-Right    (NE corner, x=1, y=1)
        // ================================================================
        
        /// <summary>Height at bottom-left corner (SW)</summary>
        public float H00 { get; set; }
        
        /// <summary>Height at bottom-right corner (SE)</summary>
        public float H10 { get; set; }
        
        /// <summary>Height at top-left corner (NW)</summary>
        public float H01 { get; set; }
        
        /// <summary>Height at top-right corner (NE)</summary>
        public float H11 { get; set; }

        // ================================================================
        // TEXTURE REFERENCE
        // ================================================================
        
        /// <summary>
        /// Index into the tile definitions array (not the texture array directly).
        /// -1 means no surface/invisible.
        /// </summary>
        public int TextureIndex { get; set; }
        
        // ================================================================
        // HELPERS
        // ================================================================
        
        /// <summary>Get average height of the tile</summary>
        public float AverageHeight => (H00 + H10 + H01 + H11) * 0.25f;
        
        /// <summary>Get height at a specific corner (0-3)</summary>
        public float GetCornerHeight(int corner) => corner switch
        {
            0 => H00,
            1 => H10,
            2 => H01,
            3 => H11,
            _ => H00
        };
    }
}
