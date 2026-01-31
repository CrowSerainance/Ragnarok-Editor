using System;
using System.IO;
using System.Text;

namespace ROMapOverlayEditor.Grf
{
    /// <summary>
    /// Diagnostic helper to probe GRF header and identify version/layout issues.
    /// Call this right after opening a GRF file to get immediate feedback on compatibility.
    /// </summary>
    public static class GrfHeaderProbe
    {
        public static string Probe(string grfPath)
        {
            if (string.IsNullOrWhiteSpace(grfPath) || !File.Exists(grfPath))
                return $"GRF Probe: File not found or invalid path.";

            try
            {
                using var fs = File.OpenRead(grfPath);
                using var br = new BinaryReader(fs);

                // GRF header is 46 bytes
                fs.Position = 0;
                byte[] header = br.ReadBytes(46);
                if (header.Length < 46)
                    return $"GRF Probe: File too small ({header.Length} bytes, expected at least 46).";

                // Read signature (first 15 bytes)
                var sig = Encoding.ASCII.GetString(header, 0, 15);
                var sigValid = sig.Equals("Master of Magic", StringComparison.Ordinal);

                // Common GRF 0x200 layout:
                // 0x00: 15 bytes - Signature
                // 0x0F: 15 bytes - Key
                // 0x1E (30): 4 bytes - FileTableOffset (relative to 46)
                // 0x22 (34): 4 bytes - Seed/Reserved
                // 0x26 (38): 4 bytes - FilesCount
                // 0x2A (42): 4 bytes - Version

                uint offsetAt30 = BitConverter.ToUInt32(header, 30);
                uint valueAt34 = BitConverter.ToUInt32(header, 34);
                uint valueAt38 = BitConverter.ToUInt32(header, 38);
                uint versionAt42 = BitConverter.ToUInt32(header, 42);

                var fi = new FileInfo(grfPath);
                long fileSize = fi.Length;
                long absOffset30 = 46L + offsetAt30;
                long absOffset34 = 46L + valueAt34;

                bool offset30Valid = absOffset30 >= 46 && absOffset30 < fileSize - 8;
                bool offset34Valid = absOffset34 >= 46 && absOffset34 < fileSize - 8;

                var sb = new StringBuilder();
                sb.AppendLine($"GRF Header Probe: {Path.GetFileName(grfPath)}");
                sb.AppendLine($"  FileSize: {fileSize:N0} bytes");
                sb.AppendLine($"  Signature: {(sigValid ? "✓ 'Master of Magic'" : $"✗ '{sig.TrimEnd('\0')}'")}");
                sb.AppendLine($"  Version (0x2A): 0x{versionAt42:X} ({versionAt42})");
                sb.AppendLine($"  Offset@30 (0x1E): {offsetAt30} → abs={absOffset30} {(offset30Valid ? "✓" : "✗")}");
                sb.AppendLine($"  Value@34 (0x22): {valueAt34} → abs={absOffset34} {(offset34Valid ? "✓" : "✗")}");
                sb.AppendLine($"  Value@38 (0x26): {valueAt38}");

                // Version diagnosis
                if (versionAt42 == 0x200)
                    sb.AppendLine($"  → GRF 0x200 (supported)");
                else if (versionAt42 >= 0x100 && versionAt42 <= 0x103)
                    sb.AppendLine($"  → GRF 1.x (0x{versionAt42:X}) - NOT SUPPORTED");
                else if (versionAt42 == 0x300)
                    sb.AppendLine($"  → GRF 0x300 - NOT SUPPORTED (reader only supports 0x200)");
                else
                    sb.AppendLine($"  → Unknown version 0x{versionAt42:X} - may not be supported");

                // Offset diagnosis
                if (!offset30Valid && !offset34Valid)
                    sb.AppendLine($"  → ERROR: Neither offset candidate is valid (file may be corrupt or non-standard)");
                else if (!offset30Valid && offset34Valid)
                    sb.AppendLine($"  → WARNING: Offset@30 invalid, but offset@34 is valid (non-standard layout?)");
                else if (offset30Valid)
                    sb.AppendLine($"  → Offset@30 is valid (standard 0x200 layout)");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"GRF Probe: Error - {ex.Message}";
            }
        }
    }
}
