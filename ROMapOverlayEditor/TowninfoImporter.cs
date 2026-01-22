// ═══════════════════════════════════════════════════════════════════════════════
// FILE: TowninfoImporter.cs
// PURPOSE: Parses Towninfo.lub from RO client to extract NPC locations by map
// ═══════════════════════════════════════════════════════════════════════════════
//
// SEARCH PRIORITY:
// 1. FILESYSTEM FIRST - Checks extracted data\System\ folder (plain text)
// 2. GRF FALLBACK - Only if filesystem doesn't have it (bytecode)
//
// COMMON FILESYSTEM PATHS SEARCHED:
// - {ClientFolder}\data\System\Towninfo.lub
// - {ClientFolder}\data\luafiles514\lua files\signboardlist\Towninfo.lub
// - {ClientFolder}\System\Towninfo.lub
//
// ═══════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ROMapOverlayEditor.Grf;
using ROMapOverlayEditor.Sources;

namespace ROMapOverlayEditor;

/// <summary>
/// Single NPC entry from Towninfo.lub before converting to NpcPlacable.
/// </summary>
public class TownNpcInfo
{
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Type { get; set; }
    public string MapName { get; set; } = "";
}

/// <summary>
/// TYPE values in Towninfo.lub.
/// </summary>
public enum TownNpcType
{
    ToolDealer = 0,
    WeaponDealer = 1,
    ArmorDealer = 2,
    Smith = 3,
    Guide = 4,
    Inn = 5,
    KafraEmployee = 6,
    StylingShop = 7
}

/// <summary>
/// Result of searching for Towninfo.lub
/// </summary>
public class TowninfoSearchResult
{
    public bool Found { get; set; }
    public string FilePath { get; set; } = "";
    public string Content { get; set; } = "";
    public string Source { get; set; } = ""; // "Filesystem" or "GRF"
    public List<string> AvailableMaps { get; set; } = new();
}

/// <summary>
/// Imports NPC data from Towninfo.lub using regex-based parsing.
/// Prioritizes FILESYSTEM over GRF (filesystem = plain text, GRF = bytecode).
/// </summary>
public static class TowninfoImporter
{
    // ═══════════════════════════════════════════════════════════════════════════
    // SEARCH PATHS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Relative paths to search for Towninfo.lub in the filesystem.
    /// These are relative to the client folder.
    /// </summary>
    public static readonly string[] FilesystemSearchPaths = new[]
    {
        @"data\System\Towninfo.lub",
        @"data\System\Towninfo.lua",
        @"data\SystemEN\Towninfo.lub",
        @"data\SystemEN\Towninfo.lua",
        @"data\luafiles514\lua files\signboardlist\Towninfo.lub",
        @"data\luafiles514\lua files\signboardlist\Towninfo.lua",
        @"System\Towninfo.lub",
        @"System\Towninfo.lua",
        @"SystemEN\Towninfo.lub",
        @"SystemEN\Towninfo.lua",
    };
    
    /// <summary>
    /// Paths to search inside GRF archives.
    /// </summary>
    public static readonly string[] GrfSearchPaths = new[]
    {
        @"data\System\Towninfo.lub",
        @"data\luafiles514\lua files\signboardlist\Towninfo.lub",
        @"System\Towninfo.lub",
    };

    // ═══════════════════════════════════════════════════════════════════════════
    // SPRITE AND LABEL MAPPINGS
    // ═══════════════════════════════════════════════════════════════════════════

    public static readonly Dictionary<TownNpcType, string> TypeToSprite = new()
    {
        { TownNpcType.ToolDealer, "4_M_MERCHANT" },
        { TownNpcType.WeaponDealer, "4_M_MERCARMY" },
        { TownNpcType.ArmorDealer, "4_M_ORIENT01" },
        { TownNpcType.Smith, "4_M_ORIENT02" },
        { TownNpcType.Guide, "4_M_SCIENTIST" },
        { TownNpcType.Inn, "4_M_INNKEEPER" },
        { TownNpcType.KafraEmployee, "4_F_KAFRA1" },
        { TownNpcType.StylingShop, "4_F_BEAUTYMASTER" }
    };

    public static readonly Dictionary<TownNpcType, string> TypeToLabel = new()
    {
        { TownNpcType.ToolDealer, "Tool Dealer" },
        { TownNpcType.WeaponDealer, "Weapon Dealer" },
        { TownNpcType.ArmorDealer, "Armor Dealer" },
        { TownNpcType.Smith, "Smith" },
        { TownNpcType.Guide, "Guide" },
        { TownNpcType.Inn, "Inn" },
        { TownNpcType.KafraEmployee, "Kafra Employee" },
        { TownNpcType.StylingShop, "Styling Shop" }
    };
    
    public static readonly Dictionary<TownNpcType, string> TypeToShortLabel = new()
    {
        { TownNpcType.ToolDealer, "Tool" },
        { TownNpcType.WeaponDealer, "Weapon" },
        { TownNpcType.ArmorDealer, "Armor" },
        { TownNpcType.Smith, "Smith" },
        { TownNpcType.Guide, "Guide" },
        { TownNpcType.Inn, "Inn" },
        { TownNpcType.KafraEmployee, "Kafra" },
        { TownNpcType.StylingShop, "Styling" }
    };

    // ═══════════════════════════════════════════════════════════════════════════
    // REGEX PATTERNS
    // ═══════════════════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════════════════
    // REGEX EXPLANATION:
    // Towninfo.lub uses tab-based indentation:
    //   - 0 tabs: mapNPCInfoTable = { (outer table - we skip this)
    //   - 1 tab:  prontera = { ... }, (town blocks - we capture these)
    //   - 2 tabs: { name = ... }, (NPC entries inside towns)
    //
    // Pattern breakdown:
    //   ^\t        - Line starts with exactly 1 tab (matches town names, not mapNPCInfoTable)
    //   (\w+)      - Capture town name (prontera, geffen, etc.)
    //   \s*=\s*\{  - Match " = {"
    //   ([\s\S]*?) - Capture content (non-greedy)
    //   ^\t\}      - Match line starting with 1 tab + "}" (town block close)
    // ═══════════════════════════════════════════════════════════════════════════
    private static readonly Regex MapBlockRegex = new(
        @"^\t(\w+)\s*=\s*\{([\s\S]*?)^\t\}",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Fallback regex: matches any "word = {" pattern for non-standard formats
    /// Less precise but more flexible
    /// </summary>
    private static readonly Regex MapBlockRegexFallback = new(
        @"(?<!\w)(\w+)\s*=\s*\{([^{}]*(?:\{[^{}]*\}[^{}]*)*)\}",
        RegexOptions.Compiled);

    private static readonly Regex NpcEntryRegex = new(
        @"\{\s*name\s*=\s*(?:\[=*\[|""?)([^\]=\]""]+)(?:\]=*\]|""?)[^}]*X\s*=\s*(\d+)[^}]*Y\s*=\s*(\d+)[^}]*TYPE\s*=\s*(\d+)[^}]*\}",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // ═══════════════════════════════════════════════════════════════════════════
    // MAIN SEARCH METHOD - PRIORITIZES FILESYSTEM
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Searches for Towninfo.lub in the following order:
    /// 1. Filesystem (client folder) - plain text files
    /// 2. GRF archive - only if filesystem doesn't have it
    /// </summary>
    public static TowninfoSearchResult SearchForTowninfo(string clientFolder, string? grfPath = null)
    {
        var result = new TowninfoSearchResult();
        
        // STEP 1: Search FILESYSTEM first (plain text, higher priority)
        if (!string.IsNullOrEmpty(clientFolder) && Directory.Exists(clientFolder))
        {
            foreach (var relativePath in FilesystemSearchPaths)
            {
                var fullPath = Path.Combine(clientFolder, relativePath);
                if (File.Exists(fullPath))
                {
                    try
                    {
                        var content = File.ReadAllText(fullPath);
                        
                        if (IsValidTowninfoContent(content))
                        {
                            result.Found = true;
                            result.FilePath = fullPath;
                            result.Content = content;
                            result.Source = "Filesystem";
                            result.AvailableMaps = ParseMapsFromContent(content);
                            return result;
                        }
                    }
                    catch { }
                }
            }
            
            // Recursive search
            var foundFile = SearchRecursive(clientFolder, "Towninfo.lub") 
                         ?? SearchRecursive(clientFolder, "Towninfo.lua");
            
            if (foundFile != null)
            {
                try
                {
                    var content = File.ReadAllText(foundFile);
                    if (IsValidTowninfoContent(content))
                    {
                        result.Found = true;
                        result.FilePath = foundFile;
                        result.Content = content;
                        result.Source = "Filesystem";
                        result.AvailableMaps = ParseMapsFromContent(content);
                        return result;
                    }
                }
                catch { }
            }
        }

        // STEP 2: Fallback to GRF
        if (!string.IsNullOrEmpty(grfPath) && File.Exists(grfPath))
        {
            if (TryExtractFromGrf(grfPath, out var content, out var internalPath))
            {
                result.Found = true;
                result.FilePath = $"{grfPath}:{internalPath}";
                result.Content = content;
                result.Source = "GRF";
                result.AvailableMaps = ParseMapsFromContent(content);
                return result;
            }
        }

        return result;
    }

    private static string? SearchRecursive(string directory, string fileName, int maxDepth = 5)
    {
        if (maxDepth <= 0) return null;
        
        try
        {
            var directMatch = Path.Combine(directory, fileName);
            if (File.Exists(directMatch)) return directMatch;
            
            foreach (var subDir in Directory.GetDirectories(directory))
            {
                var dirName = Path.GetFileName(subDir).ToLowerInvariant();
                if (dirName == "savedata" || dirName == "screenshot" || dirName == "replay") 
                    continue;
                
                var found = SearchRecursive(subDir, fileName, maxDepth - 1);
                if (found != null) return found;
            }
        }
        catch { }
        
        return null;
    }

    /// <summary>
    /// Checks if content looks like valid Towninfo.lub (not bytecode).
    /// </summary>
    private static bool IsValidTowninfoContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        
        // Check for Lua bytecode header (compiled files start with these bytes)
        if (content.Length > 4)
        {
            // LuaQ is the header for Lua 5.1 bytecode
            // \x1bLua is another common header
            if (content.StartsWith("\x1bLua") || 
                content.StartsWith("LuaQ") ||
                (content.Length > 0 && content[0] == 0x1B))
            {
                return false;
            }
        }
        
        // Valid content should contain mapNPCInfoTable
        return content.Contains("mapNPCInfoTable");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GRF EXTRACTION
    // ═══════════════════════════════════════════════════════════════════════════

    private static bool TryExtractFromGrf(string grfPath, out string content, out string foundPath)
    {
        content = "";
        foundPath = "";

        try
        {
            using var grf = GrfArchive.Open(grfPath);

            foreach (var tryPath in GrfSearchPaths)
            {
                var entry = grf.Entries.FirstOrDefault(e =>
                    e.Path.Equals(tryPath, StringComparison.OrdinalIgnoreCase));

                if (entry != null)
                {
                    byte[] bytes = grf.Extract(entry.Path);
                    content = DecodeContent(bytes);
                    foundPath = entry.Path;

                    if (IsValidTowninfoContent(content))
                        return true;
                }
            }

            var anyTowninfo = grf.Entries.FirstOrDefault(e =>
                e.Path.EndsWith("Towninfo.lub", StringComparison.OrdinalIgnoreCase));

            if (anyTowninfo != null)
            {
                byte[] bytes = grf.Extract(anyTowninfo.Path);
                content = DecodeContent(bytes);
                foundPath = anyTowninfo.Path;
                return IsValidTowninfoContent(content);
            }
        }
        catch { }

        return false;
    }

    private static string DecodeContent(byte[] bytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        try
        {
            var utf8 = Encoding.UTF8.GetString(bytes);
            if (!utf8.Contains('\uFFFD')) return utf8;
        }
        catch { }

        try
        {
            return Encoding.GetEncoding(949).GetString(bytes);
        }
        catch { }

        return Encoding.Latin1.GetString(bytes);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PARSING METHODS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Extracts all map names from Towninfo.lub content.
    /// Tries primary regex first, falls back to alternative if no matches.
    /// </summary>
    public static List<string> ParseMapsFromContent(string content)
    {
        var maps = new List<string>();
        
        // ─────────────────────────────────────────────────────────────────────
        // Try primary regex (tab-indented format)
        // ─────────────────────────────────────────────────────────────────────
        foreach (Match match in MapBlockRegex.Matches(content))
        {
            if (match.Groups.Count >= 2)
            {
                var mapName = match.Groups[1].Value.Trim();
                // Skip the outer table name
                if (!string.IsNullOrWhiteSpace(mapName) && 
                    !mapName.Equals("mapNPCInfoTable", StringComparison.OrdinalIgnoreCase))
                {
                    maps.Add(mapName);
                }
            }
        }
        
        // ─────────────────────────────────────────────────────────────────────
        // If primary regex found nothing, try fallback
        // ─────────────────────────────────────────────────────────────────────
        if (maps.Count == 0)
        {
            foreach (Match match in MapBlockRegexFallback.Matches(content))
            {
                if (match.Groups.Count >= 2)
                {
                    var mapName = match.Groups[1].Value.Trim();
                    // Skip the outer table name and function names
                    if (!string.IsNullOrWhiteSpace(mapName) && 
                        !mapName.Equals("mapNPCInfoTable", StringComparison.OrdinalIgnoreCase) &&
                        !mapName.Equals("main", StringComparison.OrdinalIgnoreCase) &&
                        !mapName.Equals("function", StringComparison.OrdinalIgnoreCase) &&
                        !mapName.Equals("if", StringComparison.OrdinalIgnoreCase) &&
                        !mapName.Equals("for", StringComparison.OrdinalIgnoreCase))
                    {
                        maps.Add(mapName);
                    }
                }
            }
        }
        
        // ─────────────────────────────────────────────────────────────────────
        // Last resort: simple line-by-line parsing
        // ─────────────────────────────────────────────────────────────────────
        if (maps.Count == 0)
        {
            // Look for patterns like "townname = {" on their own lines
            var linePattern = new Regex(@"^\s+(\w+)\s*=\s*\{\s*$", RegexOptions.Multiline);
            foreach (Match match in linePattern.Matches(content))
            {
                var mapName = match.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(mapName) &&
                    !mapName.Equals("mapNPCInfoTable", StringComparison.OrdinalIgnoreCase))
                {
                    maps.Add(mapName);
                }
            }
        }
        
        return maps.Distinct().ToList();
    }

    /// <summary>
    /// Extracts NPC entries for a specific map from Towninfo.lub content.
    /// </summary>
    public static List<TownNpcInfo> ParseNpcsFromContent(string content, string mapName)
    {
        var results = new List<TownNpcInfo>();

        // ─────────────────────────────────────────────────────────────────────
        // Try primary regex first (tab-indented)
        // ─────────────────────────────────────────────────────────────────────
        foreach (Match mapMatch in MapBlockRegex.Matches(content))
        {
            var currentMap = mapMatch.Groups[1].Value.Trim();
            if (!currentMap.Equals(mapName, StringComparison.OrdinalIgnoreCase))
                continue;

            var mapContent = mapMatch.Groups[2].Value;
            ExtractNpcsFromBlock(mapContent, mapName, results);
            if (results.Count > 0) return results;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Try fallback regex
        // ─────────────────────────────────────────────────────────────────────
        foreach (Match mapMatch in MapBlockRegexFallback.Matches(content))
        {
            var currentMap = mapMatch.Groups[1].Value.Trim();
            if (!currentMap.Equals(mapName, StringComparison.OrdinalIgnoreCase))
                continue;

            var mapContent = mapMatch.Groups[2].Value;
            ExtractNpcsFromBlock(mapContent, mapName, results);
            if (results.Count > 0) return results;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Last resort: find the map block manually
        // ─────────────────────────────────────────────────────────────────────
        var manualPattern = new Regex(
            $@"(?:^|\n)\s*{Regex.Escape(mapName)}\s*=\s*\{{([\s\S]*?)\n\s*\}},?",
            RegexOptions.IgnoreCase);
        
        var manualMatch = manualPattern.Match(content);
        if (manualMatch.Success)
        {
            ExtractNpcsFromBlock(manualMatch.Groups[1].Value, mapName, results);
        }

        return results;
    }

    /// <summary>
    /// Helper: extracts NPC entries from a town block's content.
    /// </summary>
    private static void ExtractNpcsFromBlock(string blockContent, string mapName, List<TownNpcInfo> results)
    {
        // Pattern for NPC entries: { name = [=[Name]=], X = 123, Y = 456, TYPE = 6 }
        var npcPattern = new Regex(
            @"\{\s*name\s*=\s*(?:\[=*\[|"")([^\]=\]""]+)(?:\]=*\]|"")[^}]*X\s*=\s*(\d+)[^}]*Y\s*=\s*(\d+)[^}]*TYPE\s*=\s*(\d+)",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match npcMatch in npcPattern.Matches(blockContent))
        {
            if (npcMatch.Groups.Count < 5) continue;

            var name = npcMatch.Groups[1].Value.Trim();
            if (!int.TryParse(npcMatch.Groups[2].Value, NumberStyles.Integer,
                              CultureInfo.InvariantCulture, out int x)) continue;
            if (!int.TryParse(npcMatch.Groups[3].Value, NumberStyles.Integer,
                              CultureInfo.InvariantCulture, out int y)) continue;
            if (!int.TryParse(npcMatch.Groups[4].Value, NumberStyles.Integer,
                              CultureInfo.InvariantCulture, out int type)) type = 0;

            results.Add(new TownNpcInfo
            {
                Name = name,
                X = x,
                Y = y,
                Type = type,
                MapName = mapName
            });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // LEGACY METHODS (backward compatibility)
    // ═══════════════════════════════════════════════════════════════════════════

    public static List<string> GetAvailableMaps(string lubFilePath)
    {
        if (!File.Exists(lubFilePath)) return new();
        try
        {
            var content = File.ReadAllText(lubFilePath);
            return ParseMapsFromContent(content);
        }
        catch { return new(); }
    }

    public static List<TownNpcInfo> ImportTownNpcs(string lubFilePath, string mapName)
    {
        if (!File.Exists(lubFilePath)) return new();
        try
        {
            var content = File.ReadAllText(lubFilePath);
            return ParseNpcsFromContent(content, mapName);
        }
        catch { return new(); }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CONVERSION METHODS
    // ═══════════════════════════════════════════════════════════════════════════

    public static NpcPlacable ConvertToPlacable(TownNpcInfo info)
    {
        var npcType = (TownNpcType)Math.Clamp(info.Type, 0, 7);
        var sprite = TypeToSprite.GetValueOrDefault(npcType, "4_M_MERCHANT");
        var label = TypeToLabel.GetValueOrDefault(npcType, "NPC");

        return new NpcPlacable
        {
            MapName = info.MapName,
            X = info.X,
            Y = info.Y,
            Dir = 0,
            Label = label,
            Sprite = sprite,
            ScriptName = SanitizeScriptName(info.Name),
            ScriptBody = GenerateDefaultScript(info.Name, npcType)
        };
    }

    public static List<NpcPlacable> ConvertAllToPlacables(IEnumerable<TownNpcInfo> infos)
    {
        return infos.Select(ConvertToPlacable).ToList();
    }

    private static string SanitizeScriptName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "TownNpc";
        var s = name.Replace(" ", "_");
        s = Regex.Replace(s, @"[^a-zA-Z0-9_]", "");
        if (s.Length > 0 && char.IsDigit(s[0])) s = "_" + s;
        return string.IsNullOrWhiteSpace(s) ? "TownNpc" : s;
    }

    private static string GenerateDefaultScript(string npcName, TownNpcType type)
    {
        return type switch
        {
            TownNpcType.ToolDealer => $"mes \"[{npcName}]\";\nmes \"Welcome! I sell useful items.\";\nclose;",
            TownNpcType.WeaponDealer => $"mes \"[{npcName}]\";\nmes \"Looking for weapons?\";\nclose;",
            TownNpcType.ArmorDealer => $"mes \"[{npcName}]\";\nmes \"I have the finest armor.\";\nclose;",
            TownNpcType.Smith => $"mes \"[{npcName}]\";\nmes \"Need something upgraded?\";\nclose;",
            TownNpcType.Guide => $"mes \"[{npcName}]\";\nmes \"Welcome to this town!\";\nclose;",
            TownNpcType.Inn => $"mes \"[{npcName}]\";\nmes \"Would you like to rest?\";\nclose;",
            TownNpcType.KafraEmployee => $"mes \"[{npcName}]\";\nmes \"Welcome to Kafra Services!\";\nclose;",
            TownNpcType.StylingShop => $"mes \"[{npcName}]\";\nmes \"Want a new hairstyle?\";\nclose;",
            _ => $"mes \"[{npcName}]\";\nmes \"Hello!\";\nclose;"
        };
    }

    /// <summary>
    /// Public method to get default script body given NPC name and type.
    /// Used by MainWindow when loading towns from Towninfo.
    /// </summary>
    public static string GetDefaultScriptBody(string npcName, int typeInt)
    {
        var type = (TownNpcType)Math.Clamp(typeInt, 0, 7);
        return GenerateDefaultScript(npcName, type);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // METHODS USING NEW SOURCE ABSTRACTION
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Extract Towninfo content using GrfFileSource (unified reader).
    /// Returns the decoded content if found and valid.
    /// </summary>
    public static bool TryExtractFromGrfSource(GrfFileSource source, out string content, out string foundPath)
    {
        content = "";
        foundPath = "";

        if (source == null) return false;

        try
        {
            foreach (var tryPath in GrfSearchPaths)
            {
                if (!source.Exists(tryPath)) continue;

                byte[] bytes = source.ReadAllBytes(tryPath);
                content = DecodeContent(bytes);
                foundPath = tryPath;

                if (IsValidTowninfoContent(content))
                    return true;
            }

            // Try finding any Towninfo file
            var anyTowninfo = source.EnumeratePaths()
                .FirstOrDefault(p => p.EndsWith("Towninfo.lub", StringComparison.OrdinalIgnoreCase) ||
                                     p.EndsWith("Towninfo.lua", StringComparison.OrdinalIgnoreCase));

            if (anyTowninfo != null)
            {
                byte[] bytes = source.ReadAllBytes(anyTowninfo);
                content = DecodeContent(bytes);
                foundPath = anyTowninfo;
                return IsValidTowninfoContent(content);
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Search for Towninfo using the CompositeFileSource (unified VFS).
    /// This is the preferred method for new code.
    /// </summary>
    public static TowninfoSearchResult SearchForTowninfoVfs(CompositeFileSource vfs)
    {
        var result = new TowninfoSearchResult();

        if (vfs == null) return result;

        // Check Lua files via the VFS (folder first, then GRF)
        var candidates = new[]
        {
            "data/System/Towninfo.lua",
            "data/System/Towninfo.lub",
            "System/Towninfo.lua",
            "System/Towninfo.lub",
            "Towninfo.lua",
            "Towninfo.lub",
            "data/luafiles514/lua files/signboardlist/Towninfo.lua",
            "data/luafiles514/lua files/signboardlist/Towninfo.lub",
        };

        foreach (var candidate in candidates)
        {
            if (!vfs.ExistsLua(candidate)) continue;

            try
            {
                byte[] bytes = vfs.ReadLua(candidate);
                string content = DecodeContent(bytes);

                if (IsValidTowninfoContent(content))
                {
                    result.Found = true;
                    result.FilePath = candidate;
                    result.Content = content;
                    result.Source = vfs.GetLuaSource(candidate) ?? "Unknown";
                    result.AvailableMaps = ParseMapsFromContent(content);
                    return result;
                }
            }
            catch { }
        }

        return result;
    }
}
