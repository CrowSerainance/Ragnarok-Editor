using System.Collections.Generic;

namespace ROMapOverlayEditor.Sources
{
    /// <summary>
    /// Abstraction for reading files from different sources (GRF archive, filesystem folder).
    /// Provides a unified interface for the editor to access files regardless of origin.
    /// </summary>
    public interface IFileSource
    {
        /// <summary>Display name for UI (e.g., "GRF: data.grf" or "Folder: C:\Client\data").</summary>
        string DisplayName { get; }

        /// <summary>Check if a file exists at the given virtual path (case-insensitive).</summary>
        bool Exists(string virtualPath);

        /// <summary>Read all bytes from a file at the given virtual path.</summary>
        byte[] ReadAllBytes(string virtualPath);

        /// <summary>Enumerate all available file paths in this source.</summary>
        IEnumerable<string> EnumeratePaths();
    }
}
