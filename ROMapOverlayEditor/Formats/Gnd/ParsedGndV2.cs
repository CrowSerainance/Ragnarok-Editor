using System;
using ROMapOverlayEditor.Rsw;

namespace ROMapOverlayEditor.Gnd
{
    public class ParsedGndV2
    {
        public ushort Version { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public float Zoom { get; set; }

        public string[] Textures { get; set; }
        public int LightmapCount { get; set; }
        public int LightmapWidth { get; set; }
        public int LightmapHeight { get; set; }
        public int LightmapCells { get; set; }
        public int LightmapStrideBytes { get; set; }

        public GndTileV2[] Tiles { get; set; }
        public GndSurfaceV2[] Surfaces { get; set; }
        public GndCubeV2[] Cubes { get; set; }
    }

    public struct GndTileV2
    {
        public float U { get; set; }
        public float V { get; set; }
        public ushort TextureIndex { get; set; }
        public ushort LightmapIndex { get; set; }
        public uint Color { get; set; }
    }

    public struct GndSurfaceV2
    {
        public float Height1 { get; set; }
        public float Height2 { get; set; }
        public float Height3 { get; set; }
        public float Height4 { get; set; }
        public int TileUp { get; set; }
        public int TileFront { get; set; }
        public int TileRight { get; set; }
        public Vec3F Normal { get; set; }
    }

    public struct GndCubeV2
    {
        public int SurfaceUp { get; set; }
        public int SurfaceFront { get; set; }
        public int SurfaceRight { get; set; }
    }
}
