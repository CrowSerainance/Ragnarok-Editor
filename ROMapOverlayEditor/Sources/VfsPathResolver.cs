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

        public static (string? Rsw, string? Gnd, string? Gat) ResolveMapTriplet(CompositeVfs vfs, string mapBaseName)
        {
            mapBaseName = (mapBaseName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(mapBaseName))
                return (null, null, null);

            // Allow user to type "prontera.rsw" or "prontera"
            var baseName = Path.GetFileNameWithoutExtension(mapBaseName);

            string? rsw = null;
            string? gnd = null;
            string? gat = null;

            // Search across all mounted sources
            foreach (var source in vfs.Sources)
            {
                if (rsw == null) rsw = ResolveByFileName(source, baseName + ".rsw");
                if (gnd == null) gnd = ResolveByFileName(source, baseName + ".gnd");
                if (gat == null) gat = ResolveByFileName(source, baseName + ".gat");
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
