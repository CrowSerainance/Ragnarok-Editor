using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ROMapOverlayEditor.Sources;

namespace ROMapOverlayEditor.GrfTown
{
    /// <summary>
    /// Unified resolver for Towninfo.lua/lub files across multiple sources.
    /// Handles text parsing and bytecode detection.
    /// </summary>
    public static class TowninfoResolver
    {
        /// <summary>Candidate paths to search for Towninfo files.</summary>
        private static readonly string[] Candidates =
        {
            "data/System/Towninfo.lua",
            "data/System/Towninfo.lub",
            "System/Towninfo.lua",
            "System/Towninfo.lub",
            "Towninfo.lua",
            "Towninfo.lub",
            "data/luafiles514/lua files/signboardlist/Towninfo.lua",
            "data/luafiles514/lua files/signboardlist/Towninfo.lub",
            "data/SystemEN/Towninfo.lua",
            "data/SystemEN/Towninfo.lub",
        };

        /// <summary>
        /// Try to load Towninfo text content from the composite source.
        /// Returns parsed text if successful, or an error message explaining why it failed.
        /// </summary>
        public static TowninfoResolveResult TryLoadTowninfoText(CompositeFileSource vfs)
        {
            if (vfs == null)
                return TowninfoResolveResult.Fail("No file sources configured.");

            // Track what we found but couldn't use (for better error messages)
            string? foundBytecode = null;

            // Try each candidate path
            foreach (var candidate in Candidates)
            {
                if (!vfs.ExistsLua(candidate))
                    continue;

                byte[] bytes;
                try
                {
                    bytes = vfs.ReadLua(candidate);
                }
                catch
                {
                    continue;
                }

                // Detect Lua bytecode: 1B 4C 75 61 (ESC 'L' 'u' 'a')
                if (IsLuaBytecode(bytes))
                {
                    foundBytecode ??= candidate;
                    continue; // Skip bytecode, try next candidate
                }

                // Try to decode as text
                var text = DecodeText(bytes);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                // Validate it looks like Towninfo content
                if (!LooksLikeTowninfo(text))
                    continue;

                var source = vfs.GetLuaSource(candidate) ?? candidate;
                return TowninfoResolveResult.Success(candidate, text, source);
            }

            // No valid text file found - provide helpful error message
            if (foundBytecode != null)
            {
                return TowninfoResolveResult.Fail(
                    $"Found {foundBytecode} but it is compiled Lua bytecode (ESC Lua).\n\n" +
                    "Text parsing is not supported for bytecode files.\n\n" +
                    "Solutions:\n" +
                    "1. Use 'Set Lua Folder' to select a folder containing Towninfo.lua (text format)\n" +
                    "2. Decompile the .lub file using a tool like unluac or luadec\n" +
                    "3. Use a GRF that contains Towninfo.lua in text format");
            }

            return TowninfoResolveResult.Fail(
                "Towninfo.lua/lub not found in GRF or selected Lua folder.\n\n" +
                "Use 'Set Lua Folder' to select a folder containing Towninfo.lua or Towninfo.lub (text format).");
        }

        /// <summary>
        /// Load Towninfo and parse into town entries.
        /// </summary>
        public static (List<TownEntry> Towns, string SourcePath, string Warning) LoadTownList(CompositeFileSource vfs)
        {
            var result = TryLoadTowninfoText(vfs);

            if (!result.Ok)
                return (new List<TownEntry>(), "", result.Message);

            var towns = TowninfoParser.ParseTowninfoText(result.Text, result.SourcePath);

            if (towns.Count == 0)
            {
                return (new List<TownEntry>(), result.SourcePath,
                    "Towninfo was found but no towns were parsed.\n" +
                    "The file may use a different structure than expected.");
            }

            return (towns, result.SourcePath, "");
        }

        /// <summary>Check if bytes start with Lua bytecode signature.</summary>
        public static bool IsLuaBytecode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4)
                return false;
            // Lua 5.x bytecode: 1B 4C 75 61 (ESC 'L' 'u' 'a')
            return bytes[0] == 0x1B && bytes[1] == (byte)'L' && bytes[2] == (byte)'u' && bytes[3] == (byte)'a';
        }

        /// <summary>Check if text looks like valid Towninfo content.</summary>
        private static bool LooksLikeTowninfo(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Look for common Towninfo markers
            return text.Contains("mapNPCInfoTable", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("name", StringComparison.OrdinalIgnoreCase) &&
                   text.Contains("TYPE", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Decode bytes to text, trying multiple encodings.</summary>
        private static string DecodeText(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return "";

            // Try UTF-8 first
            try
            {
                var utf8 = Encoding.UTF8.GetString(bytes);
                // Check for replacement characters or nulls that indicate wrong encoding
                if (!utf8.Contains('\uFFFD') && !utf8.Contains('\0'))
                    return utf8;
            }
            catch { }

            // Try EUC-KR (Korean) - common for RO files
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                var eucKr = Encoding.GetEncoding(949).GetString(bytes);
                if (!eucKr.Contains('\0'))
                    return eucKr;
            }
            catch { }

            // Fallback to Latin1
            try
            {
                return Encoding.Latin1.GetString(bytes);
            }
            catch
            {
                return "";
            }
        }
    }

    /// <summary>Result of attempting to resolve and load Towninfo.</summary>
    public sealed class TowninfoResolveResult
    {
        public bool Ok { get; }
        public string SourcePath { get; }
        public string Text { get; }
        public string Message { get; }
        public string SourceDisplay { get; }

        private TowninfoResolveResult(bool ok, string sourcePath, string text, string message, string sourceDisplay)
        {
            Ok = ok;
            SourcePath = sourcePath;
            Text = text;
            Message = message;
            SourceDisplay = sourceDisplay;
        }

        public static TowninfoResolveResult Success(string sourcePath, string text, string sourceDisplay)
            => new(true, sourcePath, text, "", sourceDisplay);

        public static TowninfoResolveResult Fail(string message)
            => new(false, "", "", message, "");
    }
}
