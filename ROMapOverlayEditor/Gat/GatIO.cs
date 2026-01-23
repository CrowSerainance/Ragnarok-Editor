using System;
using System.IO;
using System.Text;

namespace ROMapOverlayEditor.Gat
{
    public static class GatIO
    {
        public static GatFile Read(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var br = new BinaryReader(ms, Encoding.ASCII, leaveOpen: false);

            var sig = new string(br.ReadChars(4));
            if (!sig.Equals("GRAT", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Not a GAT (missing GRAT).");

            // CORRECTED: Version is 2 bytes (major, minor), not a float
            byte verMajor = br.ReadByte();
            byte verMinor = br.ReadByte();
            
            // CORRECTED: Width at offset 6, Height at offset 10
            int w = br.ReadInt32();
            int h = br.ReadInt32();

            if (w <= 0 || h <= 0 || w > 4096 || h > 4096)
                throw new InvalidDataException($"Invalid GAT dimensions: {w}x{h}");

            var gf = new GatFile
            {
                VersionMajor = verMajor,
                VersionMinor = verMinor,
                Width = w,
                Height = h,
                Cells = new GatCell[w * h]
            };

            for (int i = 0; i < gf.Cells.Length; i++)
            {
                var c = new GatCell
                {
                    H1 = br.ReadSingle(),
                    H2 = br.ReadSingle(),
                    H3 = br.ReadSingle(),
                    H4 = br.ReadSingle(),
                    Type = (GatCellType)br.ReadInt32()
                };
                gf.Cells[i] = c;
            }

            return gf;
        }

        public static byte[] Write(GatFile gf)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: false);

            bw.Write(Encoding.ASCII.GetBytes("GRAT"));
            bw.Write(gf.VersionMajor);
            bw.Write(gf.VersionMinor);
            bw.Write(gf.Width);
            bw.Write(gf.Height);

            for (int i = 0; i < gf.Cells.Length; i++)
            {
                var c = gf.Cells[i];
                bw.Write(c.H1);
                bw.Write(c.H2);
                bw.Write(c.H3);
                bw.Write(c.H4);
                bw.Write((int)c.Type);
            }

            bw.Flush();
            return ms.ToArray();
        }
    }
}
