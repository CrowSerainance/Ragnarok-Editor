using System.Linq;
using ROMapOverlayEditor.Vfs;

namespace ROMapOverlayEditor.Gat
{
    public static class GatResolver
    {
        private static readonly string[] Candidates =
        {
            @"data\{0}.gat",
            @"maps\{0}.gat",
            @"data\maps\{0}.gat",
            @"data\map\{0}.gat",
        };

        public static string? ResolveVirtualPath(CompositeVfs vfs, string mapName)
        {
            mapName = (mapName ?? "").Trim();
            if (mapName.Length == 0) return null;

            foreach (var fmt in Candidates)
            {
                var p = string.Format(fmt, mapName);
                if (vfs.ResolveFirstSourceName(p) != null)
                    return VPath.Norm(p);
            }

            var suffix = VPath.Norm($@"\{mapName}.gat");
            return vfs.EnumerateAllPathsDistinct().FirstOrDefault(p => p.EndsWith(suffix));
        }

        public static bool LooksLikeGat(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 16) return false;
            return bytes[0] == (byte)'G'
                && bytes[1] == (byte)'R'
                && bytes[2] == (byte)'A'
                && bytes[3] == (byte)'T';
        }
    }
}
