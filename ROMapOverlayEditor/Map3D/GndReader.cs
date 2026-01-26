using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ROMapOverlayEditor.Map3D
{
    // Minimal GND reader for textured TOP faces (terrain).
    // BrowEdit uses this to show the real map surface, not only red/green GAT.
    public sealed class GndFile
    {
        public string Signature = "";
        public uint Version;
        public int Width;
        public int Height;
        public float TileScale; // often 10
        public List<string> Textures = new();
        public Surface[] Surfaces = Array.Empty<Surface>();
        public Cube[] Cubes = Array.Empty<Cube>();
    }

    public readonly struct Surface
    {
        public readonly int TextureId;
        public readonly float U1, V1, U2, V2, U3, V3, U4, V4;
        public Surface(int tex, float u1, float v1, float u2, float v2, float u3, float v3, float u4, float v4)
        {
            TextureId = tex;
            U1 = u1; V1 = v1;
            U2 = u2; V2 = v2;
            U3 = u3; V3 = v3;
            U4 = u4; V4 = v4;
        }
    }

    public readonly struct Cube
    {
        public readonly float H1, H2, H3, H4;
        public readonly int TopSurfaceIndex;
        public Cube(float h1, float h2, float h3, float h4, int top)
        {
            H1 = h1; H2 = h2; H3 = h3; H4 = h4;
            TopSurfaceIndex = top;
        }
    }

    public static class GndReader
    {
        public static GndFile Read(Stream s)
        {
            using var br = new BinaryReader(s, Encoding.GetEncoding(1252), leaveOpen: true);

            var g = new GndFile();
            g.Signature = Encoding.ASCII.GetString(br.ReadBytes(4));
            if (g.Signature != "GRGN")
                throw new InvalidDataException($"Not a GND file (sig={g.Signature})");

            g.Version = br.ReadUInt32();
            g.Width = br.ReadInt32();
            g.Height = br.ReadInt32();
            g.TileScale = br.ReadSingle();

            int textureCount = br.ReadInt32();
            int textureNameLen = br.ReadInt32();

            for (int i = 0; i < textureCount; i++)
            {
                var b = br.ReadBytes(textureNameLen);
                int z = Array.IndexOf(b, (byte)0);
                if (z < 0) z = b.Length;
                var name = Encoding.GetEncoding(1252).GetString(b, 0, z).Trim();
                g.Textures.Add(string.IsNullOrWhiteSpace(name) ? "" : name);
            }

            int surfaceCount = br.ReadInt32();
            g.Surfaces = new Surface[surfaceCount];

            for (int i = 0; i < surfaceCount; i++)
            {
                int tex = br.ReadInt16();
                br.ReadInt16(); // lightmap (unused here)
                br.ReadInt16(); // color (unused)
                br.ReadInt16(); // reserved
                float u1 = br.ReadSingle(); float v1 = br.ReadSingle();
                float u2 = br.ReadSingle(); float v2 = br.ReadSingle();
                float u3 = br.ReadSingle(); float v3 = br.ReadSingle();
                float u4 = br.ReadSingle(); float v4 = br.ReadSingle();
                g.Surfaces[i] = new Surface(tex, u1, v1, u2, v2, u3, v3, u4, v4);
            }

            int cubeCount = g.Width * g.Height;
            g.Cubes = new Cube[cubeCount];

            for (int i = 0; i < cubeCount; i++)
            {
                float h1 = br.ReadSingle();
                float h2 = br.ReadSingle();
                float h3 = br.ReadSingle();
                float h4 = br.ReadSingle();

                int top = br.ReadInt32();
                br.ReadInt32(); // front
                br.ReadInt32(); // right
                br.ReadInt32(); // left
                br.ReadInt32(); // back

                g.Cubes[i] = new Cube(h1, h2, h3, h4, top);
            }

            return g;
        }
    }
}
