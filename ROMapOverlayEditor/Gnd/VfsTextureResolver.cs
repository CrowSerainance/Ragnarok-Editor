using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ROMapOverlayEditor.Map3D;
using ROMapOverlayEditor.Vfs;

namespace ROMapOverlayEditor.Gnd
{
    public sealed class VfsTextureResolver
    {
        private readonly CompositeVfs _vfs;
        private readonly Dictionary<string, BitmapSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

        public VfsTextureResolver(CompositeVfs vfs)
        {
            _vfs = vfs;
        }

        /// <summary>Loads a texture (TGA, PNG, JPG, BMP). Returns BitmapSource for WPF 3D materials.</summary>
        public BitmapSource? TryLoadTexture(string textureFile)
        {
            if (string.IsNullOrWhiteSpace(textureFile))
                return null;

            textureFile = textureFile.Replace('\\', '/').TrimStart('/');

            if (_cache.TryGetValue(textureFile, out var cached))
                return cached;

            // BrowEdit loads: data/texture/{file}. Try several common roots.
            string[] candidates =
            {
                "data/texture/" + textureFile,
                "data/texture/" + Path.GetFileName(textureFile),
                "data/texture/" + textureFile + ".tga",
                "data/texture/" + Path.GetFileName(textureFile) + ".tga",
                "texture/" + textureFile,
                textureFile,
            };

            foreach (var p in candidates)
            {
                if (!_vfs.Exists(p)) continue;

                try
                {
                    var bytes = _vfs.ReadAllBytes(p);
                    var img = TextureLoader.BytesToBitmapSource(bytes, p);
                    if (img != null)
                    {
                        if (img.CanFreeze) img.Freeze();
                        _cache[textureFile] = img;
                        return img;
                    }
                }
                catch
                {
                    // continue
                }
            }

            _cache[textureFile] = null;
            return null;
        }
    }
}
