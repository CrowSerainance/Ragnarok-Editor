using System.Collections.Generic;

namespace ROMapOverlayEditor.Rsw
{
    public sealed class RswFile
    {
        // Version stored as 0xMMmm (major/minor). Example: 0x0206
        public ushort Version { get; init; }
        public int? BuildNumber { get; init; } // present for newer versions

        public string IniFile1 { get; init; } = "";
        public string GndFile { get; init; } = "";
        public string GatFile { get; init; } = "";
        public string IniFile2 { get; init; } = "";

        public int ObjectCount { get; init; }
        public List<RswObject> Objects { get; init; } = new();

        public override string ToString()
            => $"RSW v=0x{Version:X4} build={BuildNumber?.ToString() ?? "-"} gnd='{GndFile}' gat='{GatFile}' objects={ObjectCount}";
    }

    public abstract class RswObject
    {
        public int Type { get; init; }
    }

    public sealed class RswModel : RswObject
    {
        public string Name { get; init; } = "";
        public int AnimationType { get; init; }
        public float AnimationSpeed { get; init; }
        public int BlockType { get; init; }
        public byte? Unknown206 { get; init; } // only in >=0x0206 build>161

        public string Filename { get; init; } = "";   // .rsm
        public string ObjectName { get; init; } = ""; // extra name

        public Vec3 Position { get; init; }
        public Vec3 Rotation { get; init; }
        public Vec3 Scale { get; init; }
    }

    public sealed class RswLight : RswObject
    {
        public string Name { get; init; } = "";
        public Vec3 Position { get; init; }
        public byte[] Unknown10 { get; init; } = new byte[10];
        public Vec3 Color { get; init; }
        public float Range { get; init; }
    }

    public sealed class RswSound : RswObject
    {
        public string Name { get; init; } = "";
        public string Filename { get; init; } = ""; // .wav
        public float Unknown1 { get; init; }
        public float Unknown2 { get; init; }
        public Vec3 Rotation { get; init; }
        public Vec3 Scale { get; init; }
        public byte[] Unknown8 { get; init; } = new byte[8];
        public Vec3 Position { get; init; }
        public float Volume { get; init; }
        public float Width { get; init; }
        public float Height { get; init; }
        public float Range { get; init; }
    }

    public sealed class RswEffect : RswObject
    {
        public string Name { get; init; } = "";
        public Vec3 Position { get; init; }
        public int Id { get; init; }
        public float Loop { get; init; }
        public float Unknown1 { get; init; }
        public float Unknown2 { get; init; }
        public float Unknown3 { get; init; }
        public float Unknown4 { get; init; }
    }

    public readonly struct Vec3
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }

        public override string ToString() => $"({X:0.###},{Y:0.###},{Z:0.###})";
    }
}
