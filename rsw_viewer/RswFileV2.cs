// ============================================================================
// RswFileV2.cs - Optimized RSW Data Models
// ============================================================================
// PURPOSE: Immutable data structures for RSW (Resource World) files
// INTEGRATION: Drop into ROMapOverlayEditor/Rsw/ folder
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
        
        /// <summary>
        /// Version stored as 0xMMmm (major in high byte, minor in low byte).
        /// Example: 0x0206 = version 2.6
        /// </summary>
        public ushort Version { get; init; }
        
        /// <summary>
        /// Build number, present only for version >= 0x0202.
        /// </summary>
        public byte? BuildNumber { get; init; }
        
        /// <summary>
        /// Unknown value added in version >= 0x0205.
        /// </summary>
        public int? UnknownV205 { get; init; }

        // --------------------------------------------------------------------
        // File References
        // --------------------------------------------------------------------
        
        /// <summary>Legacy INI file path (unused in modern RO)</summary>
        public string IniFile1 { get; init; } = string.Empty;
        
        /// <summary>Associated GND (ground) file name</summary>
        public string GndFile { get; init; } = string.Empty;
        
        /// <summary>Associated GAT (altitude) file name (version > 0x0104)</summary>
        public string GatFile { get; init; } = string.Empty;
        
        /// <summary>Legacy INI file path 2 (unused in modern RO)</summary>
        public string IniFile2 { get; init; } = string.Empty;

        // --------------------------------------------------------------------
        // Water Settings (version < 0x0206, moved to GND in later versions)
        // --------------------------------------------------------------------
        
        /// <summary>Water configuration, null if version >= 0x0206</summary>
        public RswWaterInfo? Water { get; init; }

        // --------------------------------------------------------------------
        // Lighting Settings
        // --------------------------------------------------------------------
        
        /// <summary>Global lighting configuration (version >= 0x0105)</summary>
        public RswLightingInfo? Lighting { get; init; }

        // --------------------------------------------------------------------
        // Bounding Box
        // --------------------------------------------------------------------
        
        /// <summary>Map bounding box (version >= 0x0106)</summary>
        public RswBoundingBox? BoundingBox { get; init; }

        // --------------------------------------------------------------------
        // Objects
        // --------------------------------------------------------------------
        
        /// <summary>Number of objects declared in file</summary>
        public int ObjectCount { get; init; }
        
        /// <summary>List of all map objects (models, lights, sounds, effects)</summary>
        public IReadOnlyList<RswObjectBase> Objects { get; init; } = Array.Empty<RswObjectBase>();

        // ====================================================================
        // HELPER PROPERTIES
        // ====================================================================
        
        /// <summary>Major version number (e.g., 2 for 0x0206)</summary>
        public int MajorVersion => (Version >> 8) & 0xFF;
        
        /// <summary>Minor version number (e.g., 6 for 0x0206)</summary>
        public int MinorVersion => Version & 0xFF;
        
        /// <summary>Human-readable version string</summary>
        public string VersionString => $"{MajorVersion}.{MinorVersion}";
        
        /// <summary>Check if water info is stored in RSW (vs GND)</summary>
        public bool HasWaterInRsw => Version < VERSION_WATER_IN_GND;

        // ====================================================================
        // DIAGNOSTICS
        // ====================================================================
        
        public override string ToString()
            => $"RSW v{VersionString} (0x{Version:X4}) " +
               $"build={BuildNumber?.ToString() ?? "-"} " +
               $"gnd='{GndFile}' gat='{GatFile}' " +
               $"objects={ObjectCount}";
    }

    // ========================================================================
    // WATER INFO
    // ========================================================================
    
    /// <summary>
    /// Water configuration for RSW files version < 0x0206.
    /// In newer versions, this data is stored in the GND file.
    /// </summary>
    public sealed class RswWaterInfo
    {
        /// <summary>Height of the water plane</summary>
        public float Height { get; init; }
        
        /// <summary>Water texture type index (version >= 0x0108)</summary>
        public int Type { get; init; }
        
        /// <summary>Wave amplitude - height varies between Height±Amplitude (version >= 0x0108)</summary>
        public float Amplitude { get; init; }
        
        /// <summary>Wave travel speed (version >= 0x0108)</summary>
        public float WaveSpeed { get; init; }
        
        /// <summary>Wave pitch/width (version >= 0x0108)</summary>
        public float WavePitch { get; init; }
        
        /// <summary>Texture animation speed in frames at 60fps (version >= 0x0109)</summary>
        public int TextureAnimSpeed { get; init; }
    }

    // ========================================================================
    // LIGHTING INFO
    // ========================================================================
    
    /// <summary>
    /// Global lighting configuration for the map.
    /// </summary>
    public sealed class RswLightingInfo
    {
        /// <summary>Light direction longitude (degrees)</summary>
        public int Longitude { get; init; }
        
        /// <summary>Light direction latitude (degrees)</summary>
        public int Latitude { get; init; }
        
        /// <summary>Diffuse light color (RGB, 0-1 range)</summary>
        public Vec3F DiffuseColor { get; init; }
        
        /// <summary>Ambient light color (RGB, 0-1 range)</summary>
        public Vec3F AmbientColor { get; init; }
        
        /// <summary>Shadow opacity factor (version >= 0x0107)</summary>
        public float ShadowOpacity { get; init; } = 1.0f;
        
        /// <summary>
        /// Calculate the light direction vector from longitude/latitude.
        /// Uses BrowEdit's coordinate system.
        /// </summary>
        public Vec3F GetLightDirection()
        {
            // Convert degrees to radians
            float lonRad = Longitude * MathF.PI / 180f;
            float latRad = Latitude * MathF.PI / 180f;
            
            // Spherical to Cartesian conversion (BrowEdit convention)
            float cosLat = MathF.Cos(latRad);
            return new Vec3F(
                cosLat * MathF.Sin(lonRad),
                MathF.Sin(latRad),
                cosLat * MathF.Cos(lonRad)
            );
        }
    }

    // ========================================================================
    // BOUNDING BOX
    // ========================================================================
    
    /// <summary>
    /// Map bounding box coordinates.
    /// Default values are typically (-500, -500) to (500, 500).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct RswBoundingBox
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;

        public RswBoundingBox(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public int Width => Right - Left;
        public int Height => Bottom - Top;

        public override string ToString() 
            => $"BBox({Left},{Top})-({Right},{Bottom})";
    }

    // ========================================================================
    // OBJECT BASE CLASS
    // ========================================================================
    
    /// <summary>
    /// Base class for all RSW objects.
    /// </summary>
    public abstract class RswObjectBase
    {
        /// <summary>Object type identifier (1=Model, 2=Light, 3=Sound, 4=Effect)</summary>
        public abstract int ObjectType { get; }
        
        /// <summary>Object name (40 chars max in file)</summary>
        public string Name { get; init; } = string.Empty;
        
        /// <summary>World position</summary>
        public Vec3F Position { get; init; }
    }

    // ========================================================================
    // MODEL OBJECT
    // ========================================================================
    
    /// <summary>
    /// RSM model placement in the world.
    /// </summary>
    public sealed class RswModelObject : RswObjectBase
    {
        public override int ObjectType => 1;
        
        /// <summary>Animation type identifier</summary>
        public int AnimationType { get; init; }
        
        /// <summary>Animation playback speed</summary>
        public float AnimationSpeed { get; init; }
        
        /// <summary>Collision block type</summary>
        public int BlockType { get; init; }
        
        /// <summary>Unknown byte for version >= 0x0206 with build > 161</summary>
        public byte? UnknownByte206 { get; init; }
        
        /// <summary>RSM model filename (without path, e.g., "prontera_001.rsm")</summary>
        public string Filename { get; init; } = string.Empty;
        
        /// <summary>Additional object identifier name</summary>
        public string ObjectName { get; init; } = string.Empty;
        
        /// <summary>Rotation in degrees (X, Y, Z)</summary>
        public Vec3F Rotation { get; init; }
        
        /// <summary>Scale factor (X, Y, Z)</summary>
        public Vec3F Scale { get; init; }

        public override string ToString()
            => $"Model '{Name}' -> '{Filename}' @ {Position}";
    }

    // ========================================================================
    // LIGHT OBJECT
    // ========================================================================
    
    /// <summary>
    /// Point light source in the world.
    /// </summary>
    public sealed class RswLightObject : RswObjectBase
    {
        public override int ObjectType => 2;
        
        /// <summary>10 unknown float values (preserved for round-trip)</summary>
        public float[] UnknownFloats { get; init; } = new float[10];
        
        /// <summary>Light color (RGB, typically 0-1 range)</summary>
        public Vec3F Color { get; init; }
        
        /// <summary>Light influence range/radius</summary>
        public float Range { get; init; }

        public override string ToString()
            => $"Light '{Name}' @ {Position} color={Color} range={Range}";
    }

    // ========================================================================
    // SOUND OBJECT
    // ========================================================================
    
    /// <summary>
    /// Ambient sound source in the world.
    /// </summary>
    public sealed class RswSoundObject : RswObjectBase
    {
        public override int ObjectType => 3;
        
        /// <summary>WAV file to play</summary>
        public string WaveFile { get; init; } = string.Empty;
        
        /// <summary>Unknown float 1</summary>
        public float Unknown1 { get; init; }
        
        /// <summary>Unknown float 2</summary>
        public float Unknown2 { get; init; }
        
        /// <summary>Rotation (purpose unclear for sounds)</summary>
        public Vec3F Rotation { get; init; }
        
        /// <summary>Scale (purpose unclear for sounds)</summary>
        public Vec3F Scale { get; init; }
        
        /// <summary>8 unknown bytes (preserved for round-trip)</summary>
        public byte[] UnknownBytes { get; init; } = new byte[8];
        
        /// <summary>Playback volume (0-1)</summary>
        public float Volume { get; init; }
        
        /// <summary>Trigger area width</summary>
        public int Width { get; init; }
        
        /// <summary>Trigger area height</summary>
        public int Height { get; init; }
        
        /// <summary>Audible range/distance</summary>
        public float Range { get; init; }

        public override string ToString()
            => $"Sound '{Name}' -> '{WaveFile}' @ {Position} vol={Volume} range={Range}";
    }

    // ========================================================================
    // EFFECT OBJECT
    // ========================================================================
    
    /// <summary>
    /// Visual effect placement in the world.
    /// </summary>
    public sealed class RswEffectObject : RswObjectBase
    {
        public override int ObjectType => 4;
        
        /// <summary>Effect type ID (see BrowEdit3 effect table)</summary>
        public int EffectId { get; init; }
        
        /// <summary>Emission speed</summary>
        public float EmitSpeed { get; init; }
        
        /// <summary>Effect-specific parameter</summary>
        public float Param1 { get; init; }
        
        /// <summary>Loop delay/timing</summary>
        public float Loop { get; init; }
        
        /// <summary>Unknown float 1</summary>
        public float Unknown1 { get; init; }
        
        /// <summary>Unknown float 2</summary>
        public float Unknown2 { get; init; }
        
        /// <summary>Unknown float 3</summary>
        public float Unknown3 { get; init; }
        
        /// <summary>Unknown float 4</summary>
        public float Unknown4 { get; init; }

        public override string ToString()
            => $"Effect '{Name}' type={EffectId} @ {Position}";
    }

    // ========================================================================
    // VECTOR STRUCTS (Optimized for memory layout)
    // ========================================================================
    
    /// <summary>
    /// 3D float vector with proper memory layout for interop.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public readonly struct Vec3F : IEquatable<Vec3F>
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Vec3F(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        // --------------------------------------------------------------------
        // Common Operations
        // --------------------------------------------------------------------
        
        public float Length => MathF.Sqrt(X * X + Y * Y + Z * Z);
        public float LengthSquared => X * X + Y * Y + Z * Z;
        
        public Vec3F Normalized
        {
            get
            {
                float len = Length;
                return len > 0.0001f 
                    ? new Vec3F(X / len, Y / len, Z / len) 
                    : Zero;
            }
        }

        // --------------------------------------------------------------------
        // Static Members
        // --------------------------------------------------------------------
        
        public static readonly Vec3F Zero = new(0, 0, 0);
        public static readonly Vec3F One = new(1, 1, 1);
        public static readonly Vec3F UnitX = new(1, 0, 0);
        public static readonly Vec3F UnitY = new(0, 1, 0);
        public static readonly Vec3F UnitZ = new(0, 0, 1);

        // --------------------------------------------------------------------
        // Operators
        // --------------------------------------------------------------------
        
        public static Vec3F operator +(Vec3F a, Vec3F b) 
            => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        
        public static Vec3F operator -(Vec3F a, Vec3F b) 
            => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        
        public static Vec3F operator *(Vec3F v, float s) 
            => new(v.X * s, v.Y * s, v.Z * s);
        
        public static Vec3F operator *(float s, Vec3F v) 
            => new(v.X * s, v.Y * s, v.Z * s);
        
        public static Vec3F operator /(Vec3F v, float s) 
            => new(v.X / s, v.Y / s, v.Z / s);
        
        public static Vec3F operator -(Vec3F v) 
            => new(-v.X, -v.Y, -v.Z);

        public static float Dot(Vec3F a, Vec3F b) 
            => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        
        public static Vec3F Cross(Vec3F a, Vec3F b) 
            => new(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X
            );

        // --------------------------------------------------------------------
        // Equality
        // --------------------------------------------------------------------
        
        public bool Equals(Vec3F other) 
            => X == other.X && Y == other.Y && Z == other.Z;
        
        public override bool Equals(object? obj) 
            => obj is Vec3F v && Equals(v);
        
        public override int GetHashCode() 
            => HashCode.Combine(X, Y, Z);
        
        public static bool operator ==(Vec3F left, Vec3F right) 
            => left.Equals(right);
        
        public static bool operator !=(Vec3F left, Vec3F right) 
            => !left.Equals(right);

        public override string ToString() 
            => $"({X:F3}, {Y:F3}, {Z:F3})";
    }
}
