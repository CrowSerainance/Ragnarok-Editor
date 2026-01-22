using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ROMapOverlayEditor.Grf;

namespace ROMapOverlayEditor.Sources
{
    /// <summary>
    /// File source that wraps a GRF archive using the robust GrfArchive (0x200 format).
    /// </summary>
    public sealed class GrfFileSource : IFileSource, IDisposable
    {
        public string DisplayName { get; }
        public string GrfPath { get; }

        private readonly GrfArchive _archive;
        private readonly HashSet<string> _normalizedPaths;
        private readonly Dictionary<string, string> _originalPaths;

        public uint Version => _archive.Version;
        
        public GrfFileSource(string grfPath)
        {
            if (string.IsNullOrWhiteSpace(grfPath))
                throw new ArgumentException("GRF path cannot be empty.", nameof(grfPath));
            if (!File.Exists(grfPath))
                throw new FileNotFoundException("GRF file not found.", grfPath);

            GrfPath = grfPath;
            DisplayName = $"GRF: {Path.GetFileName(grfPath)}";

            // Open using the robust archive
            _archive = GrfArchive.Open(grfPath);

            // Build normalized path lookup for case-insensitive access
            _normalizedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _originalPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in _archive.Entries)
            {
                var normalized = Normalize(entry.Path);
                _normalizedPaths.Add(normalized);
                _originalPaths[normalized] = entry.Path;
            }
        }

        public bool Exists(string virtualPath)
        {
            if (string.IsNullOrWhiteSpace(virtualPath)) return false;
            return _normalizedPaths.Contains(Normalize(virtualPath));
        }

        public byte[] ReadAllBytes(string virtualPath)
        {
            virtualPath = Normalize(virtualPath);

            if (!_originalPaths.TryGetValue(virtualPath, out var originalPath))
                throw new FileNotFoundException($"GRF entry not found: {virtualPath}");

            return _archive.Extract(originalPath);
        }

        public IEnumerable<string> EnumeratePaths() => _normalizedPaths;

        /// <summary>Get all entries with their metadata.</summary>
        public IReadOnlyList<GrfEntry> Entries => _archive.Entries;

        /// <summary>Get map BMP paths (files under \map\ folder).</summary>
        public IReadOnlyList<string> GetMapBmpPaths()
        {
            // Re-implement logic using _archive.Entries
            var list = _archive.Entries
                .Where(e => (e.Flags & 1) != 0) // File
                .Where(e => e.Path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) && 
                            (e.Path.Contains("\\map\\", StringComparison.OrdinalIgnoreCase) || e.Path.Contains("/map/", StringComparison.OrdinalIgnoreCase)))
                .Select(e => e.Path)
                .ToList();

            if (list.Count > 0) return list;

            return _archive.Entries
                .Where(e => (e.Flags & 1) != 0 && e.Path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Path)
                .ToList();
        }

        /// <summary>Find entries matching a pattern (simple contains match).</summary>
        public IEnumerable<string> FindByPattern(string pattern)
        {
            return _normalizedPaths.Where(p =>
                p.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Find entries by file extension.</summary>
        public IEnumerable<string> FindByExtension(string extension)
        {
            extension = extension.TrimStart('.');
            return _normalizedPaths.Where(p =>
                p.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase));
        }

        public void Dispose() => _archive.Dispose();

        private static string Normalize(string p) => p.Replace('\\', '/').TrimStart('/');
    }
}
