// ============================================================================
// RswFileV2.cs - Optimized RSW Data Models (from rsw_viewer reference)
// ============================================================================
// PURPOSE: Immutable data structures for RSW (Resource World) files
// NOTES: Based on BrowEdit3 format documentation with full version support
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ROMapOverlayEditor.Rsw
{
    /// <summary>
    /// RSW file header and metadata container.
    /// Supports all known versions from 0x0103 through 0x0206+.
    /// </summary>
    public sealed class RswFileV2
    {
        // ====================================================================
        // VERSION CONSTANTS - Use these instead of magic numbers
        // ====================================================================
        
        /// <summary>Version where GAT file reference was added</summary>
        public const ushort VERSION_GAT_FILE = 0x0104;
        
        /// <summary>Version where lighting info was added</summary>
        public const ushort VERSION_LIGHTING = 0x0105;
        
        /// <summary>Version where bounding box was added</summary>
        public const ushort VERSION_BBOX = 0x0106;
        
        /// <summary>Version where shadow opacity was added</summary>
        public const ushort VERSION_SHADOW_OPACITY = 0x0107;
        
        /// <summary>Version where water type/amplitude was added</summary>
        public const ushort VERSION_WATER_EXT = 0x0108;
        
        /// <summary>Version where water animation speed was added</summary>
        public const ushort VERSION_WATER_ANIM = 0x0109;
        
        /// <summary>Version with build number byte</summary>
        public const ushort VERSION_BUILD_NUMBER = 0x0202;
        
        /// <summary>Version where unknown int was added</summary>
        public const ushort VERSION_UNKNOWN_INT = 0x0205;
        
        /// <summary>Version where water moved to GND file</summary>
        public const ushort VERSION_WATER_IN_GND = 0x0206;

        // ====================================================================
        // PROPERTIES
        // ====================================================================
        
        public ushort Version { get; init; }
        public byte? BuildNumber { get; init; }
        public int? UnknownV205 { get; init; }

        public string IniFile1 { get; init; } = string.Empty;
        public string GndFile { get; init; } = string.Empty;
        public string GatFile { get; init; } = string.Empty;
        public string IniFile2 { get; init; } = string.Empty;

        public RswWaterInfo? Water { get; init; }
        public RswLightingInfo? Lighting { get; init; }
        public RswBoundingBox? BoundingBox { get; init; }

        public int ObjectCount { get; init; }
        public IReadOnlyList<RswObjectBase> Objects { get; init; } = Array.Empty<RswObjectBase>();

        public int MajorVersion => (Version >> 8) & 0xFF;
        public int MinorVersion => Version & 0xFF;
        public string VersionString => $"{MajorVersion}.{MinorVersion}";
        public bool HasWaterInRsw => Version < VERSION_WATER_IN_GND;

        public override string ToString()
            => $"RSW v{VersionString} (0x{Version:X4}) build={BuildNumber?.ToString() ?? "-"} gnd='{GndFile}' gat='{GatFile}' objects={ObjectCount}";
    }

    public sealed class RswWaterInfo
    {
        public float Height { get; init; }
        public int Type { get; init; }
        public float Amplitude { get; init; }
        public float WaveSpeed { get; init; }
        public float WavePitch { get; init; }
        public int TextureAnimSpeed { get; init; }
    }

    public sealed class RswLightingInfo
    {
        public int Longitude { get; init; }
        public int Latitude { get; init; }
        public Vec3F DiffuseColor { get; init; }
        public Vec3F AmbientColor { get; init; }
        public float ShadowOpacity { get; init; } = 1.0f;

        public Vec3F GetLightDirection()
        {
            float lonRad = Longitude * MathF.PI / 180f;
            float latRad = Latitude * MathF.PI / 180f;
            float cosLat = MathF.Cos(latRad);
            return new Vec3F(cosLat * MathF.Sin(lonRad), MathF.Sin(latRad), cosLat * MathF.Cos(lonRad));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct RswBoundingBox
    {
        public readonly int Left, Top, Right, Bottom;
        public RswBoundingBox(int left, int top, int right, int bottom) { Left = left; Top = top; Right = right; Bottom = bottom; }
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    public abstract class RswObjectBase
    {
        public abstract int ObjectType { get; }
        public string Name { get; init; } = string.Empty;
        public Vec3F Position { get; init; }
    }

    public sealed class RswModelObject : RswObjectBase
    {
        public override int ObjectType => 1;
        public int AnimationType { get; init; }
        public float AnimationSpeed { get; init; }
        public int BlockType { get; init; }
        public byte? UnknownByte206 { get; init; }
        public string Filename { get; init; } = string.Empty;
        public string ObjectName { get; init; } = string.Empty;
        public Vec3F Rotation { get; init; }
        public Vec3F Scale { get; init; }
    }

    public sealed class RswLightObject : RswObjectBase
    {
        public override int ObjectType => 2;
        public float[] UnknownFloats { get; init; } = new float[10];
        public Vec3F Color { get; init; }
        public float Range { get; init; }
    }

    public sealed class RswSoundObject : RswObjectBase
    {
        public override int ObjectType => 3;
        public string WaveFile { get; init; } = string.Empty;
        public float Unknown1 { get; init; }
        public float Unknown2 { get; init; }
        public Vec3F Rotation { get; init; }
        public Vec3F Scale { get; init; }
        public byte[] UnknownBytes { get; init; } = new byte[8];
        public float Volume { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public float Range { get; init; }
    }

    public sealed class RswEffectObject : RswObjectBase
    {
        public override int ObjectType => 4;
        public int EffectId { get; init; }
        public float EmitSpeed { get; init; }
        public float Param1 { get; init; }
        public float Loop { get; init; }
        public float Unknown1 { get; init; }
        public float Unknown2 { get; init; }
        public float Unknown3 { get; init; }
        public float Unknown4 { get; init; }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public readonly struct Vec3F : IEquatable<Vec3F>
    {
        public readonly float X, Y, Z;
        public Vec3F(float x, float y, float z) { X = x; Y = y; Z = z; }
        public float Length => MathF.Sqrt(X * X + Y * Y + Z * Z);
        public static readonly Vec3F Zero = new(0, 0, 0);
        public static Vec3F operator +(Vec3F a, Vec3F b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3F operator -(Vec3F a, Vec3F b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3F operator *(Vec3F v, float s) => new(v.X * s, v.Y * s, v.Z * s);
        public bool Equals(Vec3F other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object? obj) => obj is Vec3F v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X:F3}, {Y:F3}, {Z:F3})";
    }
}
