// ═══════════════════════════════════════════════════════════════════════════════
// FILE: TowninfoImporter.cs
// PURPOSE: Parses Towninfo.lub from RO client to extract NPC locations by map
// ═══════════════════════════════════════════════════════════════════════════════
//
// WHAT THIS FILE DOES:
// - Reads Towninfo.lub (a Lua table file) from your RO client
// - Can read from FILESYSTEM or from INSIDE A GRF ARCHIVE
// - Extracts all town NPCs organized by map name
// - Converts the NPC TYPE values (0-7) into recognizable sprite names
// - Returns data that can be added to your project's Items list
//
// TOWNINFO.LUB LOCATION INSIDE GRF:
// - Usually at: data\System\Towninfo.lub
// - Or: data\luafiles514\lua files\signboardlist\Towninfo.lub (older clients)
//
// TOWNINFO.LUB FORMAT:
// mapNPCInfoTable = { 
//   prontera = { { name = [=[Kafra Employee]=], X = 146, Y = 89, TYPE = 6 }, ... }, 
//   geffen = { ... },
//   ...
// }
//
// TYPE VALUES:
// 0 = Tool Dealer, 1 = Weapon Dealer, 2 = Armor Dealer, 3 = Smith
// 4 = Guide, 5 = Inn, 6 = Kafra Employee, 7 = Styling Shop
// ═══════════════════════════════════════════════════════════════════════════════

using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ROMapOverlayEditor.Grf;

namespace ROMapOverlayEditor;

/// <summary>
/// Single NPC entry from Towninfo.lub before converting to NpcPlacable.
/// This is an intermediate format used during the import process.
/// </summary>
public class TownNpcInfo
{
    /// <summary>Display name shown in-game (e.g., "Kafra Employee")</summary>
    public string Name { get; set; } = "";
    
    /// <summary>X coordinate on the map (in tiles)</summary>
    public int X { get; set; }
    
    /// <summary>Y coordinate on the map (in tiles)</summary>
    public int Y { get; set; }
    
    /// <summary>NPC type from 0-7 determining their role and sprite</summary>
    public int Type { get; set; }
    
    /// <summary>The map this NPC belongs to (e.g., "prontera")</summary>
    public string MapName { get; set; } = "";
}

/// <summary>
/// Enum representing the TYPE values found in Towninfo.lub.
/// Each type corresponds to a specific NPC role in the game.
/// </summary>
public enum TownNpcType
{
    ToolDealer = 0,      // Sells consumables like potions
    WeaponDealer = 1,    // Sells weapons
    ArmorDealer = 2,     // Sells armor and shields
    Smith = 3,           // Upgrade/refine services
    Guide = 4,           // Navigation info NPC
    Inn = 5,             // Save point/rest
    KafraEmployee = 6,   // Storage/teleport services
    StylingShop = 7      // Hairstyle changes
}

/// <summary>
/// Imports NPC data from Towninfo.lub using regex-based parsing.
/// Supports reading from both filesystem AND from inside GRF archives.
/// </summary>
public static class TowninfoImporter
{
    // ═══════════════════════════════════════════════════════════════════════════
    // CONSTANTS - Common paths where Towninfo.lub is found inside GRF
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Common internal paths where Towninfo.lub might be located inside a GRF.
    /// The importer will try each path in order until one is found.
    /// </summary>
    public static readonly string[] CommonTowninfoGrfPaths = new[]
    {
        @"data\System\Towninfo.lub",
        @"data\luafiles514\lua files\signboardlist\Towninfo.lub",
        @"data\luafiles514\lua files\navigation\Towninfo.lub",
        @"System\Towninfo.lub"
    };
    
    // ═══════════════════════════════════════════════════════════════════════════
    // SPRITE AND LABEL MAPPINGS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Maps TownNpcType to sprite names used in-game.
    /// These sprites are found in data.grf under data\sprite\npc\.
    /// </summary>
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

    /// <summary>
    /// Maps TownNpcType to human-readable labels for the editor UI.
    /// </summary>
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

    // ═══════════════════════════════════════════════════════════════════════════
    // REGEX PATTERNS FOR PARSING LUA
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Matches a map block in the Lua table: mapname = { ... }
    /// </summary>
    private static readonly Regex MapBlockRegex = new(
        @"(\w+)\s*=\s*\{([\s\S]*?)\}(?=\s*,|\s*\})",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Matches individual NPC entries: { name = [=[Name]=], X = 123, Y = 456, TYPE = 6 }
    /// </summary>
    private static readonly Regex NpcEntryRegex = new(
        @"\{\s*name\s*=\s*(?:\[=\[|""?)([^\]=\]""]+)(?:\]=\]|""?)[^}]*X\s*=\s*(\d+)[^}]*Y\s*=\s*(\d+)[^}]*TYPE\s*=\s*(\d+)[^}]*\}",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // ═══════════════════════════════════════════════════════════════════════════
    // GRF-BASED METHODS (PRIMARY - Use these when reading from GRF)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tries to find and extract Towninfo.lub content from a GRF archive.
    /// Searches common paths where the file might be located.
    /// </summary>
    /// <param name="grfPath">Full path to the .grf file on disk</param>
    /// <param name="content">Output: the text content of Towninfo.lub if found</param>
    /// <param name="foundPath">Output: the internal path where the file was found</param>
    /// <returns>True if Towninfo.lub was found and extracted</returns>
    public static bool TryExtractTowninfoFromGrf(string grfPath, out string content, out string foundPath)
    {
        content = "";
        foundPath = "";

        // Validate the GRF file exists
        if (string.IsNullOrEmpty(grfPath) || !File.Exists(grfPath))
        {
            return false;
        }

        try
        {
            // Open the GRF archive
            using var grf = GrfArchive.Open(grfPath);

            // Try each common path until we find one that exists
            foreach (var tryPath in CommonTowninfoGrfPaths)
            {
                // Check if this path exists in the GRF's entry list
                var entry = grf.Entries.FirstOrDefault(e => 
                    e.Path.Equals(tryPath, StringComparison.OrdinalIgnoreCase));

                if (entry != null)
                {
                    // Found it! Extract the bytes
                    byte[] bytes = grf.Extract(entry.Path);

                    // Compiled Lua chunk; cannot parse with regex text parser
                    if (bytes.Length >= 4 && bytes[0] == 0x1B && bytes[1] == (byte)'L' && bytes[2] == (byte)'u' && bytes[3] == (byte)'a')
                    {
                        content = "";
                        foundPath = entry.Path;
                        return false;
                    }

                    // Convert to string (Lua files are usually UTF-8 or EUC-KR)
                    // Try UTF-8 first, fallback to EUC-KR (codepage 949)
                    content = TryDecodeContent(bytes);
                    foundPath = entry.Path;

                    // Verify it looks like valid Towninfo data
                    if (content.Contains("mapNPCInfoTable"))
                    {
                        return true;
                    }
                }
            }

            // Also try a broader search for any file named Towninfo.lub
            var anyTowninfo = grf.Entries.FirstOrDefault(e =>
                e.Path.EndsWith("Towninfo.lub", StringComparison.OrdinalIgnoreCase));

            if (anyTowninfo != null)
            {
                byte[] bytes = grf.Extract(anyTowninfo.Path);

                // Compiled Lua chunk; cannot parse with regex text parser
                if (bytes.Length >= 4 && bytes[0] == 0x1B && bytes[1] == (byte)'L' && bytes[2] == (byte)'u' && bytes[3] == (byte)'a')
                {
                    content = "";
                    foundPath = anyTowninfo.Path;
                    return false;
                }

                content = TryDecodeContent(bytes);
                foundPath = anyTowninfo.Path;
                return content.Contains("mapNPCInfoTable");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TowninfoImporter] GRF read error: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Gets list of available maps from Towninfo.lub inside a GRF archive.
    /// </summary>
    /// <param name="grfPath">Full path to the .grf file</param>
    /// <returns>List of map names found (e.g., "prontera", "geffen", etc.)</returns>
    public static List<string> GetAvailableMapsFromGrf(string grfPath)
    {
        var maps = new List<string>();

        if (!TryExtractTowninfoFromGrf(grfPath, out string content, out _))
        {
            return maps;
        }

        return ParseMapsFromContent(content);
    }

    /// <summary>
    /// Imports town NPCs for a specific map from Towninfo.lub inside a GRF archive.
    /// </summary>
    /// <param name="grfPath">Full path to the .grf file</param>
    /// <param name="mapName">Name of the map to import (e.g., "prontera")</param>
    /// <returns>List of TownNpcInfo objects for that map</returns>
    public static List<TownNpcInfo> ImportTownNpcsFromGrf(string grfPath, string mapName)
    {
        if (!TryExtractTowninfoFromGrf(grfPath, out string content, out _))
        {
            return new List<TownNpcInfo>();
        }

        return ParseNpcsFromContent(content, mapName);
    }

    /// <summary>
    /// Imports NPCs from GRF and converts directly to NpcPlacable objects.
    /// </summary>
    public static List<NpcPlacable> ImportAsPlacablesFromGrf(string grfPath, string mapName)
    {
        return ConvertAllToPlacables(ImportTownNpcsFromGrf(grfPath, mapName));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // FILESYSTEM-BASED METHODS (Fallback - Use when file is extracted)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gets list of available maps from a Towninfo.lub file on the filesystem.
    /// </summary>
    /// <param name="lubFilePath">Full path to Towninfo.lub file</param>
    /// <returns>List of map names found</returns>
    public static List<string> GetAvailableMaps(string lubFilePath)
    {
        var maps = new List<string>();
        if (!File.Exists(lubFilePath)) return maps;
        
        try
        {
            string content = File.ReadAllText(lubFilePath);
            return ParseMapsFromContent(content);
        }
        catch 
        { 
            return maps; 
        }
    }

    /// <summary>
    /// Imports town NPCs from a Towninfo.lub file on the filesystem.
    /// </summary>
    /// <param name="lubFilePath">Full path to Towninfo.lub file</param>
    /// <param name="mapName">Name of the map to import</param>
    /// <returns>List of TownNpcInfo objects for that map</returns>
    public static List<TownNpcInfo> ImportTownNpcs(string lubFilePath, string mapName)
    {
        var results = new List<TownNpcInfo>();
        if (!File.Exists(lubFilePath) || string.IsNullOrWhiteSpace(mapName)) return results;
        
        try
        {
            string content = File.ReadAllText(lubFilePath);
            return ParseNpcsFromContent(content, mapName);
        }
        catch 
        { 
            return results; 
        }
    }

    /// <summary>
    /// Imports NPCs from filesystem and converts directly to NpcPlacable objects.
    /// </summary>
    public static List<NpcPlacable> ImportAsPlacables(string lubFilePath, string mapName)
    {
        return ConvertAllToPlacables(ImportTownNpcs(lubFilePath, mapName));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SHARED PARSING METHODS (Used by both GRF and Filesystem methods)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parses map names from Towninfo.lub content string.
    /// </summary>
    private static List<string> ParseMapsFromContent(string content)
    {
        var maps = new List<string>();
        
        foreach (Match match in MapBlockRegex.Matches(content))
        {
            if (match.Groups.Count >= 2)
            {
                string mapName = match.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(mapName)) 
                {
                    maps.Add(mapName);
                }
            }
        }
        
        return maps;
    }

    /// <summary>
    /// Parses NPC entries for a specific map from Towninfo.lub content string.
    /// </summary>
    private static List<TownNpcInfo> ParseNpcsFromContent(string content, string mapName)
    {
        var results = new List<TownNpcInfo>();

        foreach (Match mapMatch in MapBlockRegex.Matches(content))
        {
            string currentMap = mapMatch.Groups[1].Value.Trim();
            
            // Skip if not the map we're looking for (case-insensitive)
            if (!currentMap.Equals(mapName, StringComparison.OrdinalIgnoreCase)) 
            {
                continue;
            }

            // Found our map - parse the NPC entries inside
            string mapContent = mapMatch.Groups[2].Value;
            
            foreach (Match npcMatch in NpcEntryRegex.Matches(mapContent))
            {
                if (npcMatch.Groups.Count < 5) continue;

                string name = npcMatch.Groups[1].Value.Trim();
                
                // Parse coordinates
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
            
            // Found and processed our map - done
            break;
        }

        return results;
    }

    /// <summary>
    /// Tries to decode byte content as UTF-8, then falls back to EUC-KR (codepage 949).
    /// RO Lua files can be in either encoding depending on the client version.
    /// </summary>
    private static string TryDecodeContent(byte[] bytes)
    {
        // Register code pages provider for EUC-KR support
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // Try UTF-8 first (most common for modern/translated files)
        try
        {
            string utf8 = Encoding.UTF8.GetString(bytes);
            
            // Quick validation - if it decodes cleanly and has expected content
            if (!utf8.Contains('\uFFFD') && utf8.Contains("mapNPCInfoTable"))
            {
                return utf8;
            }
        }
        catch { /* Fall through */ }

        // Try EUC-KR (codepage 949) for Korean clients
        try
        {
            var eucKr = Encoding.GetEncoding(949);
            return eucKr.GetString(bytes);
        }
        catch { /* Fall through */ }

        // Last resort - ASCII/Latin1
        return Encoding.Latin1.GetString(bytes);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CONVERSION METHODS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Converts a TownNpcInfo into an NpcPlacable for the editor.
    /// </summary>
    public static NpcPlacable ConvertToPlacable(TownNpcInfo info)
    {
        var npcType = (TownNpcType)Math.Clamp(info.Type, 0, 7);
        string sprite = TypeToSprite.GetValueOrDefault(npcType, "4_M_MERCHANT");
        string label = TypeToLabel.GetValueOrDefault(npcType, "NPC");
        
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

    /// <summary>
    /// Batch converts multiple TownNpcInfo objects to NpcPlacables.
    /// </summary>
    public static List<NpcPlacable> ConvertAllToPlacables(IEnumerable<TownNpcInfo> infos)
    {
        var results = new List<NpcPlacable>();
        foreach (var info in infos) results.Add(ConvertToPlacable(info));
        return results;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HELPER METHODS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sanitizes NPC name into a valid rAthena script name.
    /// </summary>
    private static string SanitizeScriptName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "TownNpc";
        string s = name.Replace(" ", "_");
        s = Regex.Replace(s, @"[^a-zA-Z0-9_]", "");
        if (s.Length > 0 && char.IsDigit(s[0])) s = "_" + s;
        return string.IsNullOrWhiteSpace(s) ? "TownNpc" : s;
    }

    /// <summary>Returns a placeholder script body for a town NPC by type (0–7).</summary>
    public static string GetDefaultScriptBody(string npcName, int type)
    {
        var t = (TownNpcType)Math.Clamp(type, 0, 7);
        return GenerateDefaultScript(npcName, t);
    }

    /// <summary>
    /// Generates placeholder script body based on NPC type.
    /// </summary>
    private static string GenerateDefaultScript(string npcName, TownNpcType type)
    {
        return type switch
        {
            TownNpcType.ToolDealer => $"mes \"[{npcName}]\";\nmes \"Welcome! I sell useful items for adventurers.\";\nclose;",
            TownNpcType.WeaponDealer => $"mes \"[{npcName}]\";\nmes \"Looking for weapons? You've come to the right place!\";\nclose;",
            TownNpcType.ArmorDealer => $"mes \"[{npcName}]\";\nmes \"I have the finest armor in town.\";\nclose;",
            TownNpcType.Smith => $"mes \"[{npcName}]\";\nmes \"Need something upgraded or refined?\";\nclose;",
            TownNpcType.Guide => $"mes \"[{npcName}]\";\nmes \"Welcome to this town! How may I help you?\";\nclose;",
            TownNpcType.Inn => $"mes \"[{npcName}]\";\nmes \"Would you like to rest here?\";\nclose;",
            TownNpcType.KafraEmployee => $"mes \"[{npcName}]\";\nmes \"Welcome to Kafra Services!\";\nmes \"How may I assist you today?\";\nclose;",
            TownNpcType.StylingShop => $"mes \"[{npcName}]\";\nmes \"Want to change your hairstyle?\";\nclose;",
            _ => $"mes \"[{npcName}]\";\nmes \"Hello!\";\nclose;"
        };
    }
}
