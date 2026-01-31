using System.Collections.Generic;

namespace ROMapOverlayEditor.Vfs
{
    public interface IAssetSource
    {
        string DisplayName { get; }
        int Priority { get; } // higher wins
        IEnumerable<string> EnumeratePaths(); // normalized preferred
        bool Contains(string virtualPath);
        bool TryReadAllBytes(string virtualPath, out byte[]? bytes, out string? error);
    }
}
