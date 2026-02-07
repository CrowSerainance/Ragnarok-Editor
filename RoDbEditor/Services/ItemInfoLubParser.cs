using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using GRF;
using GRF.FileFormats.LubFormat;
using RoDbEditor.Models;
using Utilities.Parsers.Lua;

namespace RoDbEditor.Services;

/// <summary>
/// Parses iteminfo.lub / iteminfo.lua from GRF to extract item entries.
/// Uses GRF.dll's Lub decompiler and custom regex parsing.
/// </summary>
public static class ItemInfoLubParser
{
    /// <summary>
    /// Parse descriptions only (legacy method for file path).
    /// </summary>
    public static IReadOnlyDictionary<int, string> ParseDescriptions(string? filePath)
    {
        var result = new Dictionary<int, string>();
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return result;

        try
        {
            var data = File.ReadAllBytes(filePath);
            return ParseDescriptionsFromData(data);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ItemInfoLubParser] ParseDescriptions failed: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Parse descriptions from raw byte data (e.g., from GRF).
    /// </summary>
    public static IReadOnlyDictionary<int, string> ParseDescriptionsFromData(byte[]? data)
    {
        var result = new Dictionary<int, string>();
        if (data == null || data.Length == 0)
            return result;

        try
        {
            var items = ParseItemEntriesFromData(data);
            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(item.Name))
                    result[item.Id] = item.Name;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ItemInfoLubParser] ParseDescriptionsFromData failed: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Parse full item entries from iteminfo.lub data (from GRF).
    /// </summary>
    public static List<ItemEntry> ParseItemEntriesFromData(byte[]? data)
    {
        var result = new List<ItemEntry>();
        if (data == null || data.Length == 0)
            return result;

        try
        {
            System.Diagnostics.Debug.WriteLine($"[ItemInfoLubParser] Parsing {data.Length} bytes");

            // Decompile the LUB file to Lua text
            string content = DecompileToLua(data);

            if (string.IsNullOrEmpty(content))
            {
                System.Diagnostics.Debug.WriteLine("[ItemInfoLubParser] Decompilation returned empty content");
                return result;
            }

            System.Diagnostics.Debug.WriteLine($"[ItemInfoLubParser] Decompiled content length: {content.Length}");

            // Log first 1000 chars to understand the format
            var preview = content.Length > 1000 ? content.Substring(0, 1000) : content;
            System.Diagnostics.Debug.WriteLine($"[ItemInfoLubParser] Content preview:\n{preview}");

            // Parse the decompiled Lua
            result = ParseLuaContent(content);

            System.Diagnostics.Debug.WriteLine($"[ItemInfoLubParser] Parsed {result.Count} items");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ItemInfoLubParser] ParseItemEntriesFromData failed: {ex}");
        }

        return result;
    }

    /// <summary>
    /// Decompile .lub binary to Lua text using GRF.dll
    /// </summary>
    private static string DecompileToLua(byte[] data)
    {
        try
        {
            bool isLub = LuaParser.IsLub(data);
            System.Diagnostics.Debug.WriteLine($"[ItemInfoLubParser] IsLub: {isLub}");

            if (isLub)
            {
                // Use GRF.dll's Lub decompiler
                MultiType mt = data;
                var lub = new Lub(mt);
                return lub.Decompile();
            }
            else
            {
                // Already plain text Lua - try different encodings
                try
                {
                    return Encoding.UTF8.GetString(data);
                }
                catch
                {
                    return Encoding.GetEncoding(949).GetString(data); // Korean CP949
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ItemInfoLubParser] Decompile failed: {ex.Message}");
            // Fallback: treat as text
            try { return Encoding.UTF8.GetString(data); }
            catch { return Encoding.GetEncoding(949).GetString(data); }
        }
    }

    /// <summary>
    /// Parse decompiled Lua content to extract item entries.
    /// Handles multiple common iteminfo.lub formats.
    /// </summary>
    private static List<ItemEntry> ParseLuaContent(string content)
    {
        var result = new List<ItemEntry>();

        // Format 1: Standard table assignment
        // tbl[501] = { unidentifiedDisplayName = "...", identifiedDisplayName = "...", ... }
        // OR
        // [501] = { ... }

        // Regex pattern that matches either format
        // Group 1: Item ID
        // Group 2: Table contents
        var patterns = new[]
        {
            // Pattern 1: tbl[ID] = { ... } - with variable prefix
            @"(?:\w+)\s*\[\s*(\d+)\s*\]\s*=\s*\{([^{}]*(?:\{[^{}]*\}[^{}]*)*)\}",

            // Pattern 2: [ID] = { ... } - direct table key
            @"\[\s*(\d+)\s*\]\s*=\s*\{([^{}]*(?:\{[^{}]*\}[^{}]*)*)\}",

            // Pattern 3: Handles deeper nesting { { } { } }
            @"\[\s*(\d+)\s*\]\s*=\s*\{((?:[^{}]|\{(?:[^{}]|\{[^{}]*\})*\})*)\}",
        };

        foreach (var pattern in patterns)
        {
            try
            {
                var regex = new Regex(pattern, RegexOptions.Singleline | RegexOptions.Compiled);
                var matches = regex.Matches(content);

                System.Diagnostics.Debug.WriteLine($"[ItemInfoLubParser] Pattern '{pattern.Substring(0, Math.Min(50, pattern.Length))}...' found {matches.Count} matches");

                if (matches.Count > 0)
                {
                    foreach (Match m in matches)
                    {
                        if (!int.TryParse(m.Groups[1].Value, out var id))
                            continue;

                        // Skip if we already have this ID
                        if (result.Exists(e => e.Id == id))
                            continue;

                        var block = m.Groups[2].Value;
                        var fields = ParseTableFields(block);

                        var entry = new ItemEntry
                        {
                            Id = id,
                            AegisName = GetStringField(fields, "identifiedResourceName", "unidentifiedResourceName") ?? $"ITEM_{id}",
                            Name = GetStringField(fields, "identifiedDisplayName", "unidentifiedDisplayName") ?? $"Item {id}",
                            Type = "Etc",
                            SourceFile = "iteminfo.lub"
                        };

                        if (fields.TryGetValue("slotCount", out var slotStr) && int.TryParse(slotStr, out var slots))
                            entry.Slots = slots;

                        result.Add(entry);
                    }

                    // If we found items with this pattern, no need to try others
                    if (result.Count > 0)
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ItemInfoLubParser] Pattern matching failed: {ex.Message}");
            }
        }

        // If no matches found, try line-by-line parsing
        if (result.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("[ItemInfoLubParser] No regex matches, trying line-by-line parsing");
            result = ParseLineByLine(content);
        }

        return result;
    }

    /// <summary>
    /// Parse content line by line for unusual formats
    /// </summary>
    private static List<ItemEntry> ParseLineByLine(string content)
    {
        var result = new List<ItemEntry>();
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        int? currentId = null;
        var currentFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int braceDepth = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("--"))
                continue;

            // Check for item ID assignment: tbl[501] = { or [501] = {
            var idMatch = Regex.Match(line, @"(?:\w+\s*)?\[\s*(\d+)\s*\]\s*=\s*\{");
            if (idMatch.Success)
            {
                // Save previous item if exists
                if (currentId.HasValue && currentFields.Count > 0)
                {
                    result.Add(CreateEntryFromFields(currentId.Value, currentFields));
                }

                currentId = int.Parse(idMatch.Groups[1].Value);
                currentFields.Clear();
                braceDepth = 1;

                // Parse rest of line after the opening brace
                var restOfLine = line.Substring(idMatch.Index + idMatch.Length);
                ParseFieldsFromLine(restOfLine, currentFields);
                continue;
            }

            // If we're inside an item definition
            if (currentId.HasValue && braceDepth > 0)
            {
                // Count braces
                foreach (char c in line)
                {
                    if (c == '{') braceDepth++;
                    else if (c == '}') braceDepth--;
                }

                // Parse fields from this line
                ParseFieldsFromLine(line, currentFields);

                // Check if we've closed the item
                if (braceDepth <= 0)
                {
                    result.Add(CreateEntryFromFields(currentId.Value, currentFields));
                    currentId = null;
                    currentFields.Clear();
                    braceDepth = 0;
                }
            }
        }

        // Don't forget the last item
        if (currentId.HasValue && currentFields.Count > 0)
        {
            result.Add(CreateEntryFromFields(currentId.Value, currentFields));
        }

        return result;
    }

    private static void ParseFieldsFromLine(string line, Dictionary<string, string> fields)
    {
        // Match patterns like: fieldName = "value" or fieldName = 123
        var fieldPattern = new Regex(@"(\w+)\s*=\s*(?:""([^""]*)""|(\d+))");
        foreach (Match m in fieldPattern.Matches(line))
        {
            var key = m.Groups[1].Value;
            var value = !string.IsNullOrEmpty(m.Groups[2].Value) ? m.Groups[2].Value : m.Groups[3].Value;
            if (!string.IsNullOrEmpty(key) && !fields.ContainsKey(key))
                fields[key] = value;
        }
    }

    private static ItemEntry CreateEntryFromFields(int id, Dictionary<string, string> fields)
    {
        return new ItemEntry
        {
            Id = id,
            AegisName = GetStringField(fields, "identifiedResourceName", "unidentifiedResourceName") ?? $"ITEM_{id}",
            Name = GetStringField(fields, "identifiedDisplayName", "unidentifiedDisplayName") ?? $"Item {id}",
            Type = "Etc",
            SourceFile = "iteminfo.lub",
            Slots = fields.TryGetValue("slotCount", out var s) && int.TryParse(s, out var slots) ? slots : null
        };
    }

    /// <summary>
    /// Parse table fields from a block like: key = "value", key2 = 123, ...
    /// </summary>
    private static Dictionary<string, string> ParseTableFields(string block)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(block))
            return result;

        // Match: key = "string value" or key = number
        var fieldRegex = new Regex(@"(\w+)\s*=\s*(?:""((?:[^""\\]|\\.)*)""|(\d+(?:\.\d+)?)|(\w+))");

        foreach (Match m in fieldRegex.Matches(block))
        {
            var key = m.Groups[1].Value;
            string value;

            if (!string.IsNullOrEmpty(m.Groups[2].Value))
                value = m.Groups[2].Value.Replace("\\\"", "\"").Replace("\\n", "\n");
            else if (!string.IsNullOrEmpty(m.Groups[3].Value))
                value = m.Groups[3].Value;
            else
                value = m.Groups[4].Value;

            if (!string.IsNullOrEmpty(key))
                result[key] = value;
        }

        return result;
    }

    private static string? GetStringField(Dictionary<string, string> fields, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (fields.TryGetValue(key, out var val) && !string.IsNullOrEmpty(val))
                return val;
        }
        return null;
    }
}
