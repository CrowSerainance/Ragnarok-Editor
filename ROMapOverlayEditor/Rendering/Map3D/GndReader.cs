// ============================================================================
// GndReader.cs - DEPRECATED - Wrapper for GndReaderV2
// ============================================================================
// THIS FILE IS DEPRECATED. Use ROMapOverlayEditor.Gnd.GndReaderV2 instead.
// This wrapper exists only for backward compatibility.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ROMapOverlayEditor.Map3D
{
    // ========================================================================
    // GND FILE DATA STRUCTURES (Legacy - for backward compatibility)
    // ========================================================================
    
    /// <summary>
    /// DEPRECATED: Use ROMapOverlayEditor.Gnd.GndFileV2 instead.
    /// Legacy GND file structure maintained for backward compatibility.
    /// </summary>
    [Obsolete("Use ROMapOverlayEditor.Gnd.GndFileV2 instead")]
    public sealed class GndFile
    {
        public string Signature = "";
        public ushort Version;
        public int Width;
        public int Height;
        public float TileScale = 10f;
        public List<string> Textures = new();
        public Surface[] Surfaces = Array.Empty<Surface>();
        public Cube[] Cubes = Array.Empty<Cube>();
        
        public int LightmapCount;
        public int LightmapWidth = 8;
        public int LightmapHeight = 8;
    }

    /// <summary>
    /// DEPRECATED: Surface definition (tile face) with UVs, texture, and color.
    /// </summary>
    [Obsolete("Use ROMapOverlayEditor.Gnd.GndSurfaceTile instead")]
    public readonly struct Surface
    {
        public readonly int TextureId;
        public readonly int LightmapIndex;
        public readonly float U1, V1, U2, V2, U3, V3, U4, V4;
        public readonly byte ColorR, ColorG, ColorB, ColorA;
        
        public Surface(int tex, int lightmap, 
                       float u1, float v1, float u2, float v2, 
                       float u3, float v3, float u4, float v4,
                       byte r, byte g, byte b, byte a)
        {
            TextureId = tex;
            LightmapIndex = lightmap;
            U1 = u1; V1 = v1;
            U2 = u2; V2 = v2;
            U3 = u3; V3 = v3;
            U4 = u4; V4 = v4;
            ColorR = r; ColorG = g; ColorB = b; ColorA = a;
        }
    }

    /// <summary>
    /// DEPRECATED: Ground cube (cell) with 4 corner heights and surface indices.
    /// </summary>
    [Obsolete("Use ROMapOverlayEditor.Gnd.GndCubeV2_Legacy instead")]
    public readonly struct Cube
    {
        public readonly float H1, H2, H3, H4;
        public readonly int TopSurfaceIndex;
        public readonly int FrontSurfaceIndex;
        public readonly int SideSurfaceIndex;
        
        public Cube(float h1, float h2, float h3, float h4, int top, int front, int side)
        {
            H1 = h1; H2 = h2; H3 = h3; H4 = h4;
            TopSurfaceIndex = top;
            FrontSurfaceIndex = front;
            SideSurfaceIndex = side;
        }
    }

    // ========================================================================
    // GND READER - DEPRECATED WRAPPER
    // ========================================================================
    
    /// <summary>
    /// DEPRECATED: Use ROMapOverlayEditor.Gnd.GndReaderV2 instead.
    /// This class wraps GndReaderV2 for backward compatibility.
    /// </summary>
    [Obsolete("Use ROMapOverlayEditor.Gnd.GndReaderV2 instead")]
    public static class GndReader
    {
        /// <summary>
        /// DEPRECATED: Read a GND file from a stream.
        /// Use GndReaderV2.Read() instead.
        /// </summary>
        public static GndFile Read(Stream s)
        {
            // Read entire stream to byte array
            byte[] data;
            
            if (s is MemoryStream ms)
            {
                // Use ToArray() for safety - handles position and length correctly
                data = ms.ToArray();
            }
            else
            {
                using var temp = new MemoryStream();
                s.CopyTo(temp);
                data = temp.ToArray();
            }
            
            if (data == null || data.Length == 0)
            {
                throw new InvalidDataException("GND stream is empty or null");
            }
            
            return Read(data);
        }
        
        /// <summary>
        /// DEPRECATED: Read a GND file from byte array.
        /// Use GndReaderV2.Read() instead.
        /// </summary>
        public static GndFile Read(byte[] data)
        {
            try
            {
                // Use GndReaderV2 and convert to legacy format
                var gndV2 = ROMapOverlayEditor.Gnd.GndReaderV2.Read(data, 
                    ROMapOverlayEditor.Gnd.GndReadOptions.Default);
                
                return ConvertFromV2(gndV2);
            }
            catch (Exception ex)
            {
                // Re-throw with more context for debugging
                throw new InvalidDataException($"GndReader (via GndReaderV2) failed: {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// Convert GndFileV2 to legacy GndFile format.
        /// </summary>
        private static GndFile ConvertFromV2(ROMapOverlayEditor.Gnd.GndFileV2 v2)
        {
            var legacy = new GndFile
            {
                Signature = "GRGN",
                Version = v2.Version,
                Width = v2.Width,
                Height = v2.Height,
                TileScale = v2.TileScale,
                LightmapCount = v2.Lightmaps.Count,
                LightmapWidth = v2.Lightmaps.CellWidth,
                LightmapHeight = v2.Lightmaps.CellHeight
            };
            
            // Convert textures
            legacy.Textures = v2.Textures.Select(t => t.Filename).ToList();
            
            // Convert surfaces
            legacy.Surfaces = new Surface[v2.Surfaces.Count];
            for (int i = 0; i < v2.Surfaces.Count; i++)
            {
                var s = v2.Surfaces[i];
                legacy.Surfaces[i] = new Surface(
                    s.TextureIndex, s.LightmapIndex,
                    s.U1, s.V1, s.U2, s.V2, s.U3, s.V3, s.U4, s.V4,
                    s.R, s.G, s.B, s.A
                );
            }
            
            // Convert cubes
            int cubeCount = v2.Width * v2.Height;
            legacy.Cubes = new Cube[cubeCount];
            for (int y = 0; y < v2.Height; y++)
            {
                for (int x = 0; x < v2.Width; x++)
                {
                    int idx = y * v2.Width + x;
                    var c = v2.Cubes[x, y];
                    legacy.Cubes[idx] = new Cube(
                        c.Height00, c.Height10, c.Height01, c.Height11,
                        c.TileUp, c.TileFront, c.TileSide
                    );
                }
            }
            
            return legacy;
        }
    }
}
