// ============================================================================
// MapLoaderV2.cs - Unified Map Loading System
// ============================================================================
// PURPOSE: Complete map loading with RSW/GND/GAT parsing and mesh generation
// INTEGRATION: Drop into ROMapOverlayEditor/MapAssets/ folder
// FEATURES:
//   - Async/parallel loading
//   - Memory-efficient caching
//   - Progress reporting
//   - Error recovery
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using ROMapOverlayEditor.Gnd;
using ROMapOverlayEditor.Rsw;
using ROMapOverlayEditor.ThreeD;

namespace ROMapOverlayEditor.MapAssets
{
    /// <summary>
    /// Represents a fully loaded RO map with all assets.
    /// </summary>
    public sealed class LoadedMapV2 : IDisposable
    {
        /// <summary>Map name (e.g., "prontera")</summary>
        public string Name { get; init; } = string.Empty;
        
        /// <summary>RSW file data (resources/objects)</summary>
        public RswFileV2? Rsw { get; init; }
        
        /// <summary>GND file data (terrain)</summary>
        public GndFileV2? Gnd { get; init; }
        
        /// <summary>GAT file data (walkability) - optional</summary>
        public GatFileV2? Gat { get; init; }
        
        /// <summary>Generated terrain mesh</summary>
        public TerrainMeshV2? TerrainMesh { get; init; }
        
        /// <summary>Map width in tiles</summary>
        public int Width => Gnd?.Width ?? 0;
        
        /// <summary>Map height in tiles</summary>
        public int Height => Gnd?.Height ?? 0;
        
        /// <summary>Tile scale factor</summary>
        public float TileScale => Gnd?.TileScale ?? 10f;
        
        /// <summary>Map bounding box minimum</summary>
        public Vector3 BoundsMin { get; init; }
        
        /// <summary>Map bounding box maximum</summary>
        public Vector3 BoundsMax { get; init; }
        
        /// <summary>Map center point</summary>
        public Vector3 Center => (BoundsMin + BoundsMax) * 0.5f;
        
        /// <summary>Loading duration</summary>
        public TimeSpan LoadTime { get; init; }
        
        /// <summary>Any warnings during loading</summary>
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
        
        private bool _disposed;
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            TerrainMesh?.Dispose();
        }
    }
    
    /// <summary>
    /// Options for map loading.
    /// </summary>
    public sealed class MapLoadOptions
    {
        /// <summary>Load GAT file if available</summary>
        public bool LoadGat { get; init; } = true;
        
        /// <summary>Generate terrain mesh</summary>
        public bool GenerateMesh { get; init; } = true;
        
        /// <summary>Terrain mesh options</summary>
        public TerrainMeshOptions MeshOptions { get; init; } = TerrainMeshOptions.Default;
        
        /// <summary>GND read options</summary>
        public GndReadOptions GndOptions { get; init; } = GndReadOptions.Default;
        
        /// <summary>Default options</summary>
        public static readonly MapLoadOptions Default = new();
        
        /// <summary>Preview options (fast loading)</summary>
        public static readonly MapLoadOptions Preview = new()
        {
            LoadGat = false,
            MeshOptions = TerrainMeshOptions.Preview,
            GndOptions = GndReadOptions.Preview
        };
    }
    
    /// <summary>
    /// Progress reporting for map loading.
    /// </summary>
    public sealed class MapLoadProgress
    {
        public string Stage { get; set; } = string.Empty;
        public float Progress { get; set; }
        public string? CurrentFile { get; set; }
        
        public static readonly MapLoadProgress Starting = new() { Stage = "Starting", Progress = 0 };
        public static readonly MapLoadProgress Complete = new() { Stage = "Complete", Progress = 1 };
    }
    

    
    /// <summary>
    /// Unified map loading system with caching and async support.
    /// </summary>
    public sealed class MapLoaderV2 : IDisposable
    {
        // ====================================================================
        // FIELDS
        // ====================================================================
        
        private readonly IMapFileResolver _resolver;
        private readonly ConcurrentDictionary<string, LoadedMapV2> _cache = new();
        private readonly SemaphoreSlim _loadLock = new(Environment.ProcessorCount);
        private bool _disposed;
        
        // ====================================================================
        // CONSTRUCTOR
        // ====================================================================
        
        public MapLoaderV2(IMapFileResolver resolver)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }
        
        // ====================================================================
        // PUBLIC API
        // ====================================================================
        
        /// <summary>
        /// Load a map by name with default options.
        /// </summary>
        /// <param name="mapName">Map name (e.g., "prontera")</param>
        /// <param name="progress">Optional progress reporter</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Loaded map data</returns>
        public Task<LoadedMapV2> LoadMapAsync(
            string mapName,
            IProgress<MapLoadProgress>? progress = null,
            CancellationToken ct = default)
            => LoadMapAsync(mapName, MapLoadOptions.Default, progress, ct);
        
        /// <summary>
        /// Load a map by name with custom options.
        /// </summary>
        public async Task<LoadedMapV2> LoadMapAsync(
            string mapName,
            MapLoadOptions options,
            IProgress<MapLoadProgress>? progress = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(mapName))
                throw new ArgumentException("Map name cannot be empty", nameof(mapName));
            
            // Check cache first
            string cacheKey = $"{mapName}:{options.GetHashCode()}";
            if (_cache.TryGetValue(cacheKey, out var cached))
                return cached;
            
            await _loadLock.WaitAsync(ct);
            try
            {
                // Double-check cache after acquiring lock
                if (_cache.TryGetValue(cacheKey, out cached))
                    return cached;
                
                var startTime = DateTime.UtcNow;
                var warnings = new List<string>();
                
                progress?.Report(MapLoadProgress.Starting);
                
                // ============================================================
                // 1. LOAD RSW
                // ============================================================
                
                progress?.Report(new MapLoadProgress 
                { 
                    Stage = "Loading RSW", 
                    Progress = 0.1f,
                    CurrentFile = $"data\\{mapName}.rsw"
                });
                
                var rswPath = $"data\\{mapName}.rsw";
                var rswBytes = await _resolver.ReadFileAsync(rswPath, ct);
                
                RswFileV2? rsw = null;
                if (rswBytes != null && RswReaderV2.IsRswFile(rswBytes))
                {
                    try
                    {
                        rsw = RswReaderV2.Read(rswBytes);
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"RSW parse error: {ex.Message}");
                    }
                }
                else
                {
                    warnings.Add("RSW file not found or invalid");
                }
                
                ct.ThrowIfCancellationRequested();
                
                // ============================================================
                // 2. LOAD GND
                // ============================================================
                
                progress?.Report(new MapLoadProgress 
                { 
                    Stage = "Loading GND", 
                    Progress = 0.3f,
                    CurrentFile = rsw?.GndFile ?? $"{mapName}.gnd"
                });
                
                var gndFileName = rsw?.GndFile ?? $"{mapName}.gnd";
                var gndPath = $"data\\{gndFileName}";
                var gndBytes = await _resolver.ReadFileAsync(gndPath, ct);
                
                GndFileV2? gnd = null;
                if (gndBytes != null && GndReaderV2.IsGndFile(gndBytes))
                {
                    try
                    {
                        gnd = GndReaderV2.Read(gndBytes, options.GndOptions);
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"GND parse error: {ex.Message}");
                    }
                }
                else
                {
                    warnings.Add("GND file not found or invalid");
                }
                
                ct.ThrowIfCancellationRequested();
                
                // ============================================================
                // 3. LOAD GAT (Optional)
                // ============================================================
                
                GatFileV2? gat = null;
                if (options.LoadGat)
                {
                    progress?.Report(new MapLoadProgress 
                    { 
                        Stage = "Loading GAT", 
                        Progress = 0.5f,
                        CurrentFile = rsw?.GatFile ?? $"{mapName}.gat"
                    });
                    
                    var gatFileName = rsw?.GatFile ?? $"{mapName}.gat";
                    var gatPath = $"data\\{gatFileName}";
                    var gatBytes = await _resolver.ReadFileAsync(gatPath, ct);
                    
                    if (gatBytes != null && GatReaderV2.IsGatFile(gatBytes))
                    {
                        try
                        {
                            gat = GatReaderV2.Read(gatBytes);
                        }
                        catch (Exception ex)
                        {
                            warnings.Add($"GAT parse error: {ex.Message}");
                        }
                    }
                }
                
                ct.ThrowIfCancellationRequested();
                
                // ============================================================
                // 4. GENERATE MESH
                // ============================================================
                
                TerrainMeshV2? mesh = null;
                if (options.GenerateMesh && gnd != null)
                {
                    progress?.Report(new MapLoadProgress 
                    { 
                        Stage = "Generating Mesh", 
                        Progress = 0.7f
                    });
                    
                    try
                    {
                        mesh = TerrainMeshBuilderV2.Build(gnd, options.MeshOptions);
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"Mesh generation error: {ex.Message}");
                    }
                }
                
                // ============================================================
                // 5. CALCULATE BOUNDS
                // ============================================================
                
                progress?.Report(new MapLoadProgress 
                { 
                    Stage = "Finalizing", 
                    Progress = 0.9f
                });
                
                Vector3 boundsMin, boundsMax;
                if (mesh != null)
                {
                    boundsMin = mesh.BoundsMin;
                    boundsMax = mesh.BoundsMax;
                }
                else if (gnd != null)
                {
                    boundsMin = Vector3.Zero;
                    boundsMax = new Vector3(
                        gnd.Width * gnd.TileScale,
                        100,
                        gnd.Height * gnd.TileScale
                    );
                }
                else
                {
                    boundsMin = Vector3.Zero;
                    boundsMax = new Vector3(1000, 100, 1000);
                }
                
                // ============================================================
                // 6. BUILD RESULT
                // ============================================================
                
                var loadTime = DateTime.UtcNow - startTime;
                
                var result = new LoadedMapV2
                {
                    Name = mapName,
                    Rsw = rsw,
                    Gnd = gnd,
                    Gat = gat,
                    TerrainMesh = mesh,
                    BoundsMin = boundsMin,
                    BoundsMax = boundsMax,
                    LoadTime = loadTime,
                    Warnings = warnings
                };
                
                // Cache result
                _cache.TryAdd(cacheKey, result);
                
                progress?.Report(MapLoadProgress.Complete);
                
                return result;
            }
            finally
            {
                _loadLock.Release();
            }
        }
        
        /// <summary>
        /// Clear the map cache.
        /// </summary>
        public void ClearCache()
        {
            foreach (var kvp in _cache)
            {
                kvp.Value.Dispose();
            }
            _cache.Clear();
        }
        
        /// <summary>
        /// Remove a specific map from cache.
        /// </summary>
        public void RemoveFromCache(string mapName)
        {
            var keysToRemove = new List<string>();
            foreach (var kvp in _cache)
            {
                if (kvp.Key.StartsWith(mapName + ":"))
                    keysToRemove.Add(kvp.Key);
            }
            
            foreach (var key in keysToRemove)
            {
                if (_cache.TryRemove(key, out var map))
                    map.Dispose();
            }
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            ClearCache();
            _loadLock.Dispose();
        }
    }
    
    // ========================================================================
    // GAT FILE SUPPORT (Placeholder - expand as needed)
    // ========================================================================
    
    /// <summary>
    /// GAT (Ground Altitude) file data.
    /// </summary>
    public sealed class GatFileV2
    {
        public ushort Version { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public GatCellV2[,] Cells { get; init; } = new GatCellV2[0, 0];
    }
    
    /// <summary>
    /// GAT cell data.
    /// </summary>
    public readonly struct GatCellV2
    {
        public readonly float Height00;
        public readonly float Height10;
        public readonly float Height01;
        public readonly float Height11;
        public readonly byte Type;
        
        public GatCellV2(float h00, float h10, float h01, float h11, byte type)
        {
            Height00 = h00;
            Height10 = h10;
            Height01 = h01;
            Height11 = h11;
            Type = type;
        }
        
        /// <summary>Check if cell is walkable</summary>
        public bool IsWalkable => Type == 0 || Type == 3;
        
        /// <summary>Check if cell blocks LOS (line of sight)</summary>
        public bool BlocksLOS => Type == 1;
    }
    
    /// <summary>
    /// GAT file reader.
    /// </summary>
    public static class GatReaderV2
    {
        private static readonly byte[] SIGNATURE = { (byte)'G', (byte)'R', (byte)'A', (byte)'T' };
        
        public static bool IsGatFile(ReadOnlySpan<byte> data)
        {
            return data.Length >= 4 &&
                   data[0] == SIGNATURE[0] &&
                   data[1] == SIGNATURE[1] &&
                   data[2] == SIGNATURE[2] &&
                   data[3] == SIGNATURE[3];
        }
        
        public static GatFileV2 Read(byte[] data)
        {
            if (data == null || data.Length < 14)
                throw new InvalidDataException("GAT file too small");
            
            if (!IsGatFile(data))
                throw new InvalidDataException("Not a GAT file");
            
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);
            
            br.ReadBytes(4); // Signature
            ushort version = br.ReadUInt16();
            int width = br.ReadInt32();
            int height = br.ReadInt32();
            
            var cells = new GatCellV2[width, height];
            
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float h00 = br.ReadSingle();
                    float h10 = br.ReadSingle();
                    float h01 = br.ReadSingle();
                    float h11 = br.ReadSingle();
                    byte type = br.ReadByte();
                    
                    // Skip unknown bytes based on version
                    if (version >= 0x0001)
                        br.ReadBytes(3);
                    
                    cells[x, y] = new GatCellV2(h00, h10, h01, h11, type);
                }
            }
            
            return new GatFileV2
            {
                Version = version,
                Width = width,
                Height = height,
                Cells = cells
            };
        }
    }
}
