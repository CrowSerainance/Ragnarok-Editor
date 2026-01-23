using System;

namespace ROMapOverlayEditor.Vfs
{
    public static class VPath
    {
        public static string Norm(string p)
        {
            p = (p ?? "").Replace('/', '\\').Trim();

            while (p.Contains("\\\\"))
                p = p.Replace("\\\\", "\\");

            // Remove leading ".\" if present
            if (p.StartsWith(".\\", StringComparison.Ordinal))
                p = p.Substring(2);

            return p.ToLowerInvariant();
        }

        public static bool LooksLikeAbsolute(string p)
            => !string.IsNullOrWhiteSpace(p) && (p.Contains(":\\") || p.StartsWith("\\\\"));
    }
}
