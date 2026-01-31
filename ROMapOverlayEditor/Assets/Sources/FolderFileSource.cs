using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ROMapOverlayEditor.Sources
{
    /// <summary>
    /// File source that indexes all files in a folder tree.
    /// Solves: "multiple lua/lub files will be selected" - user selects ONE folder,
    /// we index all files under it and resolve by virtual path or filename.
    /// </summary>
    public sealed class FolderFileSource : IFileSource
    {
        public string DisplayName { get; }
        public string RootFolder { get; }

        private readonly Dictionary<string, string> _byVirtualPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _byFileName = new(StringComparer.OrdinalIgnoreCase);

        public FolderFileSource(string rootFolder, string displayName = "Lua Folder")
        {
            RootFolder = rootFolder;
            DisplayName = $"{displayName}: {rootFolder}";
            Reindex();
        }

        /// <summary>Re-scan the folder and rebuild the index.</summary>
        public void Reindex()
        {
            _byVirtualPath.Clear();
            _byFileName.Clear();

            if (string.IsNullOrWhiteSpace(RootFolder) || !Directory.Exists(RootFolder))
                return;

            try
            {
                foreach (var file in Directory.EnumerateFiles(RootFolder, "*.*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(RootFolder, file).Replace('\\', '/');
                    var virtualPath = rel;

                    _byVirtualPath[virtualPath] = file;

                    var name = Path.GetFileName(file);
                    if (!_byFileName.TryGetValue(name, out var list))
                    {
                        list = new List<string>();
                        _byFileName[name] = list;
                    }
                    list.Add(virtualPath);
                }
            }
            catch (Exception)
            {
                // Folder access issues - leave index empty
            }
        }

        public bool Exists(string virtualPath)
        {
            if (string.IsNullOrWhiteSpace(virtualPath)) return false;
            virtualPath = Normalize(virtualPath);

            if (_byVirtualPath.ContainsKey(virtualPath))
                return true;

            // Also check by filename only
            var name = Path.GetFileName(virtualPath);
            return _byFileName.ContainsKey(name);
        }

        public byte[] ReadAllBytes(string virtualPath)
        {
            virtualPath = Normalize(virtualPath);

            if (_byVirtualPath.TryGetValue(virtualPath, out var full))
                return File.ReadAllBytes(full);

            // Also allow calling with just "Towninfo.lub" or "Towninfo.lua"
            var name = Path.GetFileName(virtualPath);
            if (_byFileName.TryGetValue(name, out var candidates) && candidates.Count > 0)
            {
                var chosen = candidates[0];
                return File.ReadAllBytes(_byVirtualPath[chosen]);
            }

            throw new FileNotFoundException($"Folder source: not found: {virtualPath}");
        }

        public IEnumerable<string> EnumeratePaths() => _byVirtualPath.Keys;

        /// <summary>Find all virtual paths that match a given filename.</summary>
        public IEnumerable<string> FindByFileName(string fileName)
        {
            if (_byFileName.TryGetValue(fileName, out var list))
                return list;
            return Enumerable.Empty<string>();
        }

        /// <summary>Find all files with a given extension (e.g., ".lua").</summary>
        public IEnumerable<string> FindByExtension(string extension)
        {
            extension = extension.TrimStart('.');
            return _byVirtualPath.Keys.Where(p =>
                p.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Get the absolute file path for a virtual path.</summary>
        public string? GetAbsolutePath(string virtualPath)
        {
            virtualPath = Normalize(virtualPath);
            return _byVirtualPath.TryGetValue(virtualPath, out var full) ? full : null;
        }

        private static string Normalize(string p) => p.Replace('\\', '/').TrimStart('/');
    }
}
