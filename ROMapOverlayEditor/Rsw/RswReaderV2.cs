// ============================================================================
// RswReaderV2.cs - Optimized RSW Binary Parser (from rsw_viewer reference)
// ============================================================================
// PURPOSE: Memory-efficient RSW file parser with full version support
// OPTIMIZATIONS: Span-based reading, pre-sized collections, encoding cached
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace ROMapOverlayEditor.Rsw
{
    public static class RswReaderV2
    {
        private static readonly byte[] SIGNATURE = { (byte)'G', (byte)'R', (byte)'S', (byte)'W' };
        private const int MIN_FILE_SIZE = 16;
        private const int MAX_OBJECT_COUNT = 500000;
        private static readonly Encoding KoreanEncoding;

        static RswReaderV2()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            try { KoreanEncoding = Encoding.GetEncoding(949); }
            catch { KoreanEncoding = Encoding.UTF8; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsRswFile(ReadOnlySpan<byte> data)
        {
            return data.Length >= 4 && data[0] == SIGNATURE[0] && data[1] == SIGNATURE[1] && data[2] == SIGNATURE[2] && data[3] == SIGNATURE[3];
        }

        public static RswFileV2 Read(byte[] data)
        {
            if (data == null || data.Length < MIN_FILE_SIZE)
                throw new InvalidDataException($"RSW file too small (need at least {MIN_FILE_SIZE} bytes).");
            return Read(new ReadOnlySpan<byte>(data));
        }

        public static RswFileV2 Read(ReadOnlySpan<byte> data)
        {
            var reader = new SpanReader(data);

            if (!IsRswFile(data))
                throw new InvalidDataException("Not an RSW file (missing GRSW signature).");
            reader.Skip(4);

            ushort version = reader.ReadUInt16();
            byte? buildNumber = null;
            if (version >= RswFileV2.VERSION_BUILD_NUMBER)
                buildNumber = reader.ReadByte();
            int? unknownV205 = null;
            if (version >= RswFileV2.VERSION_UNKNOWN_INT)
                unknownV205 = reader.ReadInt32();

            string iniFile1 = ReadFixedString(ref reader, 40);
            string gndFile = ReadFixedString(ref reader, 40);
            string gatFile = version > RswFileV2.VERSION_GAT_FILE ? ReadFixedString(ref reader, 40) : gndFile;
            string iniFile2 = ReadFixedString(ref reader, 40);

            RswWaterInfo? water = null;
            if (version >= 0x0103 && version < RswFileV2.VERSION_WATER_IN_GND)
                water = ReadWaterInfo(ref reader, version);

            RswLightingInfo? lighting = null;
            if (version >= RswFileV2.VERSION_LIGHTING)
                lighting = ReadLightingInfo(ref reader, version);

            RswBoundingBox? bbox = null;
            if (version >= RswFileV2.VERSION_BBOX)
                bbox = new RswBoundingBox(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());

            int objectCount = reader.ReadInt32();
            if (objectCount < 0 || objectCount > MAX_OBJECT_COUNT)
                throw new InvalidDataException($"Invalid object count: {objectCount} (max {MAX_OBJECT_COUNT})");

            var objects = new List<RswObjectBase>(objectCount);
            for (int i = 0; i < objectCount; i++)
            {
                if (reader.Remaining < 4) break;
                int objectType = reader.ReadInt32();
                RswObjectBase? obj = objectType switch
                {
                    1 => ReadModelObject(ref reader, version, buildNumber ?? 0),
                    2 => ReadLightObject(ref reader, version),
                    3 => ReadSoundObject(ref reader, version),
                    4 => ReadEffectObject(ref reader, version),
                    _ => throw new InvalidDataException($"Unknown RSW object type {objectType} at index {i} (Offset 0x{reader.Position - 4:X}, Version 0x{version:X4})")
                };
                if (obj != null) objects.Add(obj);
            }

            return new RswFileV2
            {
                Version = version,
                BuildNumber = buildNumber,
                UnknownV205 = unknownV205,
                IniFile1 = iniFile1,
                GndFile = gndFile,
                GatFile = gatFile,
                IniFile2 = iniFile2,
                Water = water,
                Lighting = lighting,
                BoundingBox = bbox,
                ObjectCount = objectCount,
                Objects = objects
            };
        }

        private static RswWaterInfo ReadWaterInfo(ref SpanReader reader, ushort version)
        {
            float height = 0; int type = 0; float amplitude = 0; float waveSpeed = 0; float wavePitch = 0; int animSpeed = 0;
            if (version >= 0x0103) height = reader.ReadSingle();
            if (version >= RswFileV2.VERSION_WATER_EXT) { type = reader.ReadInt32(); amplitude = reader.ReadSingle(); waveSpeed = reader.ReadSingle(); wavePitch = reader.ReadSingle(); }
            if (version >= RswFileV2.VERSION_WATER_ANIM) animSpeed = reader.ReadInt32();
            return new RswWaterInfo { Height = height, Type = type, Amplitude = amplitude, WaveSpeed = waveSpeed, WavePitch = wavePitch, TextureAnimSpeed = animSpeed };
        }

        private static RswLightingInfo ReadLightingInfo(ref SpanReader reader, ushort version)
        {
            int longitude = reader.ReadInt32(); int latitude = reader.ReadInt32();
            var diffuse = ReadVec3(ref reader); var ambient = ReadVec3(ref reader);
            float shadowOpacity = (version >= RswFileV2.VERSION_SHADOW_OPACITY) ? reader.ReadSingle() : 1.0f;
            return new RswLightingInfo { Longitude = longitude, Latitude = latitude, DiffuseColor = diffuse, AmbientColor = ambient, ShadowOpacity = shadowOpacity };
        }

        private static RswModelObject ReadModelObject(ref SpanReader reader, ushort version, int buildNumber)
        {
            string name = ""; int animType = 0; float animSpeed = 0; int blockType = 0; byte? unknownByte = null;
            if (version >= 0x0103) { name = ReadFixedString(ref reader, 40); animType = reader.ReadInt32(); animSpeed = reader.ReadSingle(); blockType = reader.ReadInt32(); }
            if (version >= RswFileV2.VERSION_WATER_IN_GND && buildNumber > 161) unknownByte = reader.ReadByte();
            string filename = ReadFixedString(ref reader, 80);
            string objectName = (version >= 0x0103) ? ReadFixedString(ref reader, 80) : "";
            var position = ReadVec3(ref reader); var rotation = ReadVec3(ref reader); var scale = ReadVec3(ref reader);
            return new RswModelObject { Name = name, AnimationType = animType, AnimationSpeed = animSpeed, BlockType = blockType, UnknownByte206 = unknownByte, Filename = filename, ObjectName = objectName, Position = position, Rotation = rotation, Scale = scale };
        }

        private static RswLightObject ReadLightObject(ref SpanReader reader, ushort version)
        {
            string name = (version >= 0x0103) ? ReadFixedString(ref reader, 40) : "";
            var position = ReadVec3(ref reader);
            var unknownFloats = new float[10];
            if (version >= 0x0103) { for (int i = 0; i < 10; i++) unknownFloats[i] = reader.ReadSingle(); }
            var color = ReadVec3(ref reader); float range = reader.ReadSingle();
            return new RswLightObject { Name = name, Position = position, UnknownFloats = unknownFloats, Color = color, Range = range };
        }

        private static RswSoundObject ReadSoundObject(ref SpanReader reader, ushort version)
        {
            string name = ReadFixedString(ref reader, 80); string waveFile = ReadFixedString(ref reader, 80);
            float unknown1 = reader.ReadSingle(); float unknown2 = reader.ReadSingle();
            var rotation = ReadVec3(ref reader); var scale = ReadVec3(ref reader);
            var unknownBytes = reader.ReadBytes(8);
            var position = ReadVec3(ref reader);
            float volume = reader.ReadSingle(); int width = reader.ReadInt32(); int height = reader.ReadInt32(); float range = reader.ReadSingle();
            if (reader.Remaining >= 4) reader.Skip(4);
            return new RswSoundObject { Name = name, WaveFile = waveFile, Unknown1 = unknown1, Unknown2 = unknown2, Rotation = rotation, Scale = scale, UnknownBytes = unknownBytes, Position = position, Volume = volume, Width = width, Height = height, Range = range };
        }

        private static RswEffectObject ReadEffectObject(ref SpanReader reader, ushort version)
        {
            string name = ReadFixedString(ref reader, 80);
            var position = ReadVec3(ref reader);
            int effectId = reader.ReadInt32(); float emitSpeed = reader.ReadSingle(); float param1 = reader.ReadSingle(); float loop = reader.ReadSingle();
            float unknown1 = reader.ReadSingle(); float unknown2 = reader.ReadSingle(); float unknown3 = reader.ReadSingle(); float unknown4 = reader.ReadSingle();
            return new RswEffectObject { Name = name, Position = position, EffectId = effectId, EmitSpeed = emitSpeed, Param1 = param1, Loop = loop, Unknown1 = unknown1, Unknown2 = unknown2, Unknown3 = unknown3, Unknown4 = unknown4 };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vec3F ReadVec3(ref SpanReader reader) => new Vec3F(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

        private static string ReadFixedString(ref SpanReader reader, int length)
        {
            var bytes = reader.ReadBytesSpan(length);
            int end = bytes.IndexOf((byte)0); if (end < 0) end = length; if (end == 0) return string.Empty;
            return KoreanEncoding.GetString(bytes.Slice(0, end)).Trim();
        }

        private ref struct SpanReader
        {
            private ReadOnlySpan<byte> _data;
            private int _position;
            public SpanReader(ReadOnlySpan<byte> data) { _data = data; _position = 0; }
            public int Position => _position;
            public int Remaining => _data.Length - _position;
            [MethodImpl(MethodImplOptions.AggressiveInlining)] public void Skip(int bytes) => _position += bytes;
            [MethodImpl(MethodImplOptions.AggressiveInlining)] public byte ReadByte() => _data[_position++];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ushort ReadUInt16() { ushort v = (ushort)(_data[_position] | (_data[_position + 1] << 8)); _position += 2; return v; }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int ReadInt32() { int v = _data[_position] | (_data[_position + 1] << 8) | (_data[_position + 2] << 16) | (_data[_position + 3] << 24); _position += 4; return v; }
            [MethodImpl(MethodImplOptions.AggressiveInlining)] public float ReadSingle() => BitConverter.Int32BitsToSingle(ReadInt32());
            public byte[] ReadBytes(int count) { var r = _data.Slice(_position, count).ToArray(); _position += count; return r; }
            [MethodImpl(MethodImplOptions.AggressiveInlining)] public ReadOnlySpan<byte> ReadBytesSpan(int count) { var r = _data.Slice(_position, count); _position += count; return r; }
        }
    }
}
