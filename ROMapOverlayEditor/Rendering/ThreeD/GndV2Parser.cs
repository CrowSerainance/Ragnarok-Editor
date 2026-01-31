using System;
using System.IO;
using System.Text;

namespace ROMapOverlayEditor.ThreeD
{
    // This follows BrowEdit3's Gnd.cpp logic for v>0:
    // - magic "GRGN"
    // - version(float)
    // - width/height(int)
    // - zoom(float)
    // - texture count + texture names
    // - if version>0: lightmaps block (we skip raw LM data, but we must advance correctly)
    // - tiles list
    // - cubes grid (h1..h4, tileUp/front/right)
    public static class GndV2Parser
    {
        public static GndV2 Parse(byte[] gndBytes)
        {
            using var ms = new MemoryStream(gndBytes);
            using var br = new BinaryReader(ms, Encoding.ASCII);

            var magic = Encoding.ASCII.GetString(br.ReadBytes(4));
            if (magic != "GRGN")
                throw new InvalidDataException("Not a GND file (missing GRGN).");

            float version = br.ReadSingle();
            int width = br.ReadInt32();
            int height = br.ReadInt32();
            float zoom = br.ReadSingle();

            var gnd = new GndV2
            {
                Version = version,
                Width = width,
                Height = height,
                Zoom = zoom,
                Cubes = new GndCube[width * height]
            };

            int texCount = br.ReadInt32();
            for (int i = 0; i < texCount; i++)
                gnd.Textures.Add(ReadNullTerminated(br));

            if (version > 0f)
            {
                int lightmapCount = br.ReadInt32();
                int lightmapWidth = br.ReadInt32();
                int lightmapHeight = br.ReadInt32();

                // BrowEdit3: reads lightmapsCount * (8*8 bytes per channel * 4 channels) + (lightmapWidth*lightmapHeight bytes * 4)
                // exact:
                // for each LM:
                //   for 4 channels: 8*8 bytes
                //   for 4 channels: lightmapWidth*lightmapHeight bytes
                int perLm = (4 * 64) + (4 * lightmapWidth * lightmapHeight);
                long skip = (long)lightmapCount * perLm;
                if (ms.Position + skip > ms.Length)
                    throw new EndOfStreamException("GND truncated while skipping lightmaps.");
                ms.Position += skip;

                int tileCount = br.ReadInt32();
                for (int i = 0; i < tileCount; i++)
                {
                    var t = new GndTile();
                    t.TextureIndex = br.ReadUInt16();
                    t.LightmapIndex = br.ReadUInt16();

                    // BrowEdit Gnd.cpp: UV stored as U1,U2,U3,U4 then V1,V2,V3,V4
                    float u1 = br.ReadSingle(), u2 = br.ReadSingle(), u3 = br.ReadSingle(), u4 = br.ReadSingle();
                    float v1 = br.ReadSingle(), v2 = br.ReadSingle(), v3 = br.ReadSingle(), v4 = br.ReadSingle();
                    t.V1 = new System.Windows.Vector(u1, v1);
                    t.V2 = new System.Windows.Vector(u2, v2);
                    t.V3 = new System.Windows.Vector(u3, v3);
                    t.V4 = new System.Windows.Vector(u4, v4);

                    // BrowEdit: COLOR IS STORED AS BGRA (Blue first!)
                    t.Color[0] = br.ReadByte(); // B
                    t.Color[1] = br.ReadByte(); // G
                    t.Color[2] = br.ReadByte(); // R
                    t.Color[3] = br.ReadByte(); // A

                    gnd.Tiles.Add(t);
                }

                // cubes
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        var c = new GndCube();
                        c.H1 = br.ReadSingle();
                        c.H2 = br.ReadSingle();
                        c.H3 = br.ReadSingle();
                        c.H4 = br.ReadSingle();
                        c.TileUp = br.ReadInt32();
                        c.TileFront = br.ReadInt32();
                        c.TileRight = br.ReadInt32();
                        gnd.Cubes[x + y * width] = c;
                    }
                }
            }
            else
            {
                // Very old GNDs (rare). You can implement surfaces mode if you need it.
                throw new NotSupportedException("GND version 0 not supported by this patch (BrowEdit3 style targets v>0).");
            }

            return gnd;
        }

        private static string ReadNullTerminated(BinaryReader br)
        {
            using var ms = new MemoryStream();
            while (true)
            {
                byte b = br.ReadByte();
                if (b == 0) break;
                ms.WriteByte(b);
            }
            return Encoding.GetEncoding(949).GetString(ms.ToArray()).Replace('\\', '/');
        }
    }
}
