using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using ROMapOverlayEditor.Imaging;

namespace ROMapOverlayEditor.Map3D
{
    /// <summary>
    /// Centralized texture loading that handles TGA, PNG, JPG, BMP formats.
    /// TGA files use TgaDecoder since WPF's BitmapImage doesn't support TGA.
    /// </summary>
    public static class TextureLoader
    {
        /// <summary>
        /// Loads a texture from raw byte data, automatically detecting format.
        /// </summary>
        /// <param name="data">Raw texture file bytes</param>
        /// <param name="fileName">Original filename (used to detect format)</param>
        /// <returns>BitmapSource ready for WPF 3D materials, or null if loading fails</returns>
        public static BitmapSource? LoadTexture(byte[] data, string fileName)
        {
            if (data == null || data.Length == 0)
            {
                System.Diagnostics.Debug.WriteLine($"TextureLoader: Empty data for {fileName}");
                return null;
            }

            try
            {
                BitmapSource? bitmap = BytesToBitmapSource(data, fileName);

                if (bitmap != null && bitmap.CanFreeze)
                    bitmap.Freeze();

                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TextureLoader: Failed to load {fileName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Decode texture bytes to BitmapSource. Tries TGA first (RO textures are often TGA);
        /// falls back to WPF BitmapImage for PNG/JPG/BMP/GIF.
        /// </summary>
        public static BitmapSource? BytesToBitmapSource(byte[] data, string pathOrHint)
        {
            if (data == null || data.Length == 0) return null;

            try
            {
                // TGA: WPF BitmapImage cannot decode TGA; use our decoder
                var tga = TgaDecoder.Decode(data);
                if (tga != null) return tga;

                // PNG, JPG, BMP, GIF - use WPF's built-in BitmapImage
                return LoadStandardImage(data);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Loads standard image formats using WPF's BitmapImage.
        /// </summary>
        private static BitmapSource LoadStandardImage(byte[] data)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = new MemoryStream(data);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            return bitmap;
        }

        /// <summary>
        /// Creates a WPF 3D DiffuseMaterial from texture data.
        /// </summary>
        /// <param name="data">Raw texture file bytes</param>
        /// <param name="fileName">Original filename</param>
        /// <returns>DiffuseMaterial with ImageBrush, or fallback solid color if loading fails</returns>
        public static DiffuseMaterial CreateMaterial(byte[] data, string fileName)
        {
            var bitmap = LoadTexture(data, fileName);

            if (bitmap != null)
            {
                var brush = new ImageBrush(bitmap)
                {
                    TileMode = TileMode.Tile,
                    ViewportUnits = BrushMappingMode.Absolute
                };
                return new DiffuseMaterial(brush);
            }

            System.Diagnostics.Debug.WriteLine($"TextureLoader: Using fallback color for {fileName}");
            return new DiffuseMaterial(new SolidColorBrush(Colors.Magenta));
        }
    }
}
