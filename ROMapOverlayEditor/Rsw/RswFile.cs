using System.Collections.Generic;

namespace ROMapOverlayEditor.Rsw
{
    public sealed class RswFile
    {
        public string Signature { get; set; } = "";
        public ushort Version { get; set; }
        public byte BuildNumber { get; set; }
        public int UnknownAfterBuild { get; set; }
        public string IniFile { get; set; } = "";
        public string GndFile { get; set; } = "";
        public string GatFile { get; set; } = "";
        public string SourceFile { get; set; } = "";
        public WaterSettings? Water { get; set; }
        public LightSettings Light { get; set; } = new LightSettings();
        public List<RswObject> Objects { get; set; } = new();

        public int ObjectCount => Objects?.Count ?? 0;
    }

    public sealed class WaterSettings
    {
        public float WaterLevel { get; set; }
        public int WaterType { get; set; }
        public float WaveHeight { get; set; }
        public float WaveSpeed { get; set; }
        public float WavePitch { get; set; }
        public int AnimSpeed { get; set; }
    }

    public sealed class LightSettings
    {
        public int Longitude { get; set; }
        public int Latitude { get; set; }
        public Vec3 Diffuse { get; set; }
        public Vec3 Ambient { get; set; }
        public float Opacity { get; set; }
    }

    public class RswObject
    {
        public int ObjectType { get; set; }
        public string Name { get; set; } = "";
        public Vec3 Position { get; set; }
        public Vec3 Rotation { get; set; }
        public Vec3 Scale { get; set; }
    }

    public sealed class RswModel : RswObject
    {
        public int AnimType { get; set; }
        public float AnimSpeed { get; set; }
        public int BlockType { get; set; }
        public string FileName { get; set; } = "";
    }

    public sealed class RswLight : RswObject
    {
        public Vec3 Color { get; set; }
        public float Range { get; set; }
    }

    public sealed class RswSound : RswObject
    {
        public string FileName { get; set; } = "";
        public float Volume { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public float Range { get; set; }
    }

    public sealed class RswEffect : RswObject
    {
        public int EffectId { get; set; }
        public float Delay { get; set; }
        public float Param { get; set; }
    }

    public sealed class RswUnknown : RswObject { }

    public struct Vec3
    {
        public float X, Y, Z;
        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public override string ToString() => $"{X},{Y},{Z}";
    }
}
