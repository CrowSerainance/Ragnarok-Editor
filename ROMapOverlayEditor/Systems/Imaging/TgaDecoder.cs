// ============================================================================
// TgaDecoder.cs - Complete TGA Image Decoder for Ragnarok Online Textures
// ============================================================================
// This decoder handles all TGA formats used in RO:
//   - Uncompressed true-color (type 2)
//   - RLE compressed true-color (type 10)
//   - Uncompressed grayscale (type 3)
//   - RLE compressed grayscale (type 11)
//   - 8/16/24/32 bit pixel depths
//   - Top-left and bottom-left origins
//   - Magenta (255,0,255) transparency (RO convention)
// ============================================================================

using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ROMapOverlayEditor.Imaging
{
    /// <summary>
    /// Decodes TGA (Targa) image files to WPF BitmapSource.
    /// Handles all common formats used in Ragnarok Online texture files.
    /// </summary>
    public static class TgaDecoder
    {
        // ====================================================================
        // PUBLIC API
        // ====================================================================

        /// <summary>
        /// Decode a TGA file from raw bytes into a WPF BitmapSource.
        /// Returns null if decoding fails.
        /// </summary>
        /// <param name="tgaBytes">Raw TGA file bytes</param>
        /// <returns>Decoded BitmapSource, or null on failure</returns>
        public static BitmapSource? Decode(byte[] tgaBytes)
        {
            if (tgaBytes == null || tgaBytes.Length < 18)
                return null;

            try
            {
                using var ms = new MemoryStream(tgaBytes);
                using var br = new BinaryReader(ms);

                // ============================================================
                // TGA HEADER (18 bytes)
                // ============================================================
                byte idLength = br.ReadByte();           // 0: Length of image ID field
                byte colorMapType = br.ReadByte();     // 1: 0=no colormap, 1=has colormap
                byte imageType = br.ReadByte();        // 2: Image type code

                // Color map specification (5 bytes)
                br.ReadUInt16();  // 3-4: First color map entry index
                ushort colorMapLength = br.ReadUInt16();      // 5-6: Color map entry count
                byte colorMapEntrySize = br.ReadByte();       // 7: Bits per color map entry

                // Image specification (10 bytes)
                br.ReadUInt16();  // 8-9: X origin
                br.ReadUInt16();  // 10-11: Y origin
                ushort width = br.ReadUInt16();          // 12-13: Image width
                ushort height = br.ReadUInt16();         // 14-15: Image height
                byte pixelDepth = br.ReadByte();         // 16: Bits per pixel
                byte imageDescriptor = br.ReadByte();    // 17: Image descriptor

                // Validate dimensions
                if (width == 0 || height == 0 || width > 8192 || height > 8192)
                    return null;

                // Skip image ID field if present
                if (idLength > 0)
                    br.ReadBytes(idLength);

                // Skip color map if present (we don't use indexed TGAs in RO typically)
                if (colorMapType == 1 && colorMapLength > 0)
                {
                    int colorMapBytes = (colorMapLength * colorMapEntrySize + 7) / 8;
                    br.ReadBytes(colorMapBytes);
                }

                // Determine if image origin is top-left or bottom-left
                // Bit 5 of imageDescriptor: 0 = bottom-left, 1 = top-left
                bool topToBottom = (imageDescriptor & 0x20) != 0;

                // Decode based on image type
                byte[]? pixels = imageType switch
                {
                    2 => DecodeUncompressedTrueColor(br, width, height, pixelDepth),
                    10 => DecodeRleTrueColor(br, width, height, pixelDepth),
                    3 => DecodeUncompressedGrayscale(br, width, height, pixelDepth),
                    11 => DecodeRleGrayscale(br, width, height, pixelDepth),
                    1 => DecodeIndexed(br, width, height, pixelDepth, tgaBytes, 18 + idLength, colorMapLength, colorMapEntrySize),
                    _ => null
                };

                if (pixels == null)
                    return null;

                // Apply magenta transparency (RO convention: RGB 255,0,255 = transparent)
                ApplyMagentaTransparency(pixels);

                // Flip vertically if origin is bottom-left (default TGA)
                if (!topToBottom)
                    FlipVertical(pixels, width, height);

                // Create WPF BitmapSource
                var bitmap = BitmapSource.Create(
                    width, height,
                    96, 96,
                    PixelFormats.Bgra32,
                    null,
                    pixels,
                    width * 4
                );

                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        // ====================================================================
        // DECODING METHODS
        // ====================================================================

        /// <summary>
        /// Decode uncompressed true-color TGA (type 2).
        /// </summary>
        private static byte[]? DecodeUncompressedTrueColor(BinaryReader br, int width, int height, int bpp)
        {
            int pixelCount = width * height;
            byte[] pixels = new byte[pixelCount * 4];

            int bytesPerPixel = bpp / 8;
            if (bytesPerPixel < 2 || bytesPerPixel > 4)
                return null;

            for (int i = 0; i < pixelCount; i++)
            {
                ReadPixel(br, pixels, i * 4, bytesPerPixel);
            }

            return pixels;
        }

        /// <summary>
        /// Decode RLE-compressed true-color TGA (type 10).
        /// </summary>
        private static byte[]? DecodeRleTrueColor(BinaryReader br, int width, int height, int bpp)
        {
            int pixelCount = width * height;
            byte[] pixels = new byte[pixelCount * 4];

            int bytesPerPixel = bpp / 8;
            if (bytesPerPixel < 2 || bytesPerPixel > 4)
                return null;

            int currentPixel = 0;

            while (currentPixel < pixelCount)
            {
                byte packetHeader = br.ReadByte();
                int packetLength = (packetHeader & 0x7F) + 1;

                if ((packetHeader & 0x80) != 0)
                {
                    // RLE packet: one pixel repeated
                    byte[] pixel = new byte[4];
                    ReadPixel(br, pixel, 0, bytesPerPixel);

                    for (int i = 0; i < packetLength && currentPixel < pixelCount; i++)
                    {
                        int idx = currentPixel * 4;
                        pixels[idx + 0] = pixel[0];
                        pixels[idx + 1] = pixel[1];
                        pixels[idx + 2] = pixel[2];
                        pixels[idx + 3] = pixel[3];
                        currentPixel++;
                    }
                }
                else
                {
                    // Raw packet: multiple pixels
                    for (int i = 0; i < packetLength && currentPixel < pixelCount; i++)
                    {
                        ReadPixel(br, pixels, currentPixel * 4, bytesPerPixel);
                        currentPixel++;
                    }
                }
            }

            return pixels;
        }

        /// <summary>
        /// Decode uncompressed grayscale TGA (type 3).
        /// </summary>
        private static byte[]? DecodeUncompressedGrayscale(BinaryReader br, int width, int height, int bpp)
        {
            int pixelCount = width * height;
            byte[] pixels = new byte[pixelCount * 4];

            int bytesPerPixel = bpp / 8;

            for (int i = 0; i < pixelCount; i++)
            {
                byte gray = br.ReadByte();
                byte alpha = (bytesPerPixel == 2) ? br.ReadByte() : (byte)255;

                int idx = i * 4;
                pixels[idx + 0] = gray;  // B
                pixels[idx + 1] = gray;  // G
                pixels[idx + 2] = gray;  // R
                pixels[idx + 3] = alpha; // A
            }

            return pixels;
        }

        /// <summary>
        /// Decode RLE-compressed grayscale TGA (type 11).
        /// </summary>
        private static byte[]? DecodeRleGrayscale(BinaryReader br, int width, int height, int bpp)
        {
            int pixelCount = width * height;
            byte[] pixels = new byte[pixelCount * 4];

            int bytesPerPixel = bpp / 8;
            int currentPixel = 0;

            while (currentPixel < pixelCount)
            {
                byte packetHeader = br.ReadByte();
                int packetLength = (packetHeader & 0x7F) + 1;

                if ((packetHeader & 0x80) != 0)
                {
                    // RLE packet
                    byte gray = br.ReadByte();
                    byte alpha = (bytesPerPixel == 2) ? br.ReadByte() : (byte)255;

                    for (int i = 0; i < packetLength && currentPixel < pixelCount; i++)
                    {
                        int idx = currentPixel * 4;
                        pixels[idx + 0] = gray;
                        pixels[idx + 1] = gray;
                        pixels[idx + 2] = gray;
                        pixels[idx + 3] = alpha;
                        currentPixel++;
                    }
                }
                else
                {
                    // Raw packet
                    for (int i = 0; i < packetLength && currentPixel < pixelCount; i++)
                    {
                        byte gray = br.ReadByte();
                        byte alpha = (bytesPerPixel == 2) ? br.ReadByte() : (byte)255;

                        int idx = currentPixel * 4;
                        pixels[idx + 0] = gray;
                        pixels[idx + 1] = gray;
                        pixels[idx + 2] = gray;
                        pixels[idx + 3] = alpha;
                        currentPixel++;
                    }
                }
            }

            return pixels;
        }

        /// <summary>
        /// Decode indexed/paletted TGA (type 1) - rare in RO but included for completeness.
        /// </summary>
        private static byte[]? DecodeIndexed(BinaryReader br, int width, int height, int bpp,
            byte[] tgaBytes, int colorMapStart, int colorMapLength, int colorMapEntrySize)
        {
            if (bpp != 8 || colorMapLength == 0)
                return null;

            int pixelCount = width * height;
            byte[] pixels = new byte[pixelCount * 4];

            // Read color map
            int entryBytes = (colorMapEntrySize + 7) / 8;
            if (entryBytes < 1 || entryBytes > 4) return null;
            int colorMapBytes = colorMapLength * entryBytes;
            if (colorMapStart + colorMapBytes > tgaBytes.Length) return null;

            byte[][] palette = new byte[colorMapLength][];
            using (var paletteMs = new MemoryStream(tgaBytes, colorMapStart, colorMapBytes))
            using (var paletteBr = new BinaryReader(paletteMs))
            {
                for (int i = 0; i < colorMapLength; i++)
                {
                    palette[i] = new byte[4];
                    ReadPixel(paletteBr, palette[i], 0, entryBytes);
                }
            }

            // Decode indices
            for (int i = 0; i < pixelCount; i++)
            {
                byte index = br.ReadByte();
                if (index < colorMapLength)
                {
                    int idx = i * 4;
                    pixels[idx + 0] = palette[index][0];
                    pixels[idx + 1] = palette[index][1];
                    pixels[idx + 2] = palette[index][2];
                    pixels[idx + 3] = palette[index][3];
                }
            }

            return pixels;
        }

        // ====================================================================
        // HELPER METHODS
        // ====================================================================

        /// <summary>
        /// Read a single pixel from the stream into BGRA format.
        /// </summary>
        private static void ReadPixel(BinaryReader br, byte[] dest, int offset, int bytesPerPixel)
        {
            switch (bytesPerPixel)
            {
                case 2:
                    // 16-bit: ARRRRRGG GGGBBBBB (1-5-5-5)
                    ushort pixel16 = br.ReadUInt16();
                    dest[offset + 0] = (byte)((pixel16 & 0x001F) << 3);        // B
                    dest[offset + 1] = (byte)(((pixel16 & 0x03E0) >> 5) << 3); // G
                    dest[offset + 2] = (byte)(((pixel16 & 0x7C00) >> 10) << 3);// R
                    dest[offset + 3] = (byte)((pixel16 & 0x8000) != 0 ? 255 : 0); // A
                    break;

                case 3:
                    // 24-bit BGR
                    dest[offset + 0] = br.ReadByte(); // B
                    dest[offset + 1] = br.ReadByte(); // G
                    dest[offset + 2] = br.ReadByte(); // R
                    dest[offset + 3] = 255;          // A (opaque)
                    break;

                case 4:
                    // 32-bit BGRA
                    dest[offset + 0] = br.ReadByte(); // B
                    dest[offset + 1] = br.ReadByte(); // G
                    dest[offset + 2] = br.ReadByte(); // R
                    dest[offset + 3] = br.ReadByte(); // A
                    break;

                default:
                    dest[offset + 0] = 0;
                    dest[offset + 1] = 0;
                    dest[offset + 2] = 0;
                    dest[offset + 3] = 255;
                    break;
            }
        }

        /// <summary>
        /// Apply magenta transparency - RO convention where RGB(255,0,255) is transparent.
        /// This matches BrowEdit3's behavior in Texture.cpp.
        /// </summary>
        private static void ApplyMagentaTransparency(byte[] pixels)
        {
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i + 0];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];

                // Check for magenta (R > 247, G < 8, B > 247) - same threshold as BrowEdit3
                if (r > 247 && g < 8 && b > 247)
                {
                    pixels[i + 0] = 0;   // B
                    pixels[i + 1] = 0;   // G
                    pixels[i + 2] = 0;   // R
                    pixels[i + 3] = 0;   // A (transparent)
                }
            }
        }

        /// <summary>
        /// Flip image vertically (TGA default origin is bottom-left).
        /// </summary>
        private static void FlipVertical(byte[] pixels, int width, int height)
        {
            int rowBytes = width * 4;
            byte[] tempRow = new byte[rowBytes];

            for (int y = 0; y < height / 2; y++)
            {
                int topOffset = y * rowBytes;
                int bottomOffset = (height - 1 - y) * rowBytes;

                // Swap rows
                Array.Copy(pixels, topOffset, tempRow, 0, rowBytes);
                Array.Copy(pixels, bottomOffset, pixels, topOffset, rowBytes);
                Array.Copy(tempRow, 0, pixels, bottomOffset, rowBytes);
            }
        }
    }
}
