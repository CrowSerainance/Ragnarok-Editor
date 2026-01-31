using System.Collections.Generic;
using ROMapOverlayEditor.Vfs;

namespace ROMapOverlayEditor.Patching
{
    /// <summary>
    /// Central place to collect modified files (gat/lua/etc) before exporting a patch.
    /// </summary>
    public sealed class EditStaging
    {
        private readonly Dictionary<string, byte[]> _files = new(System.StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, byte[]> Files => _files;

        public void Put(string virtualPath, byte[] bytes)
        {
            _files[VPath.Norm(virtualPath)] = bytes;
        }

        public bool TryGet(string virtualPath, out byte[]? bytes)
        {
            if (_files.TryGetValue(VPath.Norm(virtualPath), out var b))
            {
                bytes = b;
                return true;
            }
            bytes = null;
            return false;
        }

        public void Clear() => _files.Clear();
    }
}
