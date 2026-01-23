using System;

namespace ROMapOverlayEditor.Gat
{
    public enum GatCellType : int
    {
        Walkable = 0,
        NotWalkable = 1,
        Water = 2,
        Cliff = 3,
        Unknown4 = 4,
        Unknown5 = 5,
        Unknown6 = 6,
        Unknown7 = 7
    }

    public sealed class GatCell
    {
        public float H1; // SW
        public float H2; // SE
        public float H3; // NW
        public float H4; // NE

        public GatCellType Type;

        public float AvgHeight => (H1 + H2 + H3 + H4) * 0.25f;
    }

    public sealed class GatFile
    {
        public int Width;
        public int Height;
        public byte VersionMajor = 1;
        public byte VersionMinor = 2;
        public GatCell[] Cells = Array.Empty<GatCell>();

        public GatCell Get(int x, int y) => Cells[y * Width + x];

        public void SetType(int x, int y, GatCellType t) => Cells[y * Width + x].Type = t;

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
    }
}
