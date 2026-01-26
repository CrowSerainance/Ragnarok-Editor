using System;
using System.Collections.Generic;

namespace ROMapOverlayEditor.Gnd
{
    public sealed class GndFile
    {
        public ushort Version { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public float TileScale { get; set; }

        public int LightmapWidth { get; set; }
        public int LightmapHeight { get; set; }
        public int GridSizeCell { get; set; }

        public List<GndTexture> Textures { get; } = new();
        public List<GndTile> Tiles { get; } = new();

        // Cubes indexed [x,y] matching BrowEdit’s cubes[x][y]
        public GndCube[,] Cubes { get; set; } = new GndCube[0,0];

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
    }

    public sealed class GndTexture
    {
        public string File { get; set; } = "";  // texture file name (e.g. something.bmp)
        public string Name { get; set; } = "";  // internal name
    }

    public sealed class GndTile
    {
        // UVs
        public float U1 { get; set; }
        public float U2 { get; set; }
        public float U3 { get; set; }
        public float U4 { get; set; }

        public float V1 { get; set; }
        public float V2 { get; set; }
        public float V3 { get; set; }
        public float V4 { get; set; }

        public int TextureIndex { get; set; }
        public int LightmapIndex { get; set; }

        // RGBA
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
        public byte A { get; set; }
    }

    public sealed class GndCube
    {
        public float H1 { get; set; }
        public float H2 { get; set; }
        public float H3 { get; set; }
        public float H4 { get; set; }

        public int TileUp { get; set; }
        public int TileSide { get; set; }
        public int TileFront { get; set; }

        public float AvgHeight => (H1 + H2 + H3 + H4) / 4f;
    }
}
