using System;
using System.Linq;
using ROMapOverlayEditor.Vfs;

namespace ROMapOverlayEditor.MapAssets
{
    public static class MapResolver
    {
        private static readonly string[] MinimapCandidates =
        {
            @"texture\À¯ÀúÀÎÅÍÆäÀÌ½º\map\{0}{1}",
            @"texture\userinterface\map\{0}{1}",
            @"texture\map\{0}{1}",
            @"data\texture\À¯ÀúÀÎÅÍÆäÀÌ½º\map\{0}{1}",
            @"data\texture\userinterface\map\{0}{1}",
            @"data\texture\map\{0}{1}",
        };

        private static readonly string[] GatCandidates =
        {
            @"data\{0}.gat",
            @"maps\{0}.gat",
            @"data\maps\{0}.gat",
            @"data\map\{0}.gat",
        };

        private static readonly string[] ImgExts = { ".bmp", ".png", ".jpg", ".jpeg" }; // bmp most common

        public static string? FindMinimapPath(CompositeVfs vfs, string mapName)
        {
            mapName = (mapName ?? "").Trim();
            if (mapName.Length == 0) return null;

            foreach (var ext in ImgExts)
            {
                foreach (var fmt in MinimapCandidates)
                {
                    var p = string.Format(fmt, mapName, ext);
                    if (vfs.ResolveFirstSourceName(p) != null)
                        return p;
                }
            }

            // fallback: search by filename suffix
            var suffixes = ImgExts.Select(ext => VPath.Norm($@"\map\{mapName}{ext}")).ToArray();
            foreach (var path in vfs.EnumerateAllPathsDistinct())
                if (suffixes.Any(s => path.EndsWith(s)))
                    return path;

            return null;
        }

        public static string? FindGatPath(CompositeVfs vfs, string mapName)
        {
            mapName = (mapName ?? "").Trim();
            if (mapName.Length == 0) return null;

            foreach (var fmt in GatCandidates)
            {
                var p = string.Format(fmt, mapName);
                if (vfs.ResolveFirstSourceName(p) != null)
                    return p;
            }

            // fallback: any \{map}.gat
            var suffix = VPath.Norm($@"\{mapName}.gat");
            foreach (var path in vfs.EnumerateAllPathsDistinct())
                if (path.EndsWith(suffix))
                    return path;

            return null;
        }
    }
}
