// ═══════════════════════════════════════════════════════════════════════════════
// FILE: TownExporter.cs
// PURPOSE: Exports town NPC data in copy-paste format
// ═══════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ROMapOverlayEditor;

/// <summary>
/// Exports town NPC data in various formats for copy-paste use.
/// </summary>
public static class TownExporter
{
    /// <summary>
    /// Generates the complete export text for clipboard copy.
    /// </summary>
    public static string GenerateFullExport(
        string mapName,
        List<NpcPlacable> currentNpcs,
        List<TownNpcInfo>? originalNpcs,
        string sourcePath)
    {
        var sb = new StringBuilder();
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        
        // HEADER
        sb.AppendLine(new string('=', 80));
        sb.AppendLine($"TOWN: {mapName} ({currentNpcs.Count} NPCs)");
        sb.AppendLine($"SOURCE: {sourcePath}");
        sb.AppendLine($"EXPORTED: {timestamp}");
        sb.AppendLine(new string('=', 80));
        sb.AppendLine();

        // NPC TABLE
        sb.AppendLine("## NPC TABLE");
        sb.AppendLine();
        sb.AppendLine("| # | Name | X | Y | Type | Sprite |");
        sb.AppendLine("|---|------|---|---|------|--------|");
        
        int index = 1;
        foreach (var npc in currentNpcs.OrderBy(n => n.X).ThenBy(n => n.Y))
        {
            var typeNum = GetTypeFromSprite(npc.Sprite);
            var typeLabel = TowninfoImporter.TypeToShortLabel.GetValueOrDefault((TownNpcType)typeNum, "NPC");
            sb.AppendLine($"| {index} | {npc.ScriptName} | {npc.X} | {npc.Y} | {typeNum} ({typeLabel}) | {npc.Sprite} |");
            index++;
        }
        sb.AppendLine();

        // RATHENA SCRIPT FORMAT
        sb.AppendLine(new string('=', 80));
        sb.AppendLine("## RATHENA SCRIPT FORMAT (copy to npc/custom/)");
        sb.AppendLine(new string('=', 80));
        sb.AppendLine();
        
        var nameGroups = currentNpcs.GroupBy(n => n.ScriptName).ToDictionary(g => g.Key, g => g.ToList());
        var nameCounts = new Dictionary<string, int>();
        
        foreach (var npc in currentNpcs.OrderBy(n => n.X).ThenBy(n => n.Y))
        {
            var baseName = npc.ScriptName;
            var displayName = baseName;
            
            if (nameGroups[baseName].Count > 1)
            {
                nameCounts.TryGetValue(baseName, out int count);
                count++;
                nameCounts[baseName] = count;
                displayName = $"{baseName}#{count}";
            }
            
            sb.AppendLine($"{mapName},{npc.X},{npc.Y},{npc.Dir}\tscript\t{displayName}\t{npc.Sprite},{{");
            
            if (!string.IsNullOrWhiteSpace(npc.ScriptBody))
            {
                foreach (var line in npc.ScriptBody.Split('\n'))
                {
                    sb.AppendLine($"\t{line.TrimEnd('\r')}");
                }
            }
            else
            {
                sb.AppendLine($"\tmes \"[{npc.ScriptName}]\";");
                sb.AppendLine($"\tmes \"Hello!\";");
                sb.AppendLine($"\tclose;");
            }
            
            sb.AppendLine("}");
            sb.AppendLine();
        }

        // LUA FORMAT
        sb.AppendLine(new string('=', 80));
        sb.AppendLine("## LUA FORMAT (for Towninfo.lub)");
        sb.AppendLine(new string('=', 80));
        sb.AppendLine();
        sb.AppendLine($"\t{mapName} = {{");
        
        foreach (var npc in currentNpcs.OrderBy(n => n.X).ThenBy(n => n.Y))
        {
            var typeNum = GetTypeFromSprite(npc.Sprite);
            sb.AppendLine($"\t\t{{ name = [=[{npc.ScriptName}]=], X = {npc.X}, Y = {npc.Y}, TYPE = {typeNum} }},");
        }
        
        sb.AppendLine("\t},");
        sb.AppendLine();

        // CHANGELOG
        if (originalNpcs != null && originalNpcs.Count > 0)
        {
            sb.AppendLine(new string('=', 80));
            sb.AppendLine("## CHANGES FROM ORIGINAL");
            sb.AppendLine(new string('=', 80));
            sb.AppendLine();
            
            var changelog = GenerateChangelog(mapName, originalNpcs, currentNpcs);
            sb.AppendLine(changelog);
        }

        sb.AppendLine(new string('=', 80));
        
        return sb.ToString();
    }

    /// <summary>
    /// Generates just the rAthena script format.
    /// </summary>
    public static string GenerateRathenaFormat(string mapName, List<NpcPlacable> npcs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// {mapName} NPCs - Generated {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"// Total: {npcs.Count} NPCs");
        sb.AppendLine();
        
        var nameGroups = npcs.GroupBy(n => n.ScriptName).ToDictionary(g => g.Key, g => g.ToList());
        var nameCounts = new Dictionary<string, int>();
        
        foreach (var npc in npcs.OrderBy(n => n.X).ThenBy(n => n.Y))
        {
            var baseName = npc.ScriptName;
            var displayName = baseName;
            
            if (nameGroups[baseName].Count > 1)
            {
                nameCounts.TryGetValue(baseName, out int count);
                count++;
                nameCounts[baseName] = count;
                displayName = $"{baseName}#{count}";
            }
            
            sb.AppendLine($"{mapName},{npc.X},{npc.Y},{npc.Dir}\tscript\t{displayName}\t{npc.Sprite},{{");
            
            if (!string.IsNullOrWhiteSpace(npc.ScriptBody))
            {
                foreach (var line in npc.ScriptBody.Split('\n'))
                    sb.AppendLine($"\t{line.TrimEnd('\r')}");
            }
            else
            {
                sb.AppendLine($"\tmes \"[{npc.ScriptName}]\";");
                sb.AppendLine($"\tmes \"Hello!\";");
                sb.AppendLine($"\tclose;");
            }
            
            sb.AppendLine("}");
            sb.AppendLine();
        }
        
        return sb.ToString();
    }

    /// <summary>
    /// Generates just the Lua format.
    /// </summary>
    public static string GenerateLuaFormat(string mapName, List<NpcPlacable> npcs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"-- {mapName} NPCs - Generated {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"-- Total: {npcs.Count} NPCs");
        sb.AppendLine();
        sb.AppendLine($"\t{mapName} = {{");
        
        foreach (var npc in npcs.OrderBy(n => n.X).ThenBy(n => n.Y))
        {
            var typeNum = GetTypeFromSprite(npc.Sprite);
            sb.AppendLine($"\t\t{{ name = [=[{npc.ScriptName}]=], X = {npc.X}, Y = {npc.Y}, TYPE = {typeNum} }},");
        }
        
        sb.AppendLine("\t},");
        
        return sb.ToString();
    }

    /// <summary>
    /// Generates a changelog comparing original and modified NPCs.
    /// </summary>
    public static string GenerateChangelog(string mapName, List<TownNpcInfo> original, List<NpcPlacable> modified)
    {
        var sb = new StringBuilder();
        
        var added = new List<string>();
        var moved = new List<string>();
        var removed = new List<string>();
        
        foreach (var mod in modified)
        {
            var modType = GetTypeFromSprite(mod.Sprite);
            var exactMatch = original.FirstOrDefault(o => 
                o.X == mod.X && o.Y == mod.Y && o.Type == modType);
            
            if (exactMatch == null)
            {
                var nameMatch = original.FirstOrDefault(o => 
                    o.Name == mod.ScriptName && o.Type == modType);
                
                if (nameMatch != null)
                {
                    moved.Add($"  ~ {mod.ScriptName}: ({nameMatch.X}, {nameMatch.Y}) -> ({mod.X}, {mod.Y})");
                }
                else
                {
                    added.Add($"  + {mod.ScriptName} at ({mod.X}, {mod.Y}) - Type {modType}");
                }
            }
        }
        
        foreach (var orig in original)
        {
            var stillExists = modified.Any(m => 
                m.X == orig.X && m.Y == orig.Y && GetTypeFromSprite(m.Sprite) == orig.Type);
            
            if (!stillExists)
            {
                var wasRelocated = moved.Any(m => m.Contains(orig.Name));
                if (!wasRelocated)
                {
                    removed.Add($"  - {orig.Name} at ({orig.X}, {orig.Y})");
                }
            }
        }
        
        if (added.Count == 0 && moved.Count == 0 && removed.Count == 0)
        {
            sb.AppendLine("No changes from original.");
        }
        else
        {
            if (added.Count > 0)
            {
                sb.AppendLine("ADDED:");
                foreach (var a in added) sb.AppendLine(a);
                sb.AppendLine();
            }
            
            if (moved.Count > 0)
            {
                sb.AppendLine("MOVED:");
                foreach (var m in moved) sb.AppendLine(m);
                sb.AppendLine();
            }
            
            if (removed.Count > 0)
            {
                sb.AppendLine("REMOVED:");
                foreach (var r in removed) sb.AppendLine(r);
                sb.AppendLine();
            }
        }
        
        return sb.ToString();
    }

    /// <summary>
    /// Reverse lookup: sprite name to TYPE number.
    /// </summary>
    public static int GetTypeFromSprite(string sprite)
    {
        foreach (var kvp in TowninfoImporter.TypeToSprite)
        {
            if (kvp.Value.Equals(sprite, StringComparison.OrdinalIgnoreCase))
                return (int)kvp.Key;
        }
        return 0;
    }
}
