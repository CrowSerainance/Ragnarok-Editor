using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ROMapOverlayEditor.ThreeD.Terrain
{
    public sealed class TextureAtlas
    {
        public BitmapSource AtlasImage { get; init; } = default!;
        public Dictionary<int, Rect> TexIndexToRect { get; init; } = new(); // textureIndex -> UV Rect (0..1)
    }

    public static class TextureAtlasBuilder
    {
        /// <summary>Build a texture atlas from a list of bitmaps. Shelf packing for RO ground textures.</summary>
        public static TextureAtlas Build(IReadOnlyList<BitmapSource> textures, int maxAtlasSize = 4096, int padding = 2)
        {
            if (textures == null || textures.Count == 0)
                throw new ArgumentException("No textures provided", nameof(textures));

            var normalized = textures.Select(t =>
            {
                if (t.Format == PixelFormats.Bgra32) return t;
                var conv = new FormatConvertedBitmap(t, PixelFormats.Bgra32, null, 0);
                conv.Freeze();
                return (BitmapSource)conv;
            }).ToList();

            int x = padding, y = padding, rowH = 0;
            int atlasW = padding, atlasH = padding;

            var placements = new (int X, int Y, int W, int H)[normalized.Count];

            for (int i = 0; i < normalized.Count; i++)
            {
                int w = normalized[i].PixelWidth;
                int h = normalized[i].PixelHeight;

                if (w + padding * 2 > maxAtlasSize || h + padding * 2 > maxAtlasSize)
                    throw new InvalidOperationException($"Texture {i} too large for atlas: {w}x{h}");

                if (x + w + padding > maxAtlasSize)
                {
                    x = padding;
                    y += rowH + padding;
                    rowH = 0;
                }

                placements[i] = (x, y, w, h);

                x += w + padding;
                rowH = Math.Max(rowH, h);

                atlasW = Math.Max(atlasW, x);
                atlasH = Math.Max(atlasH, y + rowH + padding);
            }

            atlasW = NextPow2(Math.Min(atlasW, maxAtlasSize));
            atlasH = NextPow2(Math.Min(atlasH, maxAtlasSize));

            var wb = new WriteableBitmap(atlasW, atlasH, 96, 96, PixelFormats.Bgra32, null);

            for (int i = 0; i < normalized.Count; i++)
            {
                var (px, py, w, h) = placements[i];
                int stride = w * 4;
                var buf = new byte[h * stride];
                normalized[i].CopyPixels(buf, stride, 0);
                wb.WritePixels(new Int32Rect(px, py, w, h), buf, stride, 0);
            }

            wb.Freeze();

            var dict = new Dictionary<int, Rect>(normalized.Count);
            for (int i = 0; i < normalized.Count; i++)
            {
                var (px, py, w, h) = placements[i];
                dict[i] = new Rect(
                    (double)px / atlasW,
                    (double)py / atlasH,
                    (double)w / atlasW,
                    (double)h / atlasH
                );
            }

            return new TextureAtlas
            {
                AtlasImage = wb,
                TexIndexToRect = dict
            };
        }

        private static int NextPow2(int v)
        {
            int p = 1;
            while (p < v) p <<= 1;
            return p;
        }
    }
}
