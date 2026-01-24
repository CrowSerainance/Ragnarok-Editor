namespace ROMapOverlayEditor.ThreeD
{
    /// <summary>Output of GND parsing. Used by GndTerrainMeshBuilder.</summary>
    public sealed class ParsedGnd
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public float Scale { get; set; } = 10f;

        public ParsedGndTile[,] Tiles { get; set; } = new ParsedGndTile[1, 1];
    }

    public sealed class ParsedGndTile
    {
        // Corner heights (float). GND: BottomLeft=SW, BottomRight=SE, TopLeft=NW, TopRight=NE.
        public float H00 { get; set; }  // SW / BottomLeft
        public float H10 { get; set; }  // SE / BottomRight
        public float H01 { get; set; }  // NW / TopLeft
        public float H11 { get; set; }  // NE / TopRight

        // Texture/surface (for future use)
        public int TextureIndex { get; set; }
    }
}
