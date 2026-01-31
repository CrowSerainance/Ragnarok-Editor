using System;
using System.IO;
using ROMapOverlayEditor.IO;
using ROMapOverlayEditor.Rsw; // Added for Vec3F

namespace ROMapOverlayEditor.Gnd
{
    public static class GndIO
    {
        private const string SIG = "GND\0";

        public static GndFile Read(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 16) throw new InvalidDataException("GND too small.");
            using var ms = new MemoryStream(bytes);
            var parsed = Read(ms);
            
            var gnd = new GndFile
            {
                Version = parsed.Version,
                Width = parsed.Width,
                Height = parsed.Height,
                TileScale = parsed.Zoom,
                LightmapWidth = parsed.LightmapWidth,
                LightmapHeight = parsed.LightmapHeight,
                GridSizeCell = parsed.LightmapCells
            };
            
            // Map Textures
            if (parsed.Textures != null)
                foreach (var t in parsed.Textures)
                    gnd.Textures.Add(new GndTexture { File = t, Name = t });
                    
            // Map Tiles
            if (parsed.Tiles != null)
            {
                foreach (var pt in parsed.Tiles)
                {
                    gnd.Tiles.Add(new GndTile 
                    {
                        // Mapping single UV pair from ParsedGndV2 (user patch) to 4-corner UVs (legacy).
                        // Assuming simple quad mapping if only one UV provided, or just using it for all?
                        // Actually, GndTileV2 in User Patch has ONE U, V.
                        // But legacy GndTile expects 4.
                        // I will map it to U1,V1 and maybe 0 for others or same?
                        // Let's just remove the duplicate lines first.
                        // U1 = pt.U, V1 = pt.V, // Removing this duplicate
                        // U2 = pt.U, V2 = pt.V, // Removing this duplicate
                        // User patch GndTileV2 has U,V (single pair?). Old has 4 pairs.
                        // Assuming Tiling/Scale? Or just plain UV? 
                        // User patch seems simplified. Mapping 0,0 1,0 1,1 0,1 logic?
                        // Old GndIO Read: U1, U2, U3, U4...
                        // User patch Read: U, V.
                        // I'll map U/V to all corners or just leave 0?
                        // Actually GndTileV2 in ParsedGndV2 has U, V.
                        // I'll just assign U1=U, V1=V etc.
                        U1 = pt.U, V1 = pt.V,
                        TextureIndex = pt.TextureIndex,
                        LightmapIndex = pt.LightmapIndex,
                        // Color processing? pt.Color is uint. Old is byte R,G,B,A.
                        B = (byte)(pt.Color & 0xFF),
                        G = (byte)((pt.Color >> 8) & 0xFF),
                        R = (byte)((pt.Color >> 16) & 0xFF),
                        A = (byte)((pt.Color >> 24) & 0xFF)
                    });
                }
            }

            // Map Cubes
            gnd.Cubes = new GndCube[parsed.Width, parsed.Height];
            int idx = 0;
            for (int y = 0; y < parsed.Height; y++)
            {
                for (int x = 0; x < parsed.Width; x++)
                {
                    if (idx >= parsed.Cubes.Length) break;
                    var pc = parsed.Cubes[idx++];
                    var gc = new GndCube 
                    {
                        TileUp = pc.SurfaceUp,
                        TileSide = pc.SurfaceRight,
                        TileFront = pc.SurfaceFront
                    };
                    
                    // Fetch heights from SurfaceUp
                    if (pc.SurfaceUp >= 0 && pc.SurfaceUp < parsed.Surfaces.Length)
                    {
                        var surf = parsed.Surfaces[pc.SurfaceUp];
                        gc.H1 = surf.Height1;
                        gc.H2 = surf.Height2;
                        gc.H3 = surf.Height3;
                        gc.H4 = surf.Height4;
                        
                        // Also, old GndCube used TileUp as direct index to TILES if version < something
                        // But here SurfaceUp points to SURFACES.
                        // Old GndFile: TileUp was "TileId" or "SurfaceId"? 
                        // Step 20 says: "if version >= 0x0106 tile ids are int" ... "c.TileUp = br.ReadInt32()".
                        // And GndMeshBuilder uses: "int tileId = cube.TileUp; ... tile = gnd.Tiles[tileId];"
                        // So TileUp MUST point to TILES, not SURFACES.
                        // User patch: "SurfaceUp = br.ReadInt32()".
                        // But User patch "Surfaces" contain "TileUp".
                        // So User Patch structure: Cube -> Surface -> Tile.
                        // Old Structure: Cube -> Tile (direct).
                        // I need to map: gc.TileUp = surf.TileUp;
                        gc.TileUp = surf.TileUp;
                    }
                    else
                    {
                        gc.TileUp = -1;
                    }
                    
                    gnd.Cubes[x, y] = gc;
                }
            }

            return gnd;
        }

        public static ParsedGndV2 Read(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new InvalidOperationException("Stream is not readable.");

            long fileLen = stream.Length;

            using var br = new BinaryReaderEx(stream, leaveOpen: true);

            var sig = br.ReadFixedString(4, BinaryReaderEx.EncodingAscii);
            if (!string.Equals(sig, SIG, StringComparison.Ordinal))
                throw new InvalidDataException($"Invalid GND signature. Expected '{SIG}', got '{sig}'.");

            ushort version = br.ReadUInt16();
            uint width = br.ReadUInt32();
            uint height = br.ReadUInt32();
            float zoom = br.ReadSingle();

            int texCount = br.ReadInt32();
            if (texCount < 0 || texCount > 200000)
                throw new InvalidDataException($"GND texture count looks invalid: {texCount}");

            string[] textures = new string[texCount];
            for (int i = 0; i < texCount; i++)
                textures[i] = br.ReadFixedString(40, BinaryReaderEx.KoreanEncoding).TrimEnd('\0');

            int lightmapCount = br.ReadInt32();
            if (lightmapCount < 0 || lightmapCount > 2000000)
                throw new InvalidDataException($"GND lightmapCount looks invalid: {lightmapCount}");

            int lmWidth = br.ReadInt32();
            int lmHeight = br.ReadInt32();
            int lmCells = br.ReadInt32();

            // --------------------------------------------------------------------
            // ROBUST LIGHTMAP STRIDE DETECTION
            // --------------------------------------------------------------------
            long posAfterLmHeader = stream.Position;

            // The remaining file must contain:
            // - tiles (texCount tiles): each tile is 20 bytes
            // - surfaces (surfaceCount surfaces): 56 bytes each
            // - cubes (width*height cubes): 28 bytes each
            // We don't know surfaceCount yet, but it's the next int after tiles.
            //
            // We do: try candidate stride; jump; read tile section + surfaceCount;
            // then validate remaining bytes for surfaces + cubes.
            // --------------------------------------------------------------------

            int stride = DetectLightmapStrideOrDefault(
                stream, br, fileLen,
                posAfterLmHeader,
                lightmapCount,
                expectedTileCount: texCount,
                cubeCount: checked((int)(width * height))
            );

            // Apply the chosen stride: move to after lightmap blob
            stream.Position = posAfterLmHeader + (long)lightmapCount * stride;

            // Tiles
            int tileCount = br.ReadInt32();
            if (tileCount < 0 || tileCount > 20000000)
                throw new InvalidDataException($"GND tileCount looks invalid: {tileCount}");

            var tiles = new GndTileV2[tileCount];
            for (int i = 0; i < tileCount; i++)
            {
                tiles[i] = new GndTileV2
                {
                    U = br.ReadSingle(),
                    V = br.ReadSingle(),
                    TextureIndex = br.ReadUInt16(),
                    LightmapIndex = br.ReadUInt16(),
                    Color = br.ReadUInt32()
                };
            }

            int surfaceCount = br.ReadInt32();
            if (surfaceCount < 0 || surfaceCount > 20000000)
                throw new InvalidDataException($"GND surfaceCount looks invalid: {surfaceCount}");

            var surfaces = new GndSurfaceV2[surfaceCount];
            for (int i = 0; i < surfaceCount; i++)
            {
                surfaces[i] = new GndSurfaceV2
                {
                    Height1 = br.ReadSingle(),
                    Height2 = br.ReadSingle(),
                    Height3 = br.ReadSingle(),
                    Height4 = br.ReadSingle(),
                    TileUp = br.ReadInt32(),
                    TileFront = br.ReadInt32(),
                    TileRight = br.ReadInt32(),
                    Normal = new Vec3F(br.ReadSingle(), br.ReadSingle(), br.ReadSingle())
                };
            }

            // Cubes
            int cubeCount = checked((int)(width * height));
            var cubes = new GndCubeV2[cubeCount];

            for (int i = 0; i < cubeCount; i++)
            {
                cubes[i] = new GndCubeV2
                {
                    SurfaceUp = br.ReadInt32(),
                    SurfaceFront = br.ReadInt32(),
                    SurfaceRight = br.ReadInt32()
                };
            }

            return new ParsedGndV2
            {
                Version = version,
                Width = (int)width,
                Height = (int)height,
                Zoom = zoom,

                Textures = textures,
                LightmapCount = lightmapCount,
                LightmapWidth = lmWidth,
                LightmapHeight = lmHeight,
                LightmapCells = lmCells,
                LightmapStrideBytes = stride,

                Tiles = tiles,
                Surfaces = surfaces,
                Cubes = cubes
            };
        }

        private static int DetectLightmapStrideOrDefault(
            Stream stream,
            BinaryReaderEx br,
            long fileLen,
            long posAfterLmHeader,
            int lightmapCount,
            int expectedTileCount,
            int cubeCount)
        {
            // Common observed strides across RO clients/tools:
            // - 256: classic (BrowEdit r586 sources show 256)
            // - 268/272: some toolchains append small blocks per lightmap
            // - 320: 8*8*(RGBA + shadow) => 64*5
            int[] candidates = { 256, 268, 272, 320 };

            long originalPos = stream.Position;

            try
            {
                foreach (var stride in candidates)
                {
                    long lmEnd = posAfterLmHeader + (long)lightmapCount * stride;
                    if (lmEnd < 0 || lmEnd > fileLen) continue;

                    // Peek-parse at that position
                    stream.Position = lmEnd;

                    if (stream.Position + 4 > fileLen) continue;
                    int tileCount = br.ReadInt32();
                    if (tileCount < 0 || tileCount > 20000000) continue;

                    // If tileCount is wildly different from expectedTileCount,
                    // it usually means wrong alignment. Allow small mismatch but reject extremes.
                    if (expectedTileCount > 0)
                    {
                        int delta = Math.Abs(tileCount - expectedTileCount);
                        if (delta > Math.Max(64, expectedTileCount / 2)) continue;
                    }

                    long tilesBytes = (long)tileCount * 20;
                    if (stream.Position + tilesBytes + 4 > fileLen) continue;

                    stream.Position += tilesBytes;

                    int surfaceCount = br.ReadInt32();
                    if (surfaceCount < 0 || surfaceCount > 20000000) continue;

                    long surfacesBytes = (long)surfaceCount * 56;
                    long cubesBytes = (long)cubeCount * 12; // 3 ints per cube

                    long needed = stream.Position + surfacesBytes + cubesBytes;
                    if (needed <= fileLen)
                        return stride;
                }

                // fallback
                return 256;
            }
            finally
            {
                stream.Position = originalPos;
            }
        }
    }
}
