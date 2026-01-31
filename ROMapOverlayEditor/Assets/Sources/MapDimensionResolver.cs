// ═══════════════════════════════════════════════════════════════════════════════
// FILE: MapDimensionResolver.cs
// PURPOSE: Extract map tile dimensions from GAT files in GRF archives
// ═══════════════════════════════════════════════════════════════════════════════
//
// GAT FILE FORMAT:
// The Ground Altitude Table (.gat) contains walkability data for RO maps.
// The file header contains the exact map dimensions in tiles.
//
// Header (14 bytes minimum):
//   [0-3]   char[4]  Magic "GRAT"
//   [4]     byte     Version major
//   [5]     byte     Version minor
//   [6-9]   int32    Width in tiles (little-endian)  ← CORRECTED OFFSET
//   [10-13] int32    Height in tiles (little-endian) ← CORRECTED OFFSET
//
// After header: Cell data (width * height cells, each cell is 20 bytes)
// ═══════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ROMapOverlayEditor.Sources;

namespace ROMapOverlayEditor.Sources
{
    /// <summary>
    /// Result of attempting to resolve map dimensions.
    /// </summary>
    public sealed class MapDimensionResult
    {
        public bool Success { get; }
        public int Width { get; }
        public int Height { get; }
        public string Source { get; }
        public string Error { get; }

        private MapDimensionResult(bool success, int width, int height, string source, string error)
        {
            Success = success;
            Width = width;
            Height = height;
            Source = source;
            Error = error;
        }

        public static MapDimensionResult Ok(int width, int height, string source)
            => new(true, width, height, source, "");

        public static MapDimensionResult Fail(string error)
            => new(false, 0, 0, "", error);
    }

    /// <summary>
    /// Extracts map dimensions from GAT files within GRF archives.
    /// </summary>
    public static class MapDimensionResolver
    {
        // GAT file magic bytes: "GRAT"
        private static readonly byte[] GatMagic = { 0x47, 0x52, 0x41, 0x54 };

        /// <summary>
        /// Cache of resolved map dimensions to avoid repeated GRF reads.
        /// Key: mapname (lowercase), Value: (width, height)
        /// </summary>
        private static readonly Dictionary<string, (int Width, int Height)> _cache = 
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Get map dimensions from GAT file in GRF.
        /// Uses caching to avoid repeated file reads.
        /// </summary>
        /// <param name="mapName">Map name (e.g., "prontera", "mora")</param>
        /// <param name="grfSource">GRF file source to read from</param>
        /// <returns>Result containing dimensions or error</returns>
        public static MapDimensionResult GetDimensions(string mapName, GrfFileSource grfSource)
        {
            if (string.IsNullOrWhiteSpace(mapName))
                return MapDimensionResult.Fail("Map name is empty");

            if (grfSource == null)
                return MapDimensionResult.Fail("No GRF source available");

            // Normalize map name (remove extension if present)
            mapName = Path.GetFileNameWithoutExtension(mapName).ToLowerInvariant();

            // Check cache first
            if (_cache.TryGetValue(mapName, out var cached))
                return MapDimensionResult.Ok(cached.Width, cached.Height, "cache");

            // Try to read from GAT file
            var result = ReadGatDimensions(mapName, grfSource);
            
            // Cache successful results
            if (result.Success)
                _cache[mapName] = (result.Width, result.Height);

            return result;
        }

        /// <summary>
        /// Get dimensions with fallback to CompositeFileSource.
        /// </summary>
        public static MapDimensionResult GetDimensions(string mapName, CompositeFileSource vfs)
        {
            if (vfs?.Grf == null)
                return MapDimensionResult.Fail("No GRF available in VFS");

            return GetDimensions(mapName, vfs.Grf);
        }

        /// <summary>
        /// Get dimensions using the new CompositeVfs layer (supports formatting: zip, 7z, folder, grf).
        /// </summary>
        public static MapDimensionResult GetDimensions(string mapName, ROMapOverlayEditor.Vfs.CompositeVfs vfs)
        {
            if (string.IsNullOrWhiteSpace(mapName)) return MapDimensionResult.Fail("Map name is empty");
            if (vfs == null) return MapDimensionResult.Fail("VFS is null");

            mapName = Path.GetFileNameWithoutExtension(mapName).ToLowerInvariant();

            // Check cache
            if (_cache.TryGetValue(mapName, out var cached))
                return MapDimensionResult.Ok(cached.Width, cached.Height, "cache");

            // Candidates
            var gatPaths = new[]
            {
                $"data\\{mapName}.gat",
                $"data/{mapName}.gat",
                $"{mapName}.gat",
                $"maps\\{mapName}.gat",
                $"data\\maps\\{mapName}.gat"
            };

            foreach (var p in gatPaths)
            {
                if (vfs.TryReadAllBytes(p, out var bytes, out var err) && bytes != null)
                {
                    var res = ParseGatHeader(bytes, p);
                    if (res.Success)
                    {
                        _cache[mapName] = (res.Width, res.Height);
                        return res;
                    }
                }
            }

            return MapDimensionResult.Fail($"GAT not found/valid in VFS: {mapName}");
        }

        /// <summary>
        /// Try to read GAT dimensions from filesystem (extracted client data).
        /// </summary>
        public static MapDimensionResult GetDimensionsFromFilesystem(string mapName, string clientDataPath)
        {
            if (string.IsNullOrWhiteSpace(mapName) || string.IsNullOrWhiteSpace(clientDataPath))
                return MapDimensionResult.Fail("Invalid parameters");

            mapName = Path.GetFileNameWithoutExtension(mapName).ToLowerInvariant();

            // Check cache
            if (_cache.TryGetValue(mapName, out var cached))
                return MapDimensionResult.Ok(cached.Width, cached.Height, "cache");

            var gatPath = Path.Combine(clientDataPath, $"{mapName}.gat");

            if (!File.Exists(gatPath))
                return MapDimensionResult.Fail($"GAT file not found: {gatPath}");

            try
            {
                // Only need first 14 bytes for header
                byte[] bytes = new byte[14];
                using (var fs = File.OpenRead(gatPath))
                {
                    int read = fs.Read(bytes, 0, 14);
                    if (read < 14)
                        return MapDimensionResult.Fail($"GAT file too small: {gatPath}");
                }

                var res = ParseGatHeader(bytes, gatPath);
                if (res.Success)
                {
                    _cache[mapName] = (res.Width, res.Height);
                }
                return res;
            }
            catch (Exception ex)
            {
                return MapDimensionResult.Fail($"Failed to read GAT: {ex.Message}");
            }
        }

        /// <summary>
        /// Get dimensions with fallback to filesystem if VFS fails.
        /// </summary>
        public static MapDimensionResult GetDimensionsWithFallback(string mapName, ROMapOverlayEditor.Vfs.CompositeVfs vfs, string? clientDataPath)
        {
            // Try VFS first
            if (vfs != null)
            {
                var vfsResult = GetDimensions(mapName, vfs);
                if (vfsResult.Success)
                    return vfsResult;
            }

            // Fallback to filesystem
            if (!string.IsNullOrWhiteSpace(clientDataPath))
            {
                return GetDimensionsFromFilesystem(mapName, clientDataPath);
            }

            return MapDimensionResult.Fail($"GAT not found for {mapName} (tried VFS and filesystem)");
        }

        /// <summary>
        /// Read map dimensions directly from GAT file bytes.
        /// </summary>
        private static MapDimensionResult ReadGatDimensions(string mapName, GrfFileSource grfSource)
        {
            // Possible GAT file paths in GRF
            var gatPaths = new[]
            {
                $"data\\{mapName}.gat",
                $"data/{mapName}.gat",
                $"{mapName}.gat"
            };

            foreach (var gatPath in gatPaths)
            {
                if (!grfSource.Exists(gatPath))
                    continue;

                try
                {
                    byte[] bytes = grfSource.ReadAllBytes(gatPath);
                    return ParseGatHeader(bytes, gatPath);
                }
                catch (Exception ex)
                {
                    // Try next path
                    continue;
                }
            }

            return MapDimensionResult.Fail($"GAT file not found for map: {mapName}");
        }

        /// <summary>
        /// Parse GAT header to extract dimensions.
        /// 
        /// CORRECTED GAT HEADER FORMAT:
        /// Bytes 0-3:  "GRAT" magic
        /// Bytes 4-5:  Version (major.minor)
        /// Bytes 6-9:  Width (int32 little-endian)  ← OFFSET 6, NOT 8!
        /// Bytes 10-13: Height (int32 little-endian) ← OFFSET 10, NOT 12!
        /// </summary>
        private static MapDimensionResult ParseGatHeader(byte[] bytes, string sourcePath)
        {
            // Minimum header size is 14 bytes
            if (bytes == null || bytes.Length < 14)
                return MapDimensionResult.Fail("GAT file too small (< 14 bytes)");

            // Verify magic bytes "GRAT" (0x47 0x52 0x41 0x54)
            if (bytes[0] != 0x47 || bytes[1] != 0x52 ||
                bytes[2] != 0x41 || bytes[3] != 0x54)
            {
                return MapDimensionResult.Fail("Invalid GAT magic (expected GRAT)");
            }

            // CORRECTED: Read dimensions at offsets 6 and 10
            int width = BitConverter.ToInt32(bytes, 6);   // Offset 6
            int height = BitConverter.ToInt32(bytes, 10); // Offset 10

            // Sanity check dimensions
            if (width <= 0 || width > 2000 || height <= 0 || height > 2000)
            {
                return MapDimensionResult.Fail($"Invalid dimensions: {width}x{height}");
            }

            return MapDimensionResult.Ok(width, height, sourcePath);
        }

        /// <summary>
        /// Clear the dimension cache (useful when switching GRF files).
        /// </summary>
        public static void ClearCache()
        {
            _cache.Clear();
        }

        /// <summary>
        /// Pre-populate cache by scanning all GAT files in GRF.
        /// Useful for building a complete dimension lookup table.
        /// </summary>
        public static Dictionary<string, (int Width, int Height)> ScanAllMaps(GrfFileSource grfSource)
        {
            var results = new Dictionary<string, (int Width, int Height)>(StringComparer.OrdinalIgnoreCase);

            if (grfSource == null)
                return results;

            // Find all .gat files in the GRF
            foreach (var path in grfSource.EnumeratePaths())
            {
                if (!path.EndsWith(".gat", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Extract map name from path (e.g., "data\prontera.gat" → "prontera")
                var fileName = Path.GetFileNameWithoutExtension(path);
                
                try
                {
                    byte[] bytes = grfSource.ReadAllBytes(path);
                    var result = ParseGatHeader(bytes, path);
                    
                    if (result.Success)
                    {
                        results[fileName] = (result.Width, result.Height);
                        _cache[fileName] = (result.Width, result.Height);
                    }
                }
                catch
                {
                    // Skip invalid files
                }
            }

            return results;
        }
    }
}
