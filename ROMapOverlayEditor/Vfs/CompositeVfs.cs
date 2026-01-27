using System;
using System.Collections.Generic;
using System.Linq;

namespace ROMapOverlayEditor.Vfs
{
    public sealed class CompositeVfs : IVfs
    {
        private readonly List<IAssetSource> _sources = new();

        public IReadOnlyList<IAssetSource> Sources => _sources;

        public void Mount(IAssetSource src)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            _sources.Add(src);
            _sources.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        public void UnmountAll() => _sources.Clear();

        public bool TryReadAllBytes(string virtualPath, out byte[]? bytes, out string? error)
        {
            bytes = null;
            error = null;

            var p = VPath.Norm(virtualPath);

            foreach (var s in _sources)
            {
                if (!s.Contains(p)) continue;

                if (s.TryReadAllBytes(p, out bytes, out error) && bytes != null)
                    return true;

                // if it claimed Contains but read failed, continue to next source
            }

            error ??= $"Not found in mounted sources: {virtualPath}";
            return false;
        }

        public string? ResolveFirstSourceName(string virtualPath)
        {
            var p = VPath.Norm(virtualPath);
            foreach (var s in _sources)
                if (s.Contains(p))
                    return s.DisplayName;
            return null;
        }

        public IEnumerable<string> EnumerateAllPathsDistinct()
        {
            // Priority order; first occurrence wins
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in _sources.OrderByDescending(x => x.Priority))
            {
                foreach (var p in s.EnumeratePaths())
                {
                    var n = VPath.Norm(p);
                    if (seen.Add(n))
                        yield return n;
                }
            }
        }
        public bool Exists(string virtualPath)
        {
            var p = VPath.Norm(virtualPath);
            foreach (var s in _sources)
                if (s.Contains(p))
                    return true;
            return false;
        }

        public byte[] ReadAllBytes(string virtualPath)
        {
            if (TryReadAllBytes(virtualPath, out var bytes, out var err) && bytes != null)
                return bytes;
            throw new System.IO.FileNotFoundException(err ?? $"File not found: {virtualPath}");
        }
    }
}
