using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ROMapOverlayEditor.GrfTown
{
    public sealed class GrfTownWorkspace
    {
        private readonly Func<string, bool> _exists;
        private readonly Func<string, byte[]> _readBytes;
        private readonly Func<IEnumerable<string>> _listPaths;
        private readonly string? _luaDataFolderPath;

        /// <summary>Relative paths to try under the Lua folder root.</summary>
        public static readonly string[] FolderTowninfoCandidates = new[]
        {
            "Towninfo.lua",
            "Towninfo.lub",
            @"System\Towninfo.lua",
            @"System\Towninfo.lub",
            @"data\System\Towninfo.lua",
            @"data\System\Towninfo.lub"
        };

        /// <summary>Paths to try inside GRF.</summary>
        private static readonly string[] GrfTowninfoCandidates = new[]
        {
            @"data\System\Towninfo.lua",
            @"data\System\Towninfo.lub",
            @"System\Towninfo.lua",
            @"System\Towninfo.lub",
            @"data\system\towninfo.lua",
            @"data\system\towninfo.lub"
        };

        public string GrfDisplayName { get; }

        public GrfTownWorkspace(
            string grfDisplayName,
            Func<IEnumerable<string>> listPaths,
            Func<string, bool> existsInGrf,
            Func<string, byte[]> readBytesFromGrf,
            string? luaDataFolderPath = null)
        {
            GrfDisplayName = grfDisplayName;
            _listPaths = listPaths;
            _exists = existsInGrf;
            _readBytes = readBytesFromGrf;
            _luaDataFolderPath = string.IsNullOrWhiteSpace(luaDataFolderPath) ? null : luaDataFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        /// <summary>Try to load Towninfo from the Lua folder first, then GRF. SourcePath is set to "Folder: {path}" or GRF internal path.</summary>
        public (List<TownEntry> Towns, string SourcePath, string Warning) LoadTownList()
        {
            // 1) Folder first (if set)
            if (!string.IsNullOrEmpty(_luaDataFolderPath) && Directory.Exists(_luaDataFolderPath))
            {
                bool foundAnyInFolder = false;
                foreach (var rel in FolderTowninfoCandidates)
                {
                    var fullPath = Path.Combine(_luaDataFolderPath, rel);
                    if (!File.Exists(fullPath)) continue;
                    foundAnyInFolder = true;

                    byte[] bytes;
                    try { bytes = File.ReadAllBytes(fullPath); } catch { continue; }

                    if (bytes.Length >= 4 && bytes[0] == 0x1B && bytes[1] == (byte)'L' && bytes[2] == (byte)'u' && bytes[3] == (byte)'a')
                        continue; // bytecode in folder: skip and try next

                    string text = DecodeText(bytes);
                    var towns = TowninfoParser.ParseTowninfoText(text, fullPath);
                    if (towns.Count > 0)
                        return (towns, "Folder: " + fullPath, "");
                }
                if (foundAnyInFolder)
                    return (new List<TownEntry>(), "",
                        "No usable Towninfo.lua/lub found in the selected Lua folder.\n\n" +
                        "Files were bytecode or did not parse. Use Towninfo.lua (text) or decompiled Towninfo.lub.");
                // Folder set but no candidate files at all → fall through to GRF
            }

            // 2) GRF
            string? found = GrfTowninfoCandidates.FirstOrDefault(p => _exists(p));
            if (found == null)
            {
                found = _listPaths()
                    .FirstOrDefault(p => p.EndsWith("Towninfo.lua", StringComparison.OrdinalIgnoreCase) ||
                                         p.EndsWith("Towninfo.lub", StringComparison.OrdinalIgnoreCase));
            }

            if (found == null)
                return (new List<TownEntry>(), "",
                    "Towninfo.lua/lub not found in this GRF.\n\n" +
                    "Select a Lua folder (Set Lua Folder) that contains Towninfo.lua or Towninfo.lub, or use a GRF that includes Towninfo.lua (text).\n\n" +
                    "Note: Many clients store Towninfo.lub as compiled Lua bytecode, which this text parser cannot read.");

            var grfBytes = _readBytes(found);

            if (grfBytes.Length >= 4 && grfBytes[0] == 0x1B && grfBytes[1] == (byte)'L' && grfBytes[2] == (byte)'u' && grfBytes[3] == (byte)'a')
            {
                return (new List<TownEntry>(), found,
                    "Towninfo.lub in GRF is compiled Lua bytecode. Text parsing is not possible yet.\n\n" +
                    "Use Set Lua Folder and choose a folder that contains Towninfo.lua (text) or decompiled Towninfo.lub.");
            }

            string grfText = DecodeText(grfBytes);
            var grfTowns = TowninfoParser.ParseTowninfoText(grfText, found);
            if (grfTowns.Count == 0)
            {
                return (new List<TownEntry>(), found,
                    "Towninfo was found but no towns were parsed.\n" +
                    "This usually means the file uses a different structure than the simple parser expects.");
            }

            return (grfTowns, found, "");
        }

        public TownLoadResult LoadTown(string townName, string towninfoSourcePath, List<TownEntry> towns)
        {
            var t = towns.FirstOrDefault(x => string.Equals(x.Name, townName, StringComparison.OrdinalIgnoreCase));
            if (t == null) return TownLoadResult.Fail($"Town '{townName}' not found.");

            // Map image path patterns (client dependent)
            var imgCandidates = new[]
            {
                $@"data\texture\effect\map\{t.Name}.bmp",
                $@"data\texture\À¯ÀúÀÎÅÍÆäÀÌ½º\map\{t.Name}.bmp",
                $@"data\texture\userinterface\map\{t.Name}.bmp",
                $@"data\texture\map\{t.Name}.bmp",
                $@"texture\À¯ÀúÀÎÅÍÆäÀÌ½º\map\{t.Name}.bmp",
                $@"texture\userinterface\map\{t.Name}.bmp"
            };

            string? imgPath = imgCandidates.FirstOrDefault(p => _exists(p));
            if (imgPath == null)
            {
                // We still return town NPCs; image can be missing.
                return TownLoadResult.Success(t);
            }

            // Attach image path into SourcePath if you want; or let UI call readBytes(imgPath)
            t.SourcePath = towninfoSourcePath + " | " + imgPath;
            return TownLoadResult.Success(t);
        }

        public byte[] Read(string grfPath) => _readBytes(grfPath);

        private static string DecodeText(byte[] bytes)
        {
            // Common encodings: UTF-8 / ANSI / EUC-KR
            // We try UTF-8 first; if it looks garbled, fall back to default ANSI.
            try
            {
                var utf8 = Encoding.UTF8.GetString(bytes);
                if (utf8.Contains("\0")) throw new Exception("Nulls present.");
                return utf8;
            }
            catch
            {
                return Encoding.Default.GetString(bytes);
            }
        }
    }
}
