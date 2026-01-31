using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using ROMapOverlayEditor.Vfs;

namespace ROMapOverlayEditor.Patching
{
    /// <summary>
    /// Writes a client patch as a ZIP with "data\..." virtual paths.
    /// Your goal is: edited files override GRF/base when placed in client data folder or patch loader.
    /// </summary>
    public static class PatchWriter
    {
        /// <summary>
        /// Writes a ZIP patch containing the given virtual-path => bytes.
        /// Virtual paths must be RO client style (e.g. "data\morocc.gat", "texture\...\map\morocc.bmp").
        /// We normalize and store them with forward slashes inside ZIP for portability.
        /// </summary>
        public static void WriteZip(string zipPath, IReadOnlyDictionary<string, byte[]> files, string? manifestText = null)
        {
            if (string.IsNullOrWhiteSpace(zipPath)) throw new ArgumentNullException(nameof(zipPath));
            if (files == null) throw new ArgumentNullException(nameof(files));

            var dir = Path.GetDirectoryName(zipPath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(zipPath))
                File.Delete(zipPath);

            using var fs = new FileStream(zipPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

            if (!string.IsNullOrWhiteSpace(manifestText))
            {
                var me = zip.CreateEntry("_manifest.txt", CompressionLevel.Optimal);
                using var ms = me.Open();
                using var sw = new StreamWriter(ms);
                sw.Write(manifestText);
            }

            foreach (var kv in files)
            {
                var vp = VPath.Norm(kv.Key);
                var bytes = kv.Value ?? Array.Empty<byte>();
                var entryName = vp.Replace('\\', '/');

                var e = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                using var es = e.Open();
                es.Write(bytes, 0, bytes.Length);
            }
        }
    }
}
