using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using RoDbEditor.Models;

namespace RoDbEditor.Services;

/// <summary>
/// Writes and updates custom item entries in the client's itemInfo_C.lua (tbl_custom)
/// so the client can display item names and descriptions instead of "Unknown Item".
/// </summary>
public class ItemInfoLuaWriter
{
    private readonly string _clientRoot;

    public ItemInfoLuaWriter(string clientRoot)
    {
        _clientRoot = clientRoot ?? "";
    }

    public string GetItemInfoPath()
    {
        var systemEn = Path.Combine(_clientRoot, "SystemEN");
        if (Directory.Exists(systemEn))
            return Path.Combine(systemEn, "itemInfo_C.lua");

        return Path.Combine(_clientRoot, "System", "itemInfo_C.lua");
    }

    /// <summary>Write or update a custom item entry in itemInfo_C.lua.</summary>
    public void WriteEntry(ItemEntry item)
    {
        var path = GetItemInfoPath();
        if (string.IsNullOrWhiteSpace(_clientRoot) || !Directory.Exists(_clientRoot))
            throw new InvalidOperationException("Client root is not set or does not exist.");

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var content = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
        var newContent = UpsertEntryInContent(content, item);

        // Post-write sanity: catch Lua syntax problems BEFORE saving to disk
        var syntaxIssue = CheckLuaSyntaxSanity(newContent);
        if (syntaxIssue != null)
            throw new InvalidOperationException(
                $"[ItemInfoLuaWriter] BUG: Generated Lua has syntax issue: {syntaxIssue}. File NOT saved to prevent client breakage.");

        File.WriteAllText(path, newContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Remove an entry by item ID (for undo).</summary>
    public bool RemoveEntry(int itemId)
    {
        var path = GetItemInfoPath();
        if (!File.Exists(path))
            return true;

        var content = File.ReadAllText(path, Encoding.UTF8);
        var newContent = RemoveEntryFromContent(content, itemId);
        if (newContent == null)
            return false; // no change or parse failed
        File.WriteAllText(path, newContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return true;
    }

    private string UpsertEntryInContent(string content, ItemEntry item)
    {
        // Fix leading comma bug: tbl_custom = { , [id] -> tbl_custom = { [id]
        content = Regex.Replace(content, @"(tbl_custom\s*=\s*\{\s*)\s*\r?\n\s*,",
            "$1" + Environment.NewLine, RegexOptions.Multiline);
        var block = BuildLuaEntry(item);
        var idStr = item.Id.ToString();
        var marker = $"[{idStr}] = {{";

        var existingStart = content.IndexOf(marker, StringComparison.Ordinal);
        if (existingStart >= 0)
        {
            var blockEnd = FindMatchingBlockEnd(content, existingStart + marker.Length - 1);
            if (blockEnd >= 0)
                return content.Remove(existingStart, blockEnd - existingStart).Insert(existingStart, block);
        }

        var tblMatch = Regex.Match(content, @"tbl_custom\s*=\s*\{");
        if (!tblMatch.Success)
        {
            var template = string.IsNullOrWhiteSpace(content)
                ? "-- Custom items (RoDbEditor)\ntbl_custom = {\n}\n"
                : content.TrimEnd() + "\n";
            if (!template.Contains("tbl_custom"))
                template = "-- Custom items (RoDbEditor)\ntbl_custom = {\n}\n" + template;
            content = template;
            tblMatch = Regex.Match(content, @"tbl_custom\s*=\s*\{");
        }

        var insertPos = FindTblCustomClosingBrace(content, tblMatch.Index);
        if (insertPos < 0)
            insertPos = content.Length;
        // Check if the content before the closing brace already ends with a comma
        // (BuildLuaEntry produces entries ending with "},") — avoid inserting a duplicate comma.
        var beforeInsert = content[..insertPos].TrimEnd();
        var separator = beforeInsert.EndsWith(",") || !Regex.IsMatch(content[tblMatch.Index..insertPos], @"\[\d+\]\s*=\s*\{")
            ? "\n"
            : ",\n";
        return content.Insert(insertPos, separator + block.TrimEnd());
    }

    private static string? RemoveEntryFromContent(string content, int itemId)
    {
        var idStr = itemId.ToString();
        var marker = $"[{idStr}] = {{";
        var start = content.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return null;
        var blockEnd = FindMatchingBlockEnd(content, start + marker.Length - 1);
        if (blockEnd < 0)
            return null;
        var toRemove = content.Substring(start, blockEnd - start);
        var trimmed = toRemove.TrimEnd();
        if (trimmed.EndsWith(","))
            toRemove = trimmed;
        var newContent = content.Remove(start, toRemove.Length).TrimEnd();
        if (newContent.EndsWith(",,"))
            newContent = newContent.TrimEnd(',').TrimEnd();
        return newContent;
    }

    private static int FindMatchingBlockEnd(string content, int openBraceIndex)
    {
        int depth = 1;
        for (int i = openBraceIndex + 1; i < content.Length; i++)
        {
            var c = content[i];
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    var j = i + 1;
                    while (j < content.Length && (content[j] == ',' || char.IsWhiteSpace(content[j])))
                        j++;
                    return j;
                }
            }
        }
        return -1;
    }

    private static int FindTblCustomClosingBrace(string content, int tblStart)
    {
        int depth = 0;
        for (int i = tblStart; i < content.Length; i++)
        {
            var c = content[i];
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }
        return -1;
    }

    private string BuildLuaEntry(ItemEntry item)
    {
        var resourceName = string.IsNullOrEmpty(item.ResourceName)
            ? (string.IsNullOrEmpty(item.AegisName) ? "Custom_Item" : item.AegisName)
            : item.ResourceName;
        var displayName = string.IsNullOrEmpty(item.Name) ? item.AegisName : item.Name;
        if (string.IsNullOrEmpty(displayName))
            displayName = "Custom Item";

        var descLines = BuildDescriptionLines(item);
        var descLua = new StringBuilder();
        foreach (var line in descLines)
            descLua.Append("\n\t\t\t\"").Append(EscapeLuaString(line)).Append("\",");

        var slotCount = item.Slots ?? 0;
        var classNum = item.View ?? 0;

        var sb = new StringBuilder();
        sb.AppendLine($"\t[{item.Id}] = {{");
        sb.AppendLine("\t\tunidentifiedDisplayName = \"Unknown Item\",");
        sb.AppendLine($"\t\tunidentifiedResourceName = \"{EscapeLuaString(resourceName)}\",");
        sb.AppendLine("\t\tunidentifiedDescriptionName = { \"\" },");
        sb.AppendLine($"\t\tidentifiedDisplayName = \"{EscapeLuaString(displayName)}\",");
        sb.AppendLine($"\t\tidentifiedResourceName = \"{EscapeLuaString(resourceName)}\",");
        sb.AppendLine("\t\tidentifiedDescriptionName = {");
        sb.Append(descLua.ToString().TrimEnd(','));
        sb.AppendLine("\n\t\t},");
        sb.AppendLine($"\t\tslotCount = {slotCount},");
        sb.AppendLine($"\t\tClassNum = {classNum},");
        sb.AppendLine("\t\tcostume = false,");
        sb.AppendLine("\t\tCustom = true");
        sb.Append("\t},");
        return sb.ToString();
    }

    private static string EscapeLuaString(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
    }

    private List<string> BuildDescriptionLines(ItemEntry item)
    {
        var lines = new List<string>();

        // Custom lore: use item.Description when present (multi-line, split by newlines)
        if (!string.IsNullOrWhiteSpace(item.Description))
        {
            foreach (var line in item.Description.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None))
                lines.Add(line.TrimEnd());
            if (lines.Count > 0)
                lines.Add("^777777________________________^000000");
        }
        else
        {
            // Auto-generated lore when no explicit description is provided.
            lines.Add("A custom item.");
            lines.Add("^777777________________________^000000");
        }

        AppendEquipmentDetails(item, lines);
        return lines;
    }

    private static void AppendEquipmentDetails(ItemEntry item, List<string> lines)
    {
        var typeLine = $"^0000CCType:^000000 {item.Type}";
        if (!string.IsNullOrEmpty(item.SubType))
            typeLine += $" / {item.SubType}";
        lines.Add(typeLine);

        if (item.Attack.HasValue && item.Attack.Value > 0)
            lines.Add($"^0000CCAttack:^000000 {item.Attack.Value}");
        if (item.MagicAttack.HasValue && item.MagicAttack.Value > 0)
            lines.Add($"^0000CCMagic Attack:^000000 {item.MagicAttack.Value}");
        if (item.Defense.HasValue && item.Defense.Value > 0)
            lines.Add($"^0000CCDefense:^000000 {item.Defense.Value}");
        if (item.Range.HasValue && item.Range.Value > 0)
            lines.Add($"^0000CCRange:^000000 {item.Range.Value}");

        var weight = item.Weight.HasValue ? item.Weight.Value / 10.0 : 0;
        lines.Add($"^0000CCWeight:^000000 {weight:F1}");

        if (item.EquipLevelMin.HasValue && item.EquipLevelMin.Value > 0)
            lines.Add($"^0000CCRequired Level:^000000 {item.EquipLevelMin.Value}");
        if (item.Locations != null && item.Locations.Count > 0)
        {
            var locs = new List<string>();
            foreach (var kv in item.Locations)
            {
                if (kv.Value && !string.IsNullOrEmpty(kv.Key))
                    locs.Add(kv.Key.Replace("_", " "));
            }
            if (locs.Count > 0)
                lines.Add($"^0000CCEquipped on:^000000 {string.Join(", ", locs)}");
        }

        if (item.Jobs != null && item.Jobs.Count > 0)
        {
            if (item.Jobs.TryGetValue("All", out var all) && all)
                lines.Add("^0000CCApplicable Job:^000000 Every Job");
            else
            {
                var jobs = new List<string>();
                foreach (var kv in item.Jobs)
                {
                    if (kv.Value && !string.IsNullOrEmpty(kv.Key))
                        jobs.Add(kv.Key);
                }
                if (jobs.Count > 0)
                    lines.Add($"^0000CCApplicable Job:^000000 {string.Join(", ", jobs)}");
            }
        }
        else
            lines.Add("^0000CCApplicable Job:^000000 Every Job");

        if (item.Slots.HasValue && item.Slots.Value > 0)
            lines.Add($"^0000CCSlots:^000000 {item.Slots.Value}");
    }

    /// <summary>
    /// Quick sanity check for common Lua syntax issues that would break the client.
    /// Returns null if OK, or a description of the problem.
    /// </summary>
    private static string? CheckLuaSyntaxSanity(string content)
    {
        // Stray comma after opening brace: tbl_custom = { , — this was the original bug
        if (Regex.IsMatch(content, @"\{\s*,\s*\["))
            return "Stray comma after opening brace '{ ,' — would cause 'unexpected symbol near ,' error";

        // Double comma between entries: },, [
        if (Regex.IsMatch(content, @",\s*,"))
            return "Double comma ',,' detected — would cause Lua parse error";

        // Unmatched braces in tbl_custom
        var tblMatch = Regex.Match(content, @"tbl_custom\s*=\s*\{");
        if (tblMatch.Success)
        {
            int depth = 0;
            bool foundClose = false;
            for (int i = tblMatch.Index + tblMatch.Length - 1; i < content.Length; i++)
            {
                if (content[i] == '{') depth++;
                else if (content[i] == '}')
                {
                    depth--;
                    if (depth == 0) { foundClose = true; break; }
                }
            }
            if (!foundClose)
                return "tbl_custom has unmatched braces — missing closing '}'";
        }

        return null; // Looks OK
    }
}
