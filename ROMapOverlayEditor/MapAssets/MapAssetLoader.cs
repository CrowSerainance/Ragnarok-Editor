using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ROMapOverlayEditor.Imaging;

namespace ROMapOverlayEditor.MapAssets
{
    public sealed class MapAssetResult
    {
        public BitmapSource? Minimap { get; set; }
        public int GatWidthCells { get; set; }
        public int GatHeightCells { get; set; }
        public string? MinimapPath { get; set; }
        public string? GatPath { get; set; }
    }

    public static class MapAssetLoader
    {
        // These are common RO minimap locations. kRO uses a Korean folder name.
        // We try multiple paths because different clients repack assets differently.
        private static readonly string[] MinimapPathFormats =
        {
            // kRO classic
            @"texture\À¯ÀúÀÎÅÍÆäÀÌ½º\map\{0}.bmp",
            @"texture\À¯ÀúÀÎÅÍÆäÀÌ½º\map\{0}.tga",
            @"texture\À¯ÀúÀÎÅÍÆäÀÌ½º\map\{0}.png",

            // common “translated/clean” repacks
            @"texture\map\{0}.bmp",
            @"texture\map\{0}.tga",
            @"texture\map\{0}.png",

            // some packs put them under data/texture
            @"data\texture\À¯ÀúÀÎÅÍÆäÀÌ½º\map\{0}.bmp",
            @"data\texture\map\{0}.bmp",
        };

        private static readonly string[] GatPathFormats =
        {
            // common
            @"data\{0}.gat",
            @"maps\{0}.gat",
            @"data\map\{0}.gat",
            @"data\maps\{0}.gat",
        };

        /// <summary>
        /// Loads minimap bitmap + .gat dimensions for a map.
        ///
        /// IMPORTANT:
        /// This loader is GRF-implementation-agnostic.
        /// You MUST provide a delegate that can read bytes by virtual path from your open GRF (or from disk fallback).
        /// </summary>
        public static MapAssetResult TryLoad(string mapName, Func<string, byte[]?> readBytes)
        {
            if (string.IsNullOrWhiteSpace(mapName)) throw new ArgumentException("mapName is empty.");

            var result = new MapAssetResult();

            // 1) Minimap
            foreach (var fmt in MinimapPathFormats)
            {
                var p = string.Format(fmt, mapName);
                var bytes = readBytes(p);
                if (bytes == null || bytes.Length == 0) continue;

                var bmp = TryDecodeBitmap(bytes);
                if (bmp == null) continue;

                result.Minimap = bmp;
                result.MinimapPath = p;
                break;
            }

            // 2) GAT (for width/height)
            foreach (var fmt in GatPathFormats)
            {
                var p = string.Format(fmt, mapName);
                var bytes = readBytes(p);
                if (bytes == null || bytes.Length < 20) continue;

                if (TryReadGatDimensions(bytes, out int w, out int h))
                {
                    result.GatWidthCells = w;
                    result.GatHeightCells = h;
                    result.GatPath = p;
                    break;
                }
            }

            return result;
        }

        private static BitmapSource? TryDecodeBitmap(byte[] bytes)
        {
            // Handles BMP/PNG/JPG. TGA won’t decode via BitmapImage by default.
            // If you need TGA: you’ll need a small TGA decoder later; for now BMP is typically enough.
            try
            {
                var tga = TgaDecoder.Decode(bytes);
                if (tga != null) return tga;

                using var ms = new MemoryStream(bytes);
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = ms;
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch
            {
                return null;
            }
        }

        public static bool TryReadGatDimensions(byte[] gatBytes, out int widthCells, out int heightCells)
        {
            widthCells = 0;
            heightCells = 0;

            try
            {
                using var ms = new MemoryStream(gatBytes);
                using var br = new BinaryReader(ms);

                // GAT header:
                // signature "GRAT" (4 bytes)
                // version float (4 bytes)
                // width int32 (4 bytes)
                // height int32 (4 bytes)
                // then cell data...
                var sig = new string(br.ReadChars(4));
                if (!sig.Equals("GRAT", StringComparison.OrdinalIgnoreCase))
                    return false;

                _ = br.ReadSingle(); // version (unused)
                widthCells = br.ReadInt32();
                heightCells = br.ReadInt32();

                // sanity
                if (widthCells <= 0 || heightCells <= 0) return false;
                if (widthCells > 4096 || heightCells > 4096) return false;

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
