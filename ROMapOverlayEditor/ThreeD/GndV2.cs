using System.Collections.Generic;
using System.Windows;

namespace ROMapOverlayEditor.ThreeD
{
    public sealed class GndV2
    {
        public int Width { get; init; }
        public int Height { get; init; }
        public float Zoom { get; init; }
        public float Version { get; init; }

        public List<string> Textures { get; } = new();
        public List<GndTile> Tiles { get; } = new();

        // Flattened cubes: index = x + y*Width
        public GndCube[] Cubes { get; init; }

        public bool InMap(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public GndCube CubeAt(int x, int y) => Cubes[x + y * Width];
    }

    public sealed class GndTile
    {
        public ushort TextureIndex;
        public ushort LightmapIndex; // we read it but you can ignore for now
        public Vector V1; // corner1 uv
        public Vector V2; // corner2 uv
        public Vector V3; // corner3 uv
        public Vector V4; // corner4 uv
        public byte[] Color = new byte[4]; // RGBA
    }

    public sealed class GndCube
    {
        public float H1, H2, H3, H4; // BrowEdit ordering
        public int TileUp, TileFront, TileRight; // tile indices (-1/-2 means none)
    }
}
