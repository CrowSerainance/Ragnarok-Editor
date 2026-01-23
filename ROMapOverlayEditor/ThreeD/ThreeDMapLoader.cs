using System;
using System.IO;
using ROMapOverlayEditor.Vfs;

namespace ROMapOverlayEditor.ThreeD
{
    public sealed class ThreeDMap
    {
        public string BaseName { get; set; } = "";
        public string RswPath { get; set; } = "";
        public string GndPath { get; set; } = "";
        public string GatPath { get; set; } = "";

        public byte[] RswBytes { get; set; } = Array.Empty<byte>();
        public byte[] GndBytes { get; set; } = Array.Empty<byte>();
        public byte[] GatBytes { get; set; } = Array.Empty<byte>();
    }

    public readonly struct ThreeDMapLoadResult
    {
        public bool Ok { get; }
        public string Message { get; }
        public ThreeDMap? Map { get; }

        private ThreeDMapLoadResult(bool ok, string msg, ThreeDMap? map)
        {
            Ok = ok;
            Message = msg;
            Map = map;
        }

        public static ThreeDMapLoadResult Success(ThreeDMap map) => new(true, "", map);
        public static ThreeDMapLoadResult Fail(string msg) => new(false, msg, null);
    }

    public static class ThreeDMapLoader
    {
        public static ThreeDMapLoadResult Load(CompositeVfs vfs, string rswPath)
        {
            rswPath = Normalize(rswPath);

            // Derive base name and sibling paths
            var dir = Path.GetDirectoryName(rswPath)?.Replace('\\', '/') ?? "";
            var baseName = Path.GetFileNameWithoutExtension(rswPath);

            // Paths
            var gndPath = Combine(dir, baseName + ".gnd");
            var gatPath = Combine(dir, baseName + ".gat");

            // 1. Read RSW
            if (!vfs.Exists(rswPath))
            {
                 // Try fallback: maybe user gave only filename "prontera.rsw" but it's in "data/prontera.rsw"?
                 // But typically rswPath comes from a browser, so it's correct.
                 // We can try to resolve virtually if missing.
                 return FailMissing("RSW not found in VFS", rswPath);
            }

            byte[] rswBytes;
            try { rswBytes = vfs.ReadAllBytes(rswPath); }
            catch (Exception ex) { return FailMissing($"RSW not readable: {ex.Message}", rswPath); }

            // 2. Read GND/GAT
            // Check existence
            bool gndExists = vfs.Exists(gndPath);
            bool gatExists = vfs.Exists(gatPath);

            if (!gndExists)
                return FailMissing("GND missing (required for 3D)", gndPath);
            
            // GAT is recommended for walkability data

            byte[] gndBytes = vfs.ReadAllBytes(gndPath);
            byte[] gatBytes = gatExists ? vfs.ReadAllBytes(gatPath) : Array.Empty<byte>();

            var map = new ThreeDMap
            {
                BaseName = baseName,
                RswPath = rswPath,
                GndPath = gndPath,
                GatPath = gatPath,
                RswBytes = rswBytes,
                GndBytes = gndBytes,
                GatBytes = gatBytes
            };

            return ThreeDMapLoadResult.Success(map);
        }

        private static ThreeDMapLoadResult FailMissing(string title, string path)
        {
            return ThreeDMapLoadResult.Fail(
                $"{title}\n\n" +
                $"Path: {path}\n\n" +
                "For 3D maps you must have:\n" +
                " - {map}.rsw\n" +
                " - {map}.gnd\n" +
                " - {map}.gat (recommended)\n\n" +
                "Ensure your sources (GRF or Folder) contain these files.");
        }

        private static string Normalize(string p) => p.Replace('\\', '/').TrimStart('/');

        private static string Combine(string dir, string file)
        {
            if (string.IsNullOrWhiteSpace(dir)) return file;
            return (dir.TrimEnd('/') + "/" + file).Replace('\\', '/');
        }
    }
}
