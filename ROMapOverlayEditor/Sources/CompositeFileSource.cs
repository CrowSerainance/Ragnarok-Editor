using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ROMapOverlayEditor.Sources
{
    /// <summary>
    /// Combines GRF and Folder sources with configurable priority.
    /// - For Lua/Lub files: Folder first, then GRF (allows overriding GRF with local files)
    /// - For BMP/images: GRF first, then Folder (GRF is the authoritative source for game assets)
    /// </summary>
    public sealed class CompositeFileSource
    {
        public GrfFileSource? Grf { get; }
        public FolderFileSource? Folder { get; }

        public CompositeFileSource(GrfFileSource? grf, FolderFileSource? folder)
        {
            Grf = grf;
            Folder = folder;
        }

        /// <summary>Check if a Lua/Lub file exists (Folder first, then GRF).</summary>
        public bool ExistsLua(string path)
        {
            if (Folder != null && Folder.Exists(path)) return true;
            if (Grf != null && Grf.Exists(path)) return true;
            return false;
        }

        /// <summary>Check if a BMP/image file exists (GRF first, then Folder).</summary>
        public bool ExistsBmp(string path)
        {
            if (Grf != null && Grf.Exists(path)) return true;
            if (Folder != null && Folder.Exists(path)) return true;
            return false;
        }

        /// <summary>Check if any file exists (tries both sources).</summary>
        public bool Exists(string path)
        {
            if (IsLuaLike(path)) return ExistsLua(path);
            if (IsBmpLike(path)) return ExistsBmp(path);
            // Default: GRF first
            if (Grf != null && Grf.Exists(path)) return true;
            if (Folder != null && Folder.Exists(path)) return true;
            return false;
        }

        /// <summary>Read a Lua/Lub file (Folder first, then GRF).</summary>
        public byte[] ReadLua(string path)
        {
            if (Folder != null && Folder.Exists(path))
                return Folder.ReadAllBytes(path);
            if (Grf != null && Grf.Exists(path))
                return Grf.ReadAllBytes(path);
            throw new FileNotFoundException($"Lua file not found in folder or GRF: {path}");
        }

        /// <summary>Read a BMP/image file (GRF first, then Folder).</summary>
        public byte[] ReadBmp(string path)
        {
            if (Grf != null && Grf.Exists(path))
                return Grf.ReadAllBytes(path);
            if (Folder != null && Folder.Exists(path))
                return Folder.ReadAllBytes(path);
            throw new FileNotFoundException($"Image file not found in GRF or folder: {path}");
        }

        /// <summary>Read any file (uses appropriate priority based on extension).</summary>
        public byte[] ReadAllBytes(string path)
        {
            if (IsLuaLike(path)) return ReadLua(path);
            if (IsBmpLike(path)) return ReadBmp(path);
            // Default: GRF first
            if (Grf != null && Grf.Exists(path))
                return Grf.ReadAllBytes(path);
            if (Folder != null && Folder.Exists(path))
                return Folder.ReadAllBytes(path);
            throw new FileNotFoundException($"File not found: {path}");
        }

        /// <summary>Get the source name where a Lua file was found.</summary>
        public string? GetLuaSource(string path)
        {
            if (Folder != null && Folder.Exists(path))
                return Folder.DisplayName;
            if (Grf != null && Grf.Exists(path))
                return Grf.DisplayName;
            return null;
        }

        /// <summary>Enumerate all Lua/Lub files from both sources (folder files override GRF).</summary>
        public IEnumerable<string> EnumerateLuaFiles()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Folder first (higher priority)
            if (Folder != null)
            {
                foreach (var p in Folder.FindByExtension(".lua").Concat(Folder.FindByExtension(".lub")))
                {
                    seen.Add(p);
                    yield return p;
                }
            }

            // GRF (only files not already seen from folder)
            if (Grf != null)
            {
                foreach (var p in Grf.FindByExtension(".lua").Concat(Grf.FindByExtension(".lub")))
                {
                    if (!seen.Contains(p))
                        yield return p;
                }
            }
        }

        /// <summary>Enumerate all BMP files from both sources (GRF files are primary).</summary>
        public IEnumerable<string> EnumerateBmpFiles()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // GRF first (higher priority for images)
            if (Grf != null)
            {
                foreach (var p in Grf.FindByExtension(".bmp"))
                {
                    seen.Add(p);
                    yield return p;
                }
            }

            // Folder (only files not already seen from GRF)
            if (Folder != null)
            {
                foreach (var p in Folder.FindByExtension(".bmp"))
                {
                    if (!seen.Contains(p))
                        yield return p;
                }
            }
        }

        public static bool IsLuaLike(string path)
        {
            var ext = Path.GetExtension(path);
            return ext.Equals(".lua", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".lub", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsBmpLike(string path)
        {
            var ext = Path.GetExtension(path);
            return ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".tga", StringComparison.OrdinalIgnoreCase);
        }
    }
}
