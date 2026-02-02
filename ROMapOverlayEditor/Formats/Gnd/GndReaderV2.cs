using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ROMapOverlayEditor.Gnd
{
    public static class GndReaderV2
    {
        private static readonly Encoding KoreanEncoding;

        static GndReaderV2()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            try { KoreanEncoding = Encoding.GetEncoding(949); }
            catch { KoreanEncoding = Encoding.UTF8; }
        }

        /// <summary>
        /// Check if the data represents a valid GND file.
        /// </summary>
        public static bool IsGndFile(byte[] data)
        {
            if (data == null || data.Length < 4) return false;
            return data[0] == 'G' && data[1] == 'R' && data[2] == 'G' && data[3] == 'N';
        }

        public static GndFileV2 Read(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);

            if (data.Length < 6) throw new InvalidDataException("GND file too small.");

            // 1. Header & Version
            var magic = Encoding.ASCII.GetString(br.ReadBytes(4));
            if (magic != "GRGN") throw new InvalidDataException("Not a GND file (missing GRGN).");
            
            ushort version = br.ReadUInt16(); 
            int width = 0, height = 0, textureCount = 0;
            float tileScale = 10f;

            if (version > 0) {
                width = br.ReadInt32();
                height = br.ReadInt32();
                tileScale = br.ReadSingle();
                textureCount = br.ReadInt32();
                br.ReadInt32(); // maxTexName (80)
            } else {
                textureCount = br.ReadInt32();
                width = br.ReadInt32();
                height = br.ReadInt32();
            }

            var gnd = new GndFileV2 { 
                Version = version, Width = width, Height = height, 
                TileScale = tileScale, Cubes = new GndCubeV2_Legacy[width, height] 
            };

            // 2. Textures - BrowEdit3 reads 40 bytes file + 40 bytes name
            for (int i = 0; i < textureCount; i++) {
                if (ms.Position + 80 > ms.Length) break;
                string file = ReadFixedString(br, 40);
                string name = ReadFixedString(br, 40);
                gnd.Textures.Add(new GndTextureV2 { Filename = file, Name = name });
            }

            if (version <= 0) return gnd;

            // 3. Lightmaps - Exact 256 bytes per 8x8 entry
            if (ms.Position + 16 > ms.Length) return gnd;
            int lmCount = br.ReadInt32();
            int lmWidth = br.ReadInt32();
            int lmHeight = br.ReadInt32();
            int gridSize = br.ReadInt32();
            
            int perLmSize = lmWidth * lmHeight * 4;
            long totalLmSize = (long)lmCount * perLmSize;
            
            if (ms.Position + totalLmSize <= ms.Length) {
                gnd.Lightmaps = new GndLightmapInfo { 
                    Count = lmCount, CellWidth = lmWidth, CellHeight = lmHeight, 
                    GridSizeCell = gridSize, RawData = br.ReadBytes((int)totalLmSize) 
                };
            } else {
                // Return partial data on truncation instead of throwing
                ms.Seek(0, SeekOrigin.End); 
            }

            // 4. Tiles (Surfaces)
            if (ms.Position + 4 > ms.Length) return gnd;
            int tileCount = br.ReadInt32();
            for (int i = 0; i < tileCount; i++) {
                if (ms.Position + 40 > ms.Length) break;
                float u1 = br.ReadSingle(), u2 = br.ReadSingle(), u3 = br.ReadSingle(), u4 = br.ReadSingle();
                float v1 = br.ReadSingle(), v2 = br.ReadSingle(), v3 = br.ReadSingle(), v4 = br.ReadSingle();
                short texIdx = br.ReadInt16();
                ushort lmIdx = br.ReadUInt16();
                byte b = br.ReadByte(), g = br.ReadByte(), r = br.ReadByte(), a = br.ReadByte();
                gnd.Surfaces.Add(new GndSurfaceTile(u1, u2, u3, u4, v1, v2, v3, v4, texIdx, lmIdx, b, g, r, a));
            }

            // 5. Cubes - Version-dependent record size
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    int recordSize = 16 + (version >= 0x0106 ? 12 : 8);
                    if (ms.Position + recordSize > ms.Length) {
                        gnd.Cubes[x, y] = new GndCubeV2_Legacy(0, 0, 0, 0, -1, -1, -1);
                        continue;
                    }
                    float h1 = br.ReadSingle(), h2 = br.ReadSingle(), h3 = br.ReadSingle(), h4 = br.ReadSingle();
                    int up, side, front;
                    if (version >= 0x0106) {
                        up = br.ReadInt32(); front = br.ReadInt32(); side = br.ReadInt32();
                    } else {
                        up = br.ReadInt16(); front = br.ReadInt16(); side = br.ReadInt16();
                        br.ReadInt16(); // Skip unknown short
                    }
                    gnd.Cubes[x, y] = new GndCubeV2_Legacy(h1, h2, h3, h4, up, side, front);
                }
            }
            return gnd;
        }

        private static string ReadFixedString(BinaryReader br, int length) {
            byte[] buffer = br.ReadBytes(length);
            int zeroIdx = Array.IndexOf(buffer, (byte)0);
            int actualLen = zeroIdx == -1 ? length : zeroIdx;
            return KoreanEncoding.GetString(buffer, 0, actualLen).Trim();
        }
    }
}