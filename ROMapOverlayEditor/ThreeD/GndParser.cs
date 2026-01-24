using System;
using System.Text;

namespace ROMapOverlayEditor.ThreeD
{
    /// <summary>Minimal GND 1.7+ parser: reads width, height, and per-tile heights (GroundMeshCubes).</summary>
    public static class GndParser
    {
        private static readonly byte[] SigGrgn = Encoding.ASCII.GetBytes("GRGN");

        public static (bool Ok, string Message, ParsedGnd? Gnd) TryParse(byte[] raw)
        {
            if (raw == null || raw.Length < 24)
                return (false, "GND too short", null);

            if (raw[0] != SigGrgn[0] || raw[1] != SigGrgn[1] || raw[2] != SigGrgn[2] || raw[3] != SigGrgn[3])
                return (false, "GND: invalid magic (expected GRGN)", null);

            byte major = raw[4];
            byte minor = raw[5];
            int w = BitConverter.ToInt32(raw, 6);
            int h = BitConverter.ToInt32(raw, 10);
            float scale = BitConverter.ToSingle(raw, 14);
            int texCount = BitConverter.ToInt32(raw, 18);
            int texPathLen = BitConverter.ToInt32(raw, 22);

            if (w <= 0 || h <= 0 || w > 512 || h > 512)
                return (false, $"GND: invalid size {w}x{h}", null);

            int off = 24;

            // TexturePaths: texCount * 80 (TexturePathLength is usually 80)
            int pathBlock = Math.Max(80, texPathLen) * Math.Max(0, texCount);
            off += pathBlock;

            if (off + 4 > raw.Length)
                return (false, "GND: truncated at lightmap count", null);

            int lightmapCount = BitConverter.ToInt32(raw, off);
            off += 4;

            // LightmapSlices: each 268 bytes (simplified; actual has sub-fields)
            off += lightmapCount * 268;

            if (off + 4 > raw.Length)
                return (false, "GND: truncated at surface count", null);

            int surfaceCount = BitConverter.ToInt32(raw, off);
            off += 4;

            // SurfaceDefinitions: 56 bytes each
            off += surfaceCount * 56;

            const int cubeStride = 28;
            int cubesTotal = w * h;
            if (off + cubesTotal * cubeStride > raw.Length)
                return (false, "GND: truncated at ground mesh cubes", null);

            var tiles = new ParsedGndTile[w, h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                int cubeOff = off + i * cubeStride;

                float bl = BitConverter.ToSingle(raw, cubeOff + 0);
                float br = BitConverter.ToSingle(raw, cubeOff + 4);
                float tl = BitConverter.ToSingle(raw, cubeOff + 8);
                float tr = BitConverter.ToSingle(raw, cubeOff + 12);
                int upSurf = BitConverter.ToInt32(raw, cubeOff + 16);

                tiles[x, y] = new ParsedGndTile
                {
                    H00 = bl,
                    H10 = br,
                    H01 = tl,
                    H11 = tr,
                    TextureIndex = upSurf
                };
            }

            var gnd = new ParsedGnd
            {
                Width = w,
                Height = h,
                Scale = scale > 0 ? scale : 10f,
                Tiles = tiles
            };
            return (true, "", gnd);
        }
    }
}
