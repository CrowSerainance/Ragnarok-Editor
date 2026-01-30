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
        private static readonly string[] Roots =
        {
            "data/texture/",
            "data/model/",
            "data/model/texture/",
            "data/"
        };

        private static readonly string[] Exts =
        {
            "", ".bmp", ".tga", ".png", ".jpg", ".jpeg"
        };

        public static BitmapSource? TryLoadTexture(IVfs vfs, string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return null;

            // Normalize separators; RO uses backslashes a lot.
            var name = rawName.Replace('\\', '/').TrimStart('/');

            // Some files contain absolute-ish "texture/xxx" already.
            var candidates = new List<string>();

            // If name already contains a slash, try it as-is under "data/" first.
            candidates.Add("data/" + name);

            foreach (var root in Roots)
            {
                candidates.Add(root + name);
            }

            // Also attempt leaf-name in common roots (some RSMs only store "foo.bmp")
            var leaf = Path.GetFileName(name);
            if (!string.IsNullOrWhiteSpace(leaf))
            {
                foreach (var root in Roots)
                    candidates.Add(root + leaf);
            }

            foreach (var pathBase in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (var ext in Exts)
                {
                    var p = pathBase.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? pathBase : (pathBase + ext);

                    if (!vfs.TryReadAllBytes(p, out var bytes, out _) || bytes == null || bytes.Length < 16)
                        continue;

                    var bmp = TryDecode(bytes);
                    if (bmp != null)
                        return bmp;
                }
            }

            return null;
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
