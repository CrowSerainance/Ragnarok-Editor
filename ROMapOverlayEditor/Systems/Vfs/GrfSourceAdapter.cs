using System;
using System.Collections.Generic;
using System.Linq;

namespace ROMapOverlayEditor.Vfs
{
    public sealed class GrfSourceAdapter : IAssetSource
    {
        private readonly Func<IEnumerable<string>> _listPaths;
        private readonly Func<string, (bool ok, byte[]? bytes, string? error)> _readBytes;

        private HashSet<string>? _index;

        public string DisplayName { get; }
        public int Priority { get; }

        public GrfSourceAdapter(
            string displayName,
            int priority,
            Func<IEnumerable<string>> listPaths,
            Func<string, (bool ok, byte[]? bytes, string? error)> readBytes)
        {
            DisplayName = displayName;
            Priority = priority;
            _listPaths = listPaths ?? throw new ArgumentNullException(nameof(listPaths));
            _readBytes = readBytes ?? throw new ArgumentNullException(nameof(readBytes));
        }

        public IEnumerable<string> EnumeratePaths()
        {
            EnsureIndex();
            return _index!;
        }

        public bool Contains(string virtualPath)
        {
            EnsureIndex();
            return _index!.Contains(VPath.Norm(virtualPath));
        }

        public bool TryReadAllBytes(string virtualPath, out byte[]? bytes, out string? error)
        {
            bytes = null;
            error = null;

            var p = VPath.Norm(virtualPath);
            var r = _readBytes(p);
            if (!r.ok || r.bytes == null)
            {
                error = r.error ?? $"Failed to read: {virtualPath}";
                return false;
            }

            bytes = r.bytes;
            return true;
        }

        private void EnsureIndex()
        {
            if (_index != null) return;
            _index = _listPaths().Select(VPath.Norm).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }
}
