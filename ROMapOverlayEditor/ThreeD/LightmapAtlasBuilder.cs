using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ROMapOverlayEditor.ThreeD
{
    /// <summary>
    /// Builds BrowEdit3-style lightmap atlas from GND lightmap raw data.
    /// Format: first half = alpha (shadow), second half = RGB (color lighting).
    /// Use with GndReaderV2 when LoadLightmaps=true; WPF cannot sample this as a second
    /// texture without custom shader (SharpDX). This builds the atlas for future use.
    /// </summary>
    public static class LightmapAtlasBuilder
    {
        /// <summary>
        /// Build RGBA texture from GND lightmap data.
        /// Each lightmap: first (width*height) bytes = alpha; next (width*height*3) bytes = RGB.
        /// </summary>
        /// <param name="lightmapCount">Number of lightmaps</param>
        /// <param name="lightmapWidth">Width of each lightmap (e.g. 8)</param>
        /// <param name="lightmapHeight">Height of each lightmap (e.g. 8)</param>
        /// <param name="rawData">Raw bytes: lightmapCount * (width*height*4) or GndReaderV2 format (256 per 8x8)</param>
        /// <returns>RGBA bitmap (atlas) or null if data invalid</returns>
        public static WriteableBitmap? BuildAtlas(int lightmapCount, int lightmapWidth, int lightmapHeight, byte[]? rawData)
        {
            if (rawData == null || lightmapCount <= 0 || lightmapWidth <= 0 || lightmapHeight <= 0)
                return null;

            int pixelsPerLm = lightmapWidth * lightmapHeight;
            // GndReaderV2: 256 bytes per 8x8 = 64 alpha + 192 RGB
            int bytesPerLm = pixelsPerLm + pixelsPerLm * 3;
            if (rawData.Length < lightmapCount * bytesPerLm)
                return null;

            // Shelf-pack lightmaps into atlas
            int atlasWidth = Math.Max(lightmapWidth * 2, NextPow2(lightmapWidth * (int)Math.Ceiling(Math.Sqrt(lightmapCount))));
            int cols = atlasWidth / lightmapWidth;
            int rows = (lightmapCount + cols - 1) / cols;
            int atlasW = cols * lightmapWidth;
            int atlasH = rows * lightmapHeight;

            var bmp = new WriteableBitmap(atlasW, atlasH, 96, 96, PixelFormats.Bgra32, null);
            var buf = new byte[atlasW * atlasH * 4];

            for (int i = 0; i < lightmapCount; i++)
            {
                int col = i % cols;
                int row = i / cols;
                int dstX = col * lightmapWidth;
                int dstY = row * lightmapHeight;
                int srcOff = i * bytesPerLm;

                for (int yy = 0; yy < lightmapHeight; yy++)
                {
                    for (int xx = 0; xx < lightmapWidth; xx++)
                    {
                        int srcPixel = xx + lightmapWidth * yy;
                        byte a = rawData[srcOff + srcPixel];
                        byte r = rawData[srcOff + pixelsPerLm + srcPixel * 3 + 0];
                        byte g = rawData[srcOff + pixelsPerLm + srcPixel * 3 + 1];
                        byte b = rawData[srcOff + pixelsPerLm + srcPixel * 3 + 2];

                        int dstPixel = (dstX + xx) + atlasW * (dstY + yy);
                        buf[dstPixel * 4 + 0] = b;
                        buf[dstPixel * 4 + 1] = g;
                        buf[dstPixel * 4 + 2] = r;
                        buf[dstPixel * 4 + 3] = a;
                    }
                }
            }

            bmp.WritePixels(new System.Windows.Int32Rect(0, 0, atlasW, atlasH), buf, atlasW * 4, 0);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>
        /// Build from Gnd.GndLightmapInfo (e.g. from GndReaderV2). Entry size assumed 256 for 8x8.
        /// </summary>
        public static WriteableBitmap? BuildAtlasFromGndLightmapInfo(int count, int cellWidth, int cellHeight, byte[]? rawData)
        {
            if (rawData == null || count <= 0) return null;
            // GndReaderV2 uses LIGHTMAP_ENTRY_SIZE = 256 for 8x8
            int pixelsPerLm = cellWidth * cellHeight;
            int expectedSize = count * (pixelsPerLm + pixelsPerLm * 3);
            if (rawData.Length < count * 256)
                return null;
            // Support both 256-byte (8x8) and full RGBA-style layout
            int bytesPerLm = (rawData.Length / count >= pixelsPerLm * 4) ? pixelsPerLm * 4 : 256;
            if (bytesPerLm == 256 && pixelsPerLm == 64)
                return BuildAtlas(count, cellWidth, cellHeight, rawData);
            if (rawData.Length >= count * pixelsPerLm * 4)
            {
                // Already RGBA
                int atlasW = NextPow2(cellWidth * (int)Math.Ceiling(Math.Sqrt(count)));
                int cols = atlasW / cellWidth;
                int rows = (count + cols - 1) / cols;
                int atlasWidth = cols * cellWidth;
                int atlasHeight = rows * cellHeight;
                var bmp = new WriteableBitmap(atlasWidth, atlasHeight, 96, 96, PixelFormats.Bgra32, null);
                var buf = new byte[atlasWidth * atlasHeight * 4];
                for (int i = 0; i < count; i++)
                {
                    int col = i % cols;
                    int row = i / cols;
                    for (int yy = 0; yy < cellHeight; yy++)
                        for (int xx = 0; xx < cellWidth; xx++)
                        {
                            int src = (i * pixelsPerLm + xx + cellWidth * yy) * 4;
                            int dst = ((col * cellWidth + xx) + atlasWidth * (row * cellHeight + yy)) * 4;
                            buf[dst + 0] = rawData[src + 2];
                            buf[dst + 1] = rawData[src + 1];
                            buf[dst + 2] = rawData[src + 0];
                            buf[dst + 3] = rawData[src + 3];
                        }
                }
                bmp.WritePixels(new System.Windows.Int32Rect(0, 0, atlasWidth, atlasHeight), buf, atlasWidth * 4, 0);
                bmp.Freeze();
                return bmp;
            }
            return BuildAtlas(count, cellWidth, cellHeight, rawData);
        }

        private static int NextPow2(int v)
        {
            int p = 1;
            while (p < v) p <<= 1;
            return p;
        }
    }
}
