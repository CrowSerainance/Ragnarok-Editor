using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using ROMapOverlayEditor.Vfs;
using ROMapOverlayEditor.Imaging;

namespace ROMapOverlayEditor.Rsm
{
    internal static class RsmTextureResolver
    {
        // RO model textures are often referenced as "texture\\xxx.bmp" or "xxx.bmp" etc.
        // We try multiple candidate roots + extensions and return the first decodable BitmapSource.
        public static BitmapSource? TryLoadTexture(IVfs vfs, string rawName)
        {
            var bytes = TryLoadTextureBytes(vfs, rawName);
            return bytes != null ? TryDecode(bytes) : null;
        }

        public static byte[]? TryLoadTextureBytes(IVfs vfs, string textureName)
        {
            foreach (var candidate in BuildTextureCandidates(textureName))
            {
                if (TryReadBytes(vfs, candidate, out var bytes))
                    return bytes;
            }
            return null;
        }

        private static bool TryReadBytes(IVfs vfs, string vpath, out byte[]? bytes)
        {
            bytes = null;
            if (vfs == null) return false;
            if (string.IsNullOrWhiteSpace(vpath)) return false;

            if (vfs.TryReadAllBytes(vpath, out var tmp, out var err) && tmp != null && tmp.Length > 0)
            {
                bytes = tmp;
                return true;
            }
            return false;
        }

        private static IEnumerable<string> BuildTextureCandidates(string rawName)
        {
            // RO is messy: texture names may be without extension, with backslashes, etc.
            var n = (rawName ?? "").Replace('\\', '/').Trim();
            while (n.StartsWith("/")) n = n.Substring(1);

            if (string.IsNullOrWhiteSpace(n))
                yield break;

            // If already rooted in data/, keep it first.
            if (n.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
                yield return n;

            // Common RO texture locations
            yield return $"data/texture/{n}";
            yield return $"data/texture/À¯ÀúÀÎÅÍÆäÀÌ½º/{n}";
            yield return n;

            // Add extensions if none
            bool hasExt =
                n.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
                n.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ||
                n.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                n.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase);

            if (!hasExt)
            {
                foreach (var basePath in new[]
                {
                    n.StartsWith("data/", StringComparison.OrdinalIgnoreCase) ? n : $"data/texture/{n}",
                    $"data/texture/{n}",
                    n
                })
                {
                    yield return basePath + ".bmp";
                    yield return basePath + ".tga";
                    yield return basePath + ".png";
                    yield return basePath + ".jpg";
                }
            }
        }

        private static BitmapSource? TryDecode(byte[] bytes)
        {
            // Try TGA first (common in RO terrain and model textures)
            try
            {
                var tga = TgaDecoder.Decode(bytes);
                if (tga != null) return tga;
            }
            catch { /* ignore */ }

            try
            {
                using var ms = new MemoryStream(bytes);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch
            {
                return null;
            }
        }
        /// <summary>Loads texture from first matching path. TGA and standard formats supported. Returns BitmapSource for materials.</summary>
        public static BitmapSource? TryLoadTextureBitmap(IVfs vfs, IEnumerable<string> candidatePaths)
        {
            foreach (var p in candidatePaths)
            {
                if (string.IsNullOrWhiteSpace(p))
                    continue;

                if (vfs.TryReadAllBytes(p, out var bytes, out _) && bytes != null && bytes.Length > 0)
                {
                    var bmp = TryDecode(bytes);
                    if (bmp != null)
                        return bmp;
                }
            }
            return null;
        }
    }
}
