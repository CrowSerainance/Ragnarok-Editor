using System;
using System.Collections.Generic;
using System.IO;
using ROMapOverlayEditor.IO;

namespace ROMapOverlayEditor.Rsw
{
    public static class RswReaderV2
    {
        // RSW signature is "GRSW"
        private const string SIG = "GRSW";

        public static bool IsRswFile(byte[] bytes)
        {
             if (bytes == null || bytes.Length < 4) return false;
             // Check for "GRSW"
             return bytes[0] == 'G' && bytes[1] == 'R' && bytes[2] == 'S' && bytes[3] == 'W';
        }

        public static RswFileV2 Read(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            return Read(ms);
        }

        public static RswFileV2 Read(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Stream is not readable.");

            using var br = new BinaryReaderEx(stream, leaveOpen: true);

            var sig = br.ReadFixedString(4, BinaryReaderEx.EncodingAscii).TrimEnd('\0');
            if (!string.Equals(sig, SIG, StringComparison.Ordinal))
                throw new InvalidDataException($"Invalid RSW signature. Expected '{SIG}', got '{sig}'.");

            // ====================================================================
            // IMPORTANT FIX: RSW stores version as TWO BYTES: major then minor.
            // Your previous implementation read UInt16 little-endian => swapped bytes.
            // That single bug cascades into: wrong parsing branches, wrong object offsets,
            // insane objectCount, "unknown object type", and eventually crashes/hangs.
            // ====================================================================
            byte major = br.ReadByte();
            byte minor = br.ReadByte();
            ushort version = (ushort)((major << 8) | minor);

            // RSW >= 0x0202 has build number byte (per your constants)
            byte? buildNumber = null;
            if (version >= RswFileV2.VERSION_BUILD_NUMBER)
                buildNumber = br.ReadByte();

            // Ini/Gnd/Gat/Ini2 are fixed 40 bytes each (BrowEdit behavior)
            string ini1 = br.ReadFixedString(40, BinaryReaderEx.KoreanEncoding).TrimEnd('\0');
            string gnd  = br.ReadFixedString(40, BinaryReaderEx.KoreanEncoding).TrimEnd('\0');

            string gat;
            if (version >= RswFileV2.VERSION_GAT_FILE)
                gat = br.ReadFixedString(40, BinaryReaderEx.KoreanEncoding).TrimEnd('\0');
            else
                gat = string.Empty;

            string ini2 = br.ReadFixedString(40, BinaryReaderEx.KoreanEncoding).TrimEnd('\0');

            // Water (present until moved to GND at >= 0x0206)
            RswWaterInfo? water = null;
            if (version < RswFileV2.VERSION_WATER_IN_GND)
            {
                float waterHeight = br.ReadSingle();
                int waterType = br.ReadInt32();

                float amp = 1f, waveSpeed = 1f, wavePitch = 1f;
                int texAnim = 0;

                if (version >= RswFileV2.VERSION_WATER_EXT)
                {
                    amp = br.ReadSingle();
                    waveSpeed = br.ReadSingle();
                    wavePitch = br.ReadSingle();
                }
                if (version >= RswFileV2.VERSION_WATER_ANIM)
                    texAnim = br.ReadInt32();

                water = new RswWaterInfo
                {
                    Height = waterHeight,
                    Type = waterType,
                    Amplitude = amp,
                    WaveSpeed = waveSpeed,
                    WavePitch = wavePitch,
                    TextureAnimSpeed = texAnim
                };
            }

            // Lighting
            RswLightingInfo? lighting = null;
            if (version >= RswFileV2.VERSION_LIGHTING)
            {
                int lon = br.ReadInt32();
                int lat = br.ReadInt32();
                var diffuse = new Vec3F(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                var ambient = new Vec3F(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                float shadowOpacity = 1f;

                if (version >= RswFileV2.VERSION_SHADOW_OPACITY)
                    shadowOpacity = br.ReadSingle();

                lighting = new RswLightingInfo
                {
                    Longitude = lon,
                    Latitude = lat,
                    DiffuseColor = diffuse,
                    AmbientColor = ambient,
                    ShadowOpacity = shadowOpacity
                };
            }

            // Bounding box
            RswBoundingBox? bbox = null;
            if (version >= RswFileV2.VERSION_BBOX)
            {
                int left = br.ReadInt32();
                int top = br.ReadInt32();
                int right = br.ReadInt32();
                int bottom = br.ReadInt32();
                bbox = new RswBoundingBox(left, top, right, bottom);
            }

            // UnknownV205
            int? unknownV205 = null;
            if (version >= RswFileV2.VERSION_UNKNOWN_INT)
                unknownV205 = br.ReadInt32();

            // Object count (THIS is where your previous byte-swapped version breaks the file)
            int objectCount = br.ReadInt32();
            if (objectCount < 0 || objectCount > 500000) // sanity clamp; prevents hangs on corrupt reads
                throw new InvalidDataException($"RSW objectCount looks invalid: {objectCount} (version=0x{version:X4}).");

            var objects = new List<RswObjectBase>(Math.Min(objectCount, 4096));
            for (int i = 0; i < objectCount; i++)
            {
                int type = br.ReadInt32();

                // Name 40 bytes
                string name = br.ReadFixedString(40, BinaryReaderEx.KoreanEncoding).TrimEnd('\0');

                // Position
                var pos = new Vec3F(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

                switch (type)
                {
                    case 1: // Model
                    {
                        int animType = br.ReadInt32();
                        float animSpeed = br.ReadSingle();
                        int blockType = br.ReadInt32();

                        byte? unk206 = null;
                        if (version >= 0x0206) // many implementations treat >=2.6 as extra byte
                            unk206 = br.ReadByte();

                        string filename = br.ReadFixedString(80, BinaryReaderEx.KoreanEncoding).TrimEnd('\0');
                        string objName  = br.ReadFixedString(80, BinaryReaderEx.KoreanEncoding).TrimEnd('\0');

                        var rot = new Vec3F(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        var scale = new Vec3F(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

                        objects.Add(new RswModelObject
                        {
                            Name = name,
                            Position = pos,
                            AnimationType = animType,
                            AnimationSpeed = animSpeed,
                            BlockType = blockType,
                            UnknownByte206 = unk206,
                            Filename = filename,
                            ObjectName = objName,
                            Rotation = rot,
                            Scale = scale
                        });
                        break;
                    }

                    case 2: // Light
                    {
                        // Many variants store 10 floats then color+range. Keep your existing schema.
                        var unk = new float[10];
                        for (int u = 0; u < 10; u++) unk[u] = br.ReadSingle();

                        var color = new Vec3F(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        float range = br.ReadSingle();

                        objects.Add(new RswLightObject
                        {
                            Name = name,
                            Position = pos,
                            UnknownFloats = unk,
                            Color = color,
                            Range = range
                        });
                        break;
                    }

                    case 3: // Sound
                    {
                        string wav = br.ReadFixedString(80, BinaryReaderEx.KoreanEncoding).TrimEnd('\0');
                        float unknown1 = br.ReadSingle();
                        float unknown2 = br.ReadSingle();
                        var rot = new Vec3F(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        var scale = new Vec3F(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        byte[] bytes = br.ReadBytes(8);
                        float volume = br.ReadSingle();
                        int width = br.ReadInt32();
                        int height = br.ReadInt32();
                        float range = br.ReadSingle();

                        objects.Add(new RswSoundObject
                        {
                            Name = name,
                            Position = pos,
                            WaveFile = wav,
                            Unknown1 = unknown1,
                            Unknown2 = unknown2,
                            Rotation = rot,
                            Scale = scale,
                            UnknownBytes = bytes,
                            Volume = volume,
                            Width = width,
                            Height = height,
                            Range = range
                        });
                        break;
                    }

                    case 4: // Effect
                    {
                        int effectId = br.ReadInt32();
                        float emitSpeed = br.ReadSingle();
                        float p1 = br.ReadSingle();
                        float loop = br.ReadSingle();
                        float u1 = br.ReadSingle();
                        float u2 = br.ReadSingle();
                        float u3 = br.ReadSingle();
                        float u4 = br.ReadSingle();

                        objects.Add(new RswEffectObject
                        {
                            Name = name,
                            Position = pos,
                            EffectId = effectId,
                            EmitSpeed = emitSpeed,
                            Param1 = p1,
                            Loop = loop,
                            Unknown1 = u1,
                            Unknown2 = u2,
                            Unknown3 = u3,
                            Unknown4 = u4
                        });
                        break;
                    }

                    default:
                        // Hard fail = prevents infinite loops / UI freeze when misaligned.
                        throw new InvalidDataException(
                            $"Unknown RSW object type {type} at index {i} (version=0x{version:X4}). " +
                            $"If this triggers, file is misaligned/corrupt or format variant unsupported.");
                }
            }

            return new RswFileV2
            {
                Version = version,
                BuildNumber = buildNumber,
                UnknownV205 = unknownV205,

                IniFile1 = ini1,
                GndFile = gnd,
                GatFile = gat,
                IniFile2 = ini2,

                Water = water,
                Lighting = lighting,
                BoundingBox = bbox,

                ObjectCount = objectCount,
                Objects = objects
            };
        }
    }
}
