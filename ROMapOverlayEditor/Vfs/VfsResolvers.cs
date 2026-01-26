// ============================================================================
// VfsResolvers.cs - Virtual File System Implementations
// ============================================================================
// PURPOSE: File resolvers for GRF archives and folder sources
// INTEGRATION: Drop into ROMapOverlayEditor/Vfs/ folder
// FEATURES:
//   - Async GRF file reading
//   - Composite (multi-source) VFS
//   - Path normalization
//   - Caching layer
// ============================================================================

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ROMapOverlayEditor.MapAssets;

namespace ROMapOverlayEditor.Vfs
{
    // ========================================================================
    // FOLDER-BASED FILE RESOLVER
    // ========================================================================
    
    /// <summary>
    /// File resolver that reads from a local folder.
    /// Useful for loose files or extracted GRF contents.
    /// </summary>
    public sealed class FolderFileResolver : IMapFileResolver
    {
        private readonly string _basePath;
        
        /// <summary>
        /// Create resolver for a folder path.
        /// </summary>
        /// <param name="basePath">Base folder path (e.g., "C:\RO\data")</param>
        public FolderFileResolver(string basePath)
        {
            _basePath = Path.GetFullPath(basePath);
        }
        
        public Task<byte[]?> ReadFileAsync(string path, CancellationToken ct = default)
        {
            var fullPath = NormalizePath(path);
            
            if (!File.Exists(fullPath))
                return Task.FromResult<byte[]?>(null);
            
            return File.ReadAllBytesAsync(fullPath, ct)!;
        }
        
        public Task<bool> FileExistsAsync(string path, CancellationToken ct = default)
        {
            var fullPath = NormalizePath(path);
            return Task.FromResult(File.Exists(fullPath));
        }
        
        private string NormalizePath(string path)
        {
            // Convert RO-style paths to local paths
            var normalized = path
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
            
            return Path.Combine(_basePath, normalized);
        }
    }
    
    // ========================================================================
    // GRF-BASED FILE RESOLVER
    // ========================================================================
    
    /// <summary>
    /// File resolver that reads from GRF archives.
    /// Supports both GRF 0x200 (LZSS) and 0x103 (zlib) compression.
    /// </summary>
    public sealed class GrfFileResolver : IMapFileResolver, IDisposable
    {
        private readonly string _grfPath;
        private readonly FileStream _stream;
        private readonly BinaryReader _reader;
        private readonly Dictionary<string, GrfEntry> _entries;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private bool _disposed;
        
        /// <summary>GRF version</summary>
        public uint Version { get; }
        
        /// <summary>Number of files in archive</summary>
        public int FileCount => _entries.Count;
        
        /// <summary>
        /// Open a GRF archive.
        /// </summary>
        /// <param name="grfPath">Path to .grf file</param>
        public GrfFileResolver(string grfPath)
        {
            _grfPath = grfPath;
            _stream = new FileStream(grfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            _reader = new BinaryReader(_stream, Encoding.GetEncoding(949));
            
            // Read header
            var header = _reader.ReadBytes(16);
            if (header[0] != 'M' || header[1] != 'a' || header[2] != 's' || header[3] != 't')
                throw new InvalidDataException("Not a valid GRF file");
            
            _stream.Position = 0x1E;
            uint tableOffset = _reader.ReadUInt32();
            uint seed = _reader.ReadUInt32();
            uint fileCount = _reader.ReadUInt32();
            Version = _reader.ReadUInt32();
            
            // Calculate actual file count
            int realFileCount = (int)(fileCount - seed - 7);
            
            // Read file table
            _stream.Position = tableOffset + 46;
            
            var compressedTableSize = _reader.ReadUInt32();
            var tableSize = _reader.ReadUInt32();
            var tableData = _reader.ReadBytes((int)compressedTableSize);
            
            // Decompress table
            var decompressed = DecompressZlib(tableData, (int)tableSize);
            
            // Parse entries
            _entries = new Dictionary<string, GrfEntry>(realFileCount, StringComparer.OrdinalIgnoreCase);
            
            using var tableReader = new BinaryReader(new MemoryStream(decompressed), Encoding.GetEncoding(949));
            
            for (int i = 0; i < realFileCount; i++)
            {
                var entry = ReadEntry(tableReader);
                if (entry != null && !string.IsNullOrEmpty(entry.Name))
                {
                    _entries[NormalizeKey(entry.Name)] = entry;
                }
            }
        }
        
        public async Task<byte[]?> ReadFileAsync(string path, CancellationToken ct = default)
        {
            var key = NormalizeKey(path);
            
            if (!_entries.TryGetValue(key, out var entry))
                return null;
            
            await _lock.WaitAsync(ct);
            try
            {
                return ReadEntryData(entry);
            }
            finally
            {
                _lock.Release();
            }
        }
        
        public Task<bool> FileExistsAsync(string path, CancellationToken ct = default)
        {
            var key = NormalizeKey(path);
            return Task.FromResult(_entries.ContainsKey(key));
        }
        
        /// <summary>
        /// Get all file paths in the archive.
        /// </summary>
        public IEnumerable<string> GetAllPaths()
        {
            return _entries.Keys;
        }
        
        /// <summary>
        /// Get file paths matching a pattern.
        /// </summary>
        public IEnumerable<string> GetPaths(string pattern)
        {
            var normalized = NormalizeKey(pattern).Replace("*", "");
            return _entries.Keys.Where(k => k.Contains(normalized, StringComparison.OrdinalIgnoreCase));
        }
        
        private GrfEntry? ReadEntry(BinaryReader reader)
        {
            // Read null-terminated filename
            var nameBytes = new List<byte>();
            byte b;
            while ((b = reader.ReadByte()) != 0)
                nameBytes.Add(b);
            
            if (nameBytes.Count == 0)
                return null;
            
            var name = Encoding.GetEncoding(949).GetString(nameBytes.ToArray());
            
            var compressedSize = reader.ReadInt32();
            var alignedCompressedSize = reader.ReadInt32();
            var uncompressedSize = reader.ReadInt32();
            var flags = reader.ReadByte();
            var offset = reader.ReadInt32();
            
            return new GrfEntry
            {
                Name = name,
                CompressedSize = compressedSize - alignedCompressedSize - 715,
                UncompressedSize = uncompressedSize,
                Flags = flags,
                Offset = offset + 46
            };
        }
        
        private byte[] ReadEntryData(GrfEntry entry)
        {
            _stream.Position = entry.Offset;
            var compressed = _reader.ReadBytes(entry.CompressedSize);
            
            // Check if already uncompressed
            if ((entry.Flags & 1) == 0)
                return compressed;
            
            // Decompress
            return DecompressZlib(compressed, entry.UncompressedSize);
        }
        
        private static byte[] DecompressZlib(byte[] data, int uncompressedSize)
        {
            // Skip zlib header (2 bytes)
            using var ms = new MemoryStream(data, 2, data.Length - 2);
            using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
            
            var result = new byte[uncompressedSize];
            int totalRead = 0;
            
            while (totalRead < uncompressedSize)
            {
                int read = deflate.Read(result, totalRead, uncompressedSize - totalRead);
                if (read == 0) break;
                totalRead += read;
            }
            
            return result;
        }
        
        private static string NormalizeKey(string path)
        {
            return path
                .Replace('/', '\\')
                .ToLowerInvariant()
                .TrimStart('\\');
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _reader.Dispose();
            _stream.Dispose();
            _lock.Dispose();
        }
        
        private class GrfEntry
        {
            public string Name { get; set; } = string.Empty;
            public int CompressedSize { get; set; }
            public int UncompressedSize { get; set; }
            public byte Flags { get; set; }
            public int Offset { get; set; }
        }
    }
    
    // ========================================================================
    // COMPOSITE FILE RESOLVER
    // ========================================================================
    
    /// <summary>
    /// Composite resolver that searches multiple sources in priority order.
    /// First matching file wins.
    /// </summary>
    public sealed class CompositeFileResolver : IMapFileResolver, IDisposable
    {
        private readonly List<IMapFileResolver> _resolvers = new();
        private readonly ConcurrentDictionary<string, int> _resolverCache = new();
        
        /// <summary>
        /// Add a resolver with the given priority (lower = higher priority).
        /// </summary>
        public void AddResolver(IMapFileResolver resolver, int priority = 0)
        {
            lock (_resolvers)
            {
                _resolvers.Add(resolver);
                // Could sort by priority if needed
            }
        }
        
        /// <summary>
        /// Add a folder source.
        /// </summary>
        public void AddFolder(string path, int priority = 0)
        {
            if (Directory.Exists(path))
                AddResolver(new FolderFileResolver(path), priority);
        }
        
        /// <summary>
        /// Add a GRF source.
        /// </summary>
        public void AddGrf(string path, int priority = 0)
        {
            if (File.Exists(path))
                AddResolver(new GrfFileResolver(path), priority);
        }
        
        public async Task<byte[]?> ReadFileAsync(string path, CancellationToken ct = default)
        {
            // Check cache for known resolver
            if (_resolverCache.TryGetValue(path, out var cachedIndex))
            {
                var result = await _resolvers[cachedIndex].ReadFileAsync(path, ct);
                if (result != null)
                    return result;
                
                // Cache miss, remove stale entry
                _resolverCache.TryRemove(path, out _);
            }
            
            // Search all resolvers
            for (int i = 0; i < _resolvers.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                
                var result = await _resolvers[i].ReadFileAsync(path, ct);
                if (result != null)
                {
                    // Cache the successful resolver
                    _resolverCache.TryAdd(path, i);
                    return result;
                }
            }
            
            return null;
        }
        
        public async Task<bool> FileExistsAsync(string path, CancellationToken ct = default)
        {
            foreach (var resolver in _resolvers)
            {
                ct.ThrowIfCancellationRequested();
                
                if (await resolver.FileExistsAsync(path, ct))
                    return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Clear the resolver cache.
        /// </summary>
        public void ClearCache()
        {
            _resolverCache.Clear();
        }
        
        public void Dispose()
        {
            foreach (var resolver in _resolvers)
            {
                if (resolver is IDisposable disposable)
                    disposable.Dispose();
            }
            _resolvers.Clear();
        }
    }
    
    // ========================================================================
    // CACHING LAYER
    // ========================================================================
    
    /// <summary>
    /// Caching wrapper for any file resolver.
    /// </summary>
    public sealed class CachedFileResolver : IMapFileResolver, IDisposable
    {
        private readonly IMapFileResolver _inner;
        private readonly ConcurrentDictionary<string, byte[]?> _cache = new();
        private readonly int _maxCacheSize;
        private long _currentCacheSize;
        
        /// <summary>
        /// Create caching wrapper.
        /// </summary>
        /// <param name="inner">Inner resolver</param>
        /// <param name="maxCacheSizeMB">Maximum cache size in MB</param>
        public CachedFileResolver(IMapFileResolver inner, int maxCacheSizeMB = 256)
        {
            _inner = inner;
            _maxCacheSize = maxCacheSizeMB * 1024 * 1024;
        }
        
        public async Task<byte[]?> ReadFileAsync(string path, CancellationToken ct = default)
        {
            var key = path.ToLowerInvariant();
            
            if (_cache.TryGetValue(key, out var cached))
                return cached;
            
            var data = await _inner.ReadFileAsync(path, ct);
            
            // Cache if small enough
            if (data != null && data.Length < _maxCacheSize / 10)
            {
                TryAddToCache(key, data);
            }
            
            return data;
        }
        
        public Task<bool> FileExistsAsync(string path, CancellationToken ct = default)
        {
            var key = path.ToLowerInvariant();
            
            if (_cache.ContainsKey(key))
                return Task.FromResult(true);
            
            return _inner.FileExistsAsync(path, ct);
        }
        
        private void TryAddToCache(string key, byte[] data)
        {
            // Simple eviction: clear half the cache if full
            if (_currentCacheSize + data.Length > _maxCacheSize)
            {
                var keysToRemove = _cache.Keys.Take(_cache.Count / 2).ToList();
                foreach (var k in keysToRemove)
                {
                    if (_cache.TryRemove(k, out var removed) && removed != null)
                        Interlocked.Add(ref _currentCacheSize, -removed.Length);
                }
            }
            
            if (_cache.TryAdd(key, data))
                Interlocked.Add(ref _currentCacheSize, data.Length);
        }
        
        /// <summary>
        /// Clear the cache.
        /// </summary>
        public void ClearCache()
        {
            _cache.Clear();
            _currentCacheSize = 0;
        }
        
        public void Dispose()
        {
            ClearCache();
            
            if (_inner is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
