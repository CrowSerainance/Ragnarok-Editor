using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace ROMapOverlayEditor.Imaging
{
    // Minimal TGA loader for RO terrain (24/32-bit, uncompressed or RLE).
    // Outputs BGRA32 BitmapSource (Frozen-ready).
    public static class TgaDecoder
    {
        public static BitmapSource Decode(byte[] tgaBytes)
        {
            using var ms = new MemoryStream(tgaBytes);
            using var br = new BinaryReader(ms);

            byte idLength = br.ReadByte();
            byte colorMapType = br.ReadByte();
            byte imageType = br.ReadByte(); // 2=uncompressed truecolor, 10=RLE truecolor

            ushort cmFirst = br.ReadUInt16();
            ushort cmLength = br.ReadUInt16();
            byte cmDepth = br.ReadByte();

            ushort xOrigin = br.ReadUInt16();
            ushort yOrigin = br.ReadUInt16();
            ushort width = br.ReadUInt16();
            ushort height = br.ReadUInt16();
            byte bpp = br.ReadByte(); // 24 or 32
            byte imageDesc = br.ReadByte();

            if (colorMapType != 0)
                throw new NotSupportedException("Color-mapped TGA not supported (RO terrain is usually truecolor).");

            bool rle = imageType == 10;
            bool raw = imageType == 2;
            if (!rle && !raw)
                throw new NotSupportedException($"Unsupported TGA type: {imageType}");

            int bytesPerPixel = bpp / 8;
            if (bytesPerPixel != 3 && bytesPerPixel != 4)
                throw new NotSupportedException($"Unsupported TGA bpp: {bpp}");

            if (idLength > 0)
                br.ReadBytes(idLength); // skip image ID

            // origin bit (bit 5): 0 = bottom-left, 1 = top-left
            bool originTop = (imageDesc & 0x20) != 0;

            int pixelCount = width * height;
            byte[] bgra = new byte[pixelCount * 4];

            if (raw)
            {
                ReadRaw(br, bgra, width, height, bytesPerPixel, originTop);
            }
            else
            {
                ReadRle(br, bgra, width, height, bytesPerPixel, originTop);
            }

            var bmp = BitmapSource.Create(
                width, height, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32,
                null, bgra, width * 4);

            bmp.Freeze();
            return bmp;
        }

        private static void ReadRaw(BinaryReader br, byte[] bgra, int w, int h, int bpp, bool originTop)
        {
            for (int y = 0; y < h; y++)
            {
                int dstY = originTop ? y : (h - 1 - y);
                for (int x = 0; x < w; x++)
                {
                    byte b = br.ReadByte();
                    byte g = br.ReadByte();
                    byte r = br.ReadByte();
                    byte a = (bpp == 4) ? br.ReadByte() : (byte)255;

                    int idx = (dstY * w + x) * 4;
                    bgra[idx + 0] = b;
                    bgra[idx + 1] = g;
                    bgra[idx + 2] = r;
                    bgra[idx + 3] = a;
                }
            }
        }

        private static void ReadRle(BinaryReader br, byte[] bgra, int w, int h, int bpp, bool originTop)
        {
            int x = 0, y = 0;
            while (y < h)
            {
                byte header = br.ReadByte();
                int count = (header & 0x7F) + 1;

                if ((header & 0x80) != 0)
                {
                    // RLE packet: one pixel repeated
                    byte b = br.ReadByte();
                    byte g = br.ReadByte();
                    byte r = br.ReadByte();
                    byte a = (bpp == 4) ? br.ReadByte() : (byte)255;

                    for (int i = 0; i < count; i++)
                        WritePixel(bgra, w, h, ref x, ref y, b, g, r, a, originTop);
                }
                else
                {
                    // RAW packet: count pixels follow
                    for (int i = 0; i < count; i++)
                    {
                        byte b = br.ReadByte();
                        byte g = br.ReadByte();
                        byte r = br.ReadByte();
                        byte a = (bpp == 4) ? br.ReadByte() : (byte)255;

                        WritePixel(bgra, w, h, ref x, ref y, b, g, r, a, originTop);
                    }
                }
            }
        }

        private static void WritePixel(byte[] bgra, int w, int h, ref int x, ref int y, byte b, byte g, byte r, byte a, bool originTop)
        {
            int dstY = originTop ? y : (h - 1 - y);
            int idx = (dstY * w + x) * 4;
            bgra[idx + 0] = b;
            bgra[idx + 1] = g;
            bgra[idx + 2] = r;
            bgra[idx + 3] = a;

            x++;
            if (x >= w)
            {
                x = 0;
                y++;
            }
        }
    }
}
