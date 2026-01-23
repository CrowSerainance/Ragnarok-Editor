using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ROMapOverlayEditor.Vfs
{
    public sealed class FolderSource : IAssetSource
    {
        private readonly string _root;
        private HashSet<string>? _index;

        public string DisplayName { get; }
        public int Priority { get; }

        public FolderSource(string rootFolder, int priority = 10, string? displayName = null)
        {
            _root = rootFolder ?? throw new ArgumentNullException(nameof(rootFolder));
            if (!Directory.Exists(_root))
                throw new DirectoryNotFoundException(_root);

            Priority = priority;
            DisplayName = displayName ?? $"Folder: {Path.GetFileName(_root)}";
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

            try
            {
                var p = VPath.Norm(virtualPath);
                var full = Path.Combine(_root, p.Replace('\\', Path.DirectorySeparatorChar));

                if (!File.Exists(full))
                {
                    error = $"Missing file in folder: {full}";
                    return false;
                }

                bytes = File.ReadAllBytes(full);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private void EnsureIndex()
        {
            if (_index != null) return;

            _index = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(_root, file);
                rel = rel.Replace(Path.DirectorySeparatorChar, '\\');
                _index.Add(VPath.Norm(rel));
            }
        }
    }
}
