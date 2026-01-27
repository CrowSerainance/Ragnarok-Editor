// ============================================================================
// GndReaderV2.cs - Optimized GND Binary Parser (from rsw_viewer reference)
// ============================================================================
// PURPOSE: Memory-efficient GND file parser with streaming lightmap support
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace ROMapOverlayEditor.Gnd
{
    public sealed class GndReadOptions
    {
        public bool LoadLightmaps { get; init; } = true;
        public bool SkipSurfaces { get; init; } = false;
        public static readonly GndReadOptions Default = new();
        public static readonly GndReadOptions Preview = new() { LoadLightmaps = false };
        public static readonly GndReadOptions HeightOnly = new() { LoadLightmaps = false, SkipSurfaces = true };
    }

    public static class GndReaderV2
    {
        private static readonly byte[] SIGNATURE = { (byte)'G', (byte)'R', (byte)'G', (byte)'N' };
        private const int MIN_FILE_SIZE = 24;
        private const int LIGHTMAP_ENTRY_SIZE = 256;
        /// <summary>Surface record size: 8 floats + 2 shorts + 4 bytes = 40</summary>
        private const int SURFACE_RECORD_SIZE = 40;
        private static readonly Encoding KoreanEncoding;

        static GndReaderV2()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            try { KoreanEncoding = Encoding.GetEncoding(949); }
            catch { KoreanEncoding = Encoding.UTF8; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsGndFile(ReadOnlySpan<byte> data) =>
            data.Length >= 4 && data[0] == SIGNATURE[0] && data[1] == SIGNATURE[1] && data[2] == SIGNATURE[2] && data[3] == SIGNATURE[3];

        public static GndFileV2 Read(byte[] data) => Read(data, GndReadOptions.Default);

        public static GndFileV2 Read(byte[] data, GndReadOptions options)
        {
            if (data == null || data.Length < MIN_FILE_SIZE)
                throw new InvalidDataException($"GND file too small (need at least {MIN_FILE_SIZE} bytes).");
            return Read(new ReadOnlySpan<byte>(data), options);
        }

        public static GndFileV2 Read(ReadOnlySpan<byte> data, GndReadOptions options)
        {
            var reader = new SpanReader(data);
            if (!IsGndFile(data)) throw new InvalidDataException("Not a GND file (missing GRGN signature).");
            reader.Skip(4);

            ushort version = reader.ReadUInt16();
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            float tileScale = reader.ReadSingle();
            int textureCount = reader.ReadInt32();
            reader.ReadInt32(); // maxTexNameLen

            if (width <= 0 || height <= 0 || width > 10000 || height > 10000)
                throw new InvalidDataException($"Invalid GND dimensions: {width}x{height}");

            var textures = new List<GndTextureV2>(textureCount);
            for (int i = 0; i < textureCount; i++)
            {
                string file = ReadFixedString(ref reader, 80);
                string name = ReadFixedString(ref reader, 80);
                textures.Add(new GndTextureV2 { Filename = file, Name = name });
            }

            int lightmapCount = reader.ReadInt32();
            int lightmapWidth = reader.ReadInt32();
            int lightmapHeight = reader.ReadInt32();
            int gridSizeCell = reader.ReadInt32();
            long lightmapBytes = (long)lightmapCount * LIGHTMAP_ENTRY_SIZE;

            byte[]? lightmapData = null;
            if (options.LoadLightmaps && lightmapCount > 0)
            {
                if (reader.Remaining < lightmapBytes) throw new InvalidDataException("GND truncated in lightmap section.");
                lightmapData = reader.ReadBytes((int)lightmapBytes);
            }
            else
                reader.Skip((int)Math.Min(lightmapBytes, reader.Remaining));

            var lightmaps = new GndLightmapInfo { Count = lightmapCount, CellWidth = lightmapWidth, CellHeight = lightmapHeight, GridSizeCell = gridSizeCell, RawData = lightmapData };

            int surfaceCount = reader.ReadInt32();
            var surfaces = new List<GndSurfaceTile>(surfaceCount);
            if (!options.SkipSurfaces)
            {
                for (int i = 0; i < surfaceCount; i++)
                {
                    float u1 = reader.ReadSingle(), u2 = reader.ReadSingle(), u3 = reader.ReadSingle(), u4 = reader.ReadSingle();
                    float v1 = reader.ReadSingle(), v2 = reader.ReadSingle(), v3 = reader.ReadSingle(), v4 = reader.ReadSingle();
                    short texIndex = reader.ReadInt16();
                    ushort lmIndex = reader.ReadUInt16();
                    byte b = reader.ReadByte(), g = reader.ReadByte(), r = reader.ReadByte(), a = reader.ReadByte();
                    surfaces.Add(new GndSurfaceTile(u1, u2, u3, u4, v1, v2, v3, v4, texIndex, lmIndex, b, g, r, a));
                }
            }
            else
                reader.Skip(surfaceCount * SURFACE_RECORD_SIZE);

            var cubes = new GndCubeV2_Legacy[width, height];
            bool intTileIds = version >= GndFileV2.VERSION_INT_TILE_IDS;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float h00 = reader.ReadSingle(), h10 = reader.ReadSingle(), h01 = reader.ReadSingle(), h11 = reader.ReadSingle();
                    int tileUp = intTileIds ? reader.ReadInt32() : reader.ReadUInt16();
                    int tileSide = intTileIds ? reader.ReadInt32() : reader.ReadUInt16();
                    int tileFront = intTileIds ? reader.ReadInt32() : reader.ReadUInt16();
                    cubes[x, y] = new GndCubeV2_Legacy(h00, h10, h01, h11, tileUp, tileSide, tileFront);
                }

            GndWaterInfo? water = null;
            if (version >= GndFileV2.VERSION_WATER_INFO && reader.Remaining >= 24)
                water = new GndWaterInfo { Height = reader.ReadSingle(), Type = reader.ReadInt32(), Amplitude = reader.ReadSingle(), WaveSpeed = reader.ReadSingle(), WavePitch = reader.ReadSingle(), AnimationSpeed = reader.ReadInt32() };

            return new GndFileV2 { Version = version, Width = width, Height = height, TileScale = tileScale, Textures = textures, Lightmaps = lightmaps, Surfaces = surfaces, Cubes = cubes, Water = water };
        }

        public static (int width, int height, float tileScale) ReadDimensions(ReadOnlySpan<byte> data)
        {
            if (data.Length < 18) throw new InvalidDataException("GND file too small for dimension read.");
            if (!IsGndFile(data)) throw new InvalidDataException("Not a GND file.");
            var reader = new SpanReader(data);
            reader.Skip(6);
            return (reader.ReadInt32(), reader.ReadInt32(), reader.ReadSingle());
        }

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
            public int Remaining => _data.Length - _position;
            [MethodImpl(MethodImplOptions.AggressiveInlining)] public void Skip(int bytes) => _position += bytes;
            [MethodImpl(MethodImplOptions.AggressiveInlining)] public byte ReadByte() => _data[_position++];
            [MethodImpl(MethodImplOptions.AggressiveInlining)] public short ReadInt16() { short v = (short)(_data[_position] | (_data[_position + 1] << 8)); _position += 2; return v; }
            [MethodImpl(MethodImplOptions.AggressiveInlining)] public ushort ReadUInt16() { ushort v = (ushort)(_data[_position] | (_data[_position + 1] << 8)); _position += 2; return v; }
            [MethodImpl(MethodImplOptions.AggressiveInlining)] public int ReadInt32() { int v = _data[_position] | (_data[_position + 1] << 8) | (_data[_position + 2] << 16) | (_data[_position + 3] << 24); _position += 4; return v; }
            [MethodImpl(MethodImplOptions.AggressiveInlining)] public float ReadSingle() => BitConverter.Int32BitsToSingle(ReadInt32());
            public byte[] ReadBytes(int count) { var r = _data.Slice(_position, count).ToArray(); _position += count; return r; }
            [MethodImpl(MethodImplOptions.AggressiveInlining)] public ReadOnlySpan<byte> ReadBytesSpan(int count) { var r = _data.Slice(_position, count); _position += count; return r; }
        }
    }
}
