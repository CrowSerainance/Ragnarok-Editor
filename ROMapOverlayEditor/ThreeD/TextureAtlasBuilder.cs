using ROMapOverlayEditor.Imaging;
using ROMapOverlayEditor.Vfs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ROMapOverlayEditor.ThreeD
{
    public sealed class TextureAtlas
    {
        public BitmapSource AtlasBitmap { get; init; }
        public Dictionary<int, Rect> TextureRects01 { get; init; } = new(); // textureIndex -> rect in [0..1]
    }

    public static class TextureAtlasBuilder
    {
        // Simple "shelf" packer. Good enough for RO terrain textures.
        public static TextureAtlas BuildAtlas(IVfs vfs, IReadOnlyList<string> gndTextureNames, int paddingPx = 2, int maxAtlasSize = 4096)
        {
            // Load bitmaps
            var bitmaps = new List<(int idx, BitmapSource bmp)>();
            for (int i = 0; i < gndTextureNames.Count; i++)
            {
                var src = TryLoadTexture(vfs, gndTextureNames[i]);
                if (src != null)
                    bitmaps.Add((i, src));
            }

            if (bitmaps.Count == 0)
            {
                // Fallback 1x1 magenta so material exists
                var fb = MakeSolid(4, 4, Colors.Magenta);
                return new TextureAtlas
                {
                    AtlasBitmap = fb,
                    TextureRects01 = gndTextureNames.Select((_, i) => (i)).ToDictionary(i => i, i => new Rect(0, 0, 1, 1))
                };
            }

            // Sort large to small for better packing
            bitmaps.Sort((a, b) => (b.bmp.PixelHeight * b.bmp.PixelWidth).CompareTo(a.bmp.PixelHeight * a.bmp.PixelWidth));

            // Determine atlas width by heuristic
            int atlasW = Math.Min(maxAtlasSize, NextPow2(Math.Max(256, bitmaps.Max(b => b.bmp.PixelWidth + paddingPx * 2) * 4)));
            int x = paddingPx, y = paddingPx;
            int shelfH = 0;

            var placements = new List<(int idx, BitmapSource bmp, Int32Rect rect)>();

            foreach (var (idx, bmp) in bitmaps)
            {
                int w = bmp.PixelWidth;
                int h = bmp.PixelHeight;

                if (w + paddingPx * 2 > atlasW)
                    atlasW = Math.Min(maxAtlasSize, NextPow2(w + paddingPx * 2));

                if (x + w + paddingPx > atlasW)
                {
                    x = paddingPx;
                    y += shelfH + paddingPx;
                    shelfH = 0;
                }

                placements.Add((idx, bmp, new Int32Rect(x, y, w, h)));
                x += w + paddingPx;
                shelfH = Math.Max(shelfH, h);
            }

            int atlasH = NextPow2(y + shelfH + paddingPx);
            atlasH = Math.Min(maxAtlasSize, atlasH);

            var wb = new WriteableBitmap(atlasW, atlasH, 96, 96, PixelFormats.Bgra32, null);

            // Clear to transparent
            var clear = new byte[atlasW * atlasH * 4];
            wb.WritePixels(new Int32Rect(0, 0, atlasW, atlasH), clear, atlasW * 4, 0);

            // Blit each bitmap
            foreach (var p in placements)
                Blit(wb, p.bmp, p.rect);

            wb.Freeze();

            // Map to normalized rects
            var rects01 = new Dictionary<int, Rect>();
            foreach (var p in placements)
            {
                rects01[p.idx] = new Rect(
                    (double)p.rect.X / atlasW,
                    (double)p.rect.Y / atlasH,
                    (double)p.rect.Width / atlasW,
                    (double)p.rect.Height / atlasH);
            }

            // Any missing textures map to 0,0,1,1 (fallback)
            for (int i = 0; i < gndTextureNames.Count; i++)
                if (!rects01.ContainsKey(i))
                    rects01[i] = new Rect(0, 0, 1, 1);

            return new TextureAtlas
            {
                AtlasBitmap = wb,
                TextureRects01 = rects01
            };
        }

        private static void Blit(WriteableBitmap target, BitmapSource src, Int32Rect dst)
        {
            // Ensure BGRA32
            BitmapSource s = src;
            if (src.Format != PixelFormats.Bgra32)
                s = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);

            int stride = s.PixelWidth * 4;
            var buf = new byte[stride * s.PixelHeight];
            s.CopyPixels(buf, stride, 0);

            target.WritePixels(dst, buf, stride, 0);
        }

        private static BitmapSource TryLoadTexture(IVfs vfs, string name)
        {
            // GND texture names are often like "prontera_1.tga". Try TGA decode first (RO terrain is mostly TGA), then BitmapImage.
            foreach (var p in CandidatePaths(name))
            {
                var norm = VPath.Norm(p);
                if (!vfs.Exists(norm)) continue;

                byte[] bytes = vfs.ReadAllBytes(norm);
                if (bytes == null || bytes.Length < 8) continue;

                var decoded = TgaDecoder.Decode(bytes);
                if (decoded != null) return decoded;

                try
                {
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
                    // not a valid standard image; try next path
                }
            }

            return null;
        }

        private static IEnumerable<string> CandidatePaths(string tex)
        {
            tex = (tex ?? "").Replace('\\', '/');

            // already a path
            yield return tex;

            // common RO roots
            yield return $"data/texture/{tex}";
            yield return $"data/texture/유저인터페이스/{tex}";
            yield return $"texture/{tex}";
            yield return $"data/{tex}";
        }

        private static BitmapSource MakeSolid(int w, int h, Color c)
        {
            var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            byte[] buf = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                buf[i * 4 + 0] = c.B;
                buf[i * 4 + 1] = c.G;
                buf[i * 4 + 2] = c.R;
                buf[i * 4 + 3] = c.A;
            }
            wb.WritePixels(new Int32Rect(0, 0, w, h), buf, w * 4, 0);
            wb.Freeze();
            return wb;
        }

        private static int NextPow2(int v)
        {
            int p = 1;
            while (p < v) p <<= 1;
            return p;
        }
    }
}
