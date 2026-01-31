using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace ROMapOverlayEditor.Vfs
{
    public sealed class ArchiveSource : IAssetSource, IDisposable
    {
        private readonly string _archivePath;
        private IArchive _archive;
        private Dictionary<string, IArchiveEntry> _entries;

        public string DisplayName { get; }
        public int Priority { get; }

        public ArchiveSource(string archivePath, int priority = 20, string? displayName = null)
        {
            _archivePath = archivePath ?? throw new ArgumentNullException(nameof(archivePath));
            if (!File.Exists(_archivePath))
                throw new FileNotFoundException(_archivePath);

            Priority = priority;
            DisplayName = displayName ?? $"Archive: {Path.GetFileName(_archivePath)}";

            _archive = ArchiveFactory.Open(_archivePath);
            _entries = BuildIndex(_archive);
        }

        public void Dispose()
        {
            try { _archive?.Dispose(); } catch { /* ignore */ }
        }

        public IEnumerable<string> EnumeratePaths() => _entries.Keys;

        public bool Contains(string virtualPath)
            => _entries.ContainsKey(VPath.Norm(virtualPath));

        public bool TryReadAllBytes(string virtualPath, out byte[]? bytes, out string? error)
        {
            bytes = null;
            error = null;

            try
            {
                var p = VPath.Norm(virtualPath);
                if (!_entries.TryGetValue(p, out var e))
                {
                    error = $"Not found in archive: {virtualPath}";
                    return false;
                }

                using var ms = new MemoryStream();
                e.WriteTo(ms);
                bytes = ms.ToArray();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static Dictionary<string, IArchiveEntry> BuildIndex(IArchive archive)
        {
            // First match wins; if duplicates exist, prefer non-directory, then larger size
            var dict = new Dictionary<string, IArchiveEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var e in archive.Entries.Where(x => !x.IsDirectory))
            {
                var key = VPath.Norm(e.Key);

                if (!dict.TryGetValue(key, out var existing))
                {
                    dict[key] = e;
                    continue;
                }

                // prefer larger entry as heuristic
                if (e.Size > existing.Size)
                    dict[key] = e;
            }

            return dict;
        }
    }
}
