using System;
using ROMapOverlayEditor.Vfs;

namespace ROMapOverlayEditor.Systems.Vfs
{
    public static class VfsExtensions
    {
        // Allows legacy call-sites: vfs.TryReadAllBytes(path, out bytes)
        public static bool TryReadAllBytes(this IVfs vfs, string path, out byte[]? bytes)
        {
            if (vfs is null) throw new ArgumentNullException(nameof(vfs));
            return vfs.TryReadAllBytes(path, out bytes, out _);
        }

        // Convenience: throw-on-fail read (optional, but handy)
        public static byte[] ReadAllBytesOrThrow(this IVfs vfs, string path)
        {
            if (!vfs.TryReadAllBytes(path, out var bytes, out var err) || bytes is null)
                throw new InvalidOperationException($"VFS read failed: '{path}' ({err ?? "unknown error"})");
            return bytes;
        }
    }
}
