using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ROMapOverlayEditor.Vfs;

namespace ROMapOverlayEditor.Sources
{
    public static class VfsPathResolver
    {
        // Prefer these folders first (common RO layouts)
        private static readonly string[] PreferredContains =
        {
            "/data/",
            "/data/maps/",
            "/maps/",
        };

        public static string Normalize(string p) => p.Replace('\\', '/').TrimStart('/');

        public static string? ResolveByFileName(ROMapOverlayEditor.Vfs.IAssetSource source, string fileName)
        {
            fileName = Normalize(fileName);
            var candidates = source.EnumeratePaths()
                .Select(Normalize)
                .Where(p => p.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (candidates.Count == 0) return null;
            if (candidates.Count == 1) return candidates[0];

            // Score candidates: prefer common directories + shorter paths
            int Score(string p)
            {
                int s = 0;
                foreach (var pref in PreferredContains)
                    if (p.Contains(pref, StringComparison.OrdinalIgnoreCase)) s += 10;

                // shorter is usually "more canonical"
                s += Math.Max(0, 50 - p.Length / 2);
                return s;
            }

            return candidates.OrderByDescending(Score).First();
        }

        /// <summary>Enumerate RSW map base names from VFS (for backward compatibility).</summary>
        public static List<string> EnumerateRswMapNames(CompositeVfs vfs)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in vfs.EnumerateAllPathsDistinct())
            {
                if (p.EndsWith(".rsw", StringComparison.OrdinalIgnoreCase))
                    names.Add(Path.GetFileNameWithoutExtension(p));
            }
            var list = names.ToList();
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        /// <summary>Enumerate all RSW file paths from VFS (BrowEdit-style: every RSW in GRF, including data/, data/maps/, etc.).</summary>
        public static List<string> EnumerateRswPaths(CompositeVfs vfs)
        {
            var paths = new List<string>();
            foreach (var p in vfs.EnumerateAllPathsDistinct())
            {
                if (p.EndsWith(".rsw", StringComparison.OrdinalIgnoreCase))
                    paths.Add(p);
            }
            paths.Sort(StringComparer.OrdinalIgnoreCase);
            return paths;
        }

        public static (string? Rsw, string? Gnd, string? Gat) ResolveMapTriplet(CompositeVfs vfs, string mapBaseName)
        {
            mapBaseName = (mapBaseName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(mapBaseName))
                return (null, null, null);

            var normalized = Normalize(mapBaseName);
            var isFullPath = normalized.Contains("/") || normalized.Contains("\\");

            // If user selected full path (e.g. "data/maps/prontera.rsw"), use that RSW and resolve GND/GAT from same folder first
            if (isFullPath && normalized.EndsWith(".rsw", StringComparison.OrdinalIgnoreCase) && vfs.Exists(normalized))
            {
                var dir = Path.GetDirectoryName(normalized)?.Replace('\\', '/') ?? "";
                var baseName = Path.GetFileNameWithoutExtension(normalized);
                var siblingGnd = string.IsNullOrEmpty(dir) ? baseName + ".gnd" : dir.TrimEnd('/') + "/" + baseName + ".gnd";
                var siblingGat = string.IsNullOrEmpty(dir) ? baseName + ".gat" : dir.TrimEnd('/') + "/" + baseName + ".gat";
                string? gndPath = vfs.Exists(siblingGnd) ? siblingGnd : null;
                string? gatPath = vfs.Exists(siblingGat) ? siblingGat : null;
                if (gndPath == null || gatPath == null)
                {
                    foreach (var source in vfs.Sources)
                    {
                        if (gndPath == null) gndPath = ResolveByFileName(source, baseName + ".gnd");
                        if (gatPath == null) gatPath = ResolveByFileName(source, baseName + ".gat");
                    }
                }
                return (normalized, gndPath, gatPath);
            }

            // Allow user to type "prontera.rsw" or "prontera"
            var baseNameOnly = Path.GetFileNameWithoutExtension(mapBaseName);

            string? rsw = null;
            string? gnd = null;
            string? gat = null;

            // Search across all mounted sources
            foreach (var source in vfs.Sources)
            {
                if (rsw == null) rsw = ResolveByFileName(source, baseNameOnly + ".rsw");
                if (gnd == null) gnd = ResolveByFileName(source, baseNameOnly + ".gnd");
                if (gat == null) gat = ResolveByFileName(source, baseNameOnly + ".gat");
            }

            return (rsw, gnd, gat);
        }

        public static string? ResolveByFileName(IFileSource source, string fileName)
        {
            fileName = Normalize(fileName);
            var candidates = source.EnumeratePaths()
                .Select(Normalize)
                .Where(p => p.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (candidates.Count == 0) return null;
            if (candidates.Count == 1) return candidates[0];

            // Score candidates: prefer common directories + shorter paths
            int Score(string p)
            {
                int s = 0;
                foreach (var pref in PreferredContains)
                    if (p.Contains(pref, StringComparison.OrdinalIgnoreCase)) s += 10;

                // shorter is usually "more canonical"
                s += Math.Max(0, 50 - p.Length / 2);
                return s;
            }

            return candidates.OrderByDescending(Score).First();
        }

        public static (string? Rsw, string? Gnd, string? Gat) ResolveMapTriplet(CompositeFileSource vfs, string mapBaseName)
        {
            mapBaseName = (mapBaseName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(mapBaseName))
                return (null, null, null);

            var baseName = Path.GetFileNameWithoutExtension(mapBaseName);

            string? rsw = null;
            string? gnd = null;
            string? gat = null;

            // Prefer GRF for map binaries; fallback folder if missing
            if (vfs.Grf != null)
            {
                rsw = ResolveByFileName(vfs.Grf, baseName + ".rsw");
                gnd = ResolveByFileName(vfs.Grf, baseName + ".gnd");
                gat = ResolveByFileName(vfs.Grf, baseName + ".gat");
            }

            if (vfs.Folder != null)
            {
                if (rsw == null) rsw = ResolveByFileName(vfs.Folder, baseName + ".rsw");
                if (gnd == null) gnd = ResolveByFileName(vfs.Folder, baseName + ".gnd");
                if (gat == null) gat = ResolveByFileName(vfs.Folder, baseName + ".gat");
            }

            return (rsw, gnd, gat);
        }
    }
}
