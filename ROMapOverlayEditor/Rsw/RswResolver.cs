using System;
using System.Linq;
using ROMapOverlayEditor.Sources;
using ROMapOverlayEditor.Vfs;

namespace ROMapOverlayEditor.Rsw
{
    public static class RswResolver
    {
        // RO assets usually live under "data/"
        private static readonly string[] Candidates =
        {
            "data/{0}.rsw",
            "data/map/{0}.rsw",
            "data/maps/{0}.rsw",
            "{0}.rsw"
        };

        public static string? ResolveRswPath(CompositeVfs vfs, string mapName)
        {
            mapName = (mapName ?? "").Trim();
            if (mapName.Length == 0) return null;

            foreach (var fmt in Candidates)
            {
                var p = string.Format(fmt, mapName).Replace("\\", "/");
                if (vfs.ResolveFirstSourceName(p) != null) return p;
            }

            // Fallback: scan for any *.rsw
            var suffix = ("/" + mapName + ".rsw").ToLowerInvariant();
            
            // This fallback is only possible if we can enumerate all (slow) or if we implemented a fast index.
            // CompositeVfs has EnumerateAllPathsDistinct.
            foreach (var path in vfs.EnumerateAllPathsDistinct())
            {
                if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return path;
            }

            return null;
        }
    }
}
