using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;
using ROMapOverlayEditor.Vfs;

namespace ROMapOverlayEditor.Gnd
{
    public sealed class VfsTextureResolver
    {
        private readonly CompositeVfs _vfs;
        private readonly Dictionary<string, BitmapImage?> _cache = new(StringComparer.OrdinalIgnoreCase);

        public VfsTextureResolver(CompositeVfs vfs)
        {
            _vfs = vfs;
        }

        public BitmapImage? TryLoadTexture(string textureFile)
        {
            if (string.IsNullOrWhiteSpace(textureFile))
                return null;

            textureFile = textureFile.Replace('\\', '/').TrimStart('/');

            if (_cache.TryGetValue(textureFile, out var cached))
                return cached;

            // BrowEdit loads: data/texture/{file}
            // Try several common roots
            string[] candidates =
            {
                "data/texture/" + textureFile,
                "data/texture/" + Path.GetFileName(textureFile),
                "texture/" + textureFile,
                textureFile,
            };

            foreach (var p in candidates)
            {
                if (!_vfs.Exists(p)) continue;

                try
                {
                    var bytes = _vfs.ReadAllBytes(p);
                    var img = LoadBitmap(bytes);
                    _cache[textureFile] = img;
                    return img;
                }
                catch
                {
                    // continue
                }
            }

            _cache[textureFile] = null;
            return null;
        }

        private static BitmapImage LoadBitmap(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
    }
}
