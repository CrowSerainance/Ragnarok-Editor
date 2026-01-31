using System;
using System.Collections.Generic;
using System.Numerics;

namespace ROMapOverlayEditor.ThreeD
{
    public sealed class TerrainMesh
    {
        public Vector3[] Positions { get; set; } = Array.Empty<Vector3>();
        public Vector2[] UV { get; set; } = Array.Empty<Vector2>();
        public int[] Indices { get; set; } = Array.Empty<int>();

        public int Width { get; set; }
        public int Height { get; set; }
    }

    public static class GndTerrainMeshBuilder
    {
        // RO tile size 10; typical center at width*5, height*5
        private const float TileSize = 10f;

        public static TerrainMesh BuildFromParsedGnd(ParsedGnd gnd)
        {
            int w = gnd.Width;
            int h = gnd.Height;

            var positions = new List<Vector3>(w * h * 4);
            var uv = new List<Vector2>(w * h * 4);
            var indices = new List<int>(w * h * 6);

            int vBase = 0;

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var tile = gnd.Tiles[x, y];

                float h0 = tile.H00;
                float h1 = tile.H10;
                float h2 = tile.H01;
                float h3 = tile.H11;

                float x0 = x * TileSize;
                float x1 = (x + 1) * TileSize;
                float z0 = y * TileSize;
                float z1 = (y + 1) * TileSize;

                // 0:(x0,z0) 1:(x1,z0) 2:(x0,z1) 3:(x1,z1)
                positions.Add(new Vector3(x0, h0, z0));
                positions.Add(new Vector3(x1, h1, z0));
                positions.Add(new Vector3(x0, h2, z1));
                positions.Add(new Vector3(x1, h3, z1));

                uv.Add(new Vector2(0, 0));
                uv.Add(new Vector2(1, 0));
                uv.Add(new Vector2(0, 1));
                uv.Add(new Vector2(1, 1));

                indices.Add(vBase + 0);
                indices.Add(vBase + 1);
                indices.Add(vBase + 2);

                indices.Add(vBase + 2);
                indices.Add(vBase + 1);
                indices.Add(vBase + 3);

                vBase += 4;
            }

            return new TerrainMesh
            {
                Width = w,
                Height = h,
                Positions = positions.ToArray(),
                UV = uv.ToArray(),
                Indices = indices.ToArray()
            };
        }

        /// <summary>Build a flat grid from dimensions when GND is missing. Heights = 0.</summary>
        public static TerrainMesh BuildFlatGrid(int width, int height)
        {
            var gnd = new ParsedGnd { Width = width, Height = height, Tiles = new ParsedGndTile[width, height] };
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                gnd.Tiles[x, y] = new ParsedGndTile();
            return BuildFromParsedGnd(gnd);
        }
    }
}
