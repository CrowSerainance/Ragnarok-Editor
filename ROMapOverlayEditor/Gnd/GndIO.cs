using System;
using System.IO;
using System.Text;

namespace ROMapOverlayEditor.Gnd
{
    public static class GndIO
    {
        public static bool LooksLikeGnd(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 8) return false;
            return bytes[0] == (byte)'G' && bytes[1] == (byte)'R' && bytes[2] == (byte)'G' && bytes[3] == (byte)'N';
        }

        public static GndFile Read(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 16)
                throw new InvalidDataException("GND too small.");

            using var ms = new MemoryStream(bytes, writable: false);
            using var br = new BinaryReader(ms);

            var sig = br.ReadBytes(4);
            if (sig.Length != 4 || sig[0] != 'G' || sig[1] != 'R' || sig[2] != 'G' || sig[3] != 'N')
                throw new InvalidDataException("Not a GND (missing GRGN/GRGN-like signature).");

            // BrowEdit: version is short (little-endian)
            ushort version = br.ReadUInt16();

            int width = br.ReadInt32();
            int height = br.ReadInt32();
            float tileScale = br.ReadSingle();
            int textureCount = br.ReadInt32();
            int maxTexName = br.ReadInt32(); // usually 80; not needed for rendering

            var gnd = new GndFile
            {
                Version = version,
                Width = width,
                Height = height,
                TileScale = tileScale
            };

            // Textures list: 40 bytes file + 40 bytes name
            for (int i = 0; i < textureCount; i++)
            {
                string file = ReadFixedString(br, 40);
                string name = ReadFixedString(br, 40);
                gnd.Textures.Add(new GndTexture { File = file, Name = name });
            }

            // Lightmaps header
            int lightmapCount = br.ReadInt32();
            gnd.LightmapWidth = br.ReadInt32();
            gnd.LightmapHeight = br.ReadInt32();
            gnd.GridSizeCell = br.ReadInt32();

            // Lightmap data: lightmapCount * 256 bytes
            // We do not use the lightmaps yet for rendering; just skip.
            long lmBytes = (long)lightmapCount * 256L;
            if (ms.Position + lmBytes > ms.Length)
                throw new InvalidDataException("GND truncated in lightmap section.");
            ms.Position += lmBytes;

            // Tiles
            int tileCount = br.ReadInt32();
            for (int i = 0; i < tileCount; i++)
            {
                var t = new GndTile();

                // 4 U, then 4 V (BrowEdit reads x then y)
                t.U1 = br.ReadSingle();
                t.U2 = br.ReadSingle();
                t.U3 = br.ReadSingle();
                t.U4 = br.ReadSingle();

                t.V1 = br.ReadSingle();
                t.V2 = br.ReadSingle();
                t.V3 = br.ReadSingle();
                t.V4 = br.ReadSingle();

                // BrowEdit: textureIndex = readWord, lightmapIndex = readUWord
                // We treat both as ushort -> int.
                t.TextureIndex = br.ReadUInt16();
                t.LightmapIndex = br.ReadUInt16();

                // Color is stored BGRA in BrowEdit code order (b,g,r,a)
                t.B = br.ReadByte();
                t.G = br.ReadByte();
                t.R = br.ReadByte();
                t.A = br.ReadByte();

                gnd.Tiles.Add(t);
            }

            // Cubes: width * height
            var cubes = new GndCube[width, height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var c = new GndCube
                    {
                        H1 = br.ReadSingle(),
                        H2 = br.ReadSingle(),
                        H3 = br.ReadSingle(),
                        H4 = br.ReadSingle(),
                    };

                    // BrowEdit: if version >= 0x0106 tile ids are int; else ushort.
                    if (version >= 0x0106)
                    {
                        c.TileUp = br.ReadInt32();
                        c.TileSide = br.ReadInt32();
                        c.TileFront = br.ReadInt32();
                    }
                    else
                    {
                        c.TileUp = br.ReadUInt16();
                        c.TileSide = br.ReadUInt16();
                        c.TileFront = br.ReadUInt16();
                    }

                    cubes[x, y] = c;
                }
            }

            gnd.Cubes = cubes;
            return gnd;
        }

        private static string ReadFixedString(BinaryReader br, int len)
        {
            var bytes = br.ReadBytes(len);
            if (bytes == null || bytes.Length == 0) return "";

            int end = Array.IndexOf(bytes, (byte)0);
            if (end < 0) end = bytes.Length;

            try
            {
                var enc = Encoding.GetEncoding(949);
                return enc.GetString(bytes, 0, end).Trim();
            }
            catch
            {
                return Encoding.Default.GetString(bytes, 0, end).Trim();
            }
        }
    }
}
