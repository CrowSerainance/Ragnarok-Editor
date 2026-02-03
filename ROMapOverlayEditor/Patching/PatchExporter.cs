using System;
using System.IO;
using System.IO.Compression;

namespace ROMapOverlayEditor.Patching
{
    public static class PatchExporter
    {
        /// <summary>
        /// Exports the current staging files into a ZIP at the provided output path.
        /// </summary>
        public static void ExportPatchZip(EditStaging staging, string outputZipPath)
        {
            if (staging == null) throw new ArgumentNullException(nameof(staging));
            if (string.IsNullOrWhiteSpace(outputZipPath)) throw new ArgumentNullException(nameof(outputZipPath));

            var dir = Path.GetDirectoryName(outputZipPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(outputZipPath))
                File.Delete(outputZipPath);

            using var zip = ZipFile.Open(outputZipPath, ZipArchiveMode.Create);

            // Assumption: staging.Files is Dictionary<string, byte[]> or similar.
            foreach (var kv in staging.Files)
            {
                var vpath = kv.Key.Replace('\\', '/').TrimStart('/');
                var bytes = kv.Value;
                if (bytes == null || bytes.Length == 0) continue;

                var entry = zip.CreateEntry(vpath, CompressionLevel.Optimal);
                using var es = entry.Open();
                es.Write(bytes, 0, bytes.Length);
            }
        }

        // Writes the staged files into a ZIP patch (paths stored as forward-slash)
        public static void ExportToZip(EditStaging staging, string zipPath)
        {
            // Redirect to ExportPatchZip for consistency
            ExportPatchZip(staging, zipPath);
        }
    }
}
