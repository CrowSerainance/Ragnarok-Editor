using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using GRF;
using GRF.FileFormats.LubFormat;
using RoDbEditor.Models;
using Utilities.Parsers.Lua;

namespace RoDbEditor.Services;

/// <summary>
/// Parses mobinfo.lub / mobinfo.lua from GRF to extract monster entries.
/// Mirrors ItemInfoLubParser; uses same LUB decompilation and regex approach.
/// </summary>
public static class MobInfoLubParser
{
    // Match [1002] = { ... } blocks
    private static readonly Regex IdBlockRegex = new Regex(
        @"\[\s*(\d+)\s*\]\s*=\s*\{([^{}]*(?:\{[^{}]*\}[^{}]*)*)\}",
        RegexOptions.Singleline | RegexOptions.Compiled);

    // Match field = "value" or ["field"] = "value"
    private static readonly Regex FieldRegex = new Regex(
        @"(?:([a-zA-Z_]\w*)|[""]([^""]+)[""])\s*=\s*(?:[""]([^""]*?)[""]|(\d+))",
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Converts raw mobinfo.lub/lua bytes to Lua text. Decompiles compiled LUB via GRF.dll; otherwise decodes as UTF-8 or CP949.
    /// </summary>
    private static string GetContentFromData(byte[] data)
    {
        if (data == null || data.Length == 0)
            return string.Empty;

        if (LuaParser.IsLub(data))
        {
            try
            {
                MultiType mt = data;
                var lub = new Lub(mt);
                return lub.Decompile();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MobInfoLubParser] Decompile failed: {ex.Message}; falling back to text decode");
            }
        }

        try
        {
            return Encoding.UTF8.GetString(data);
        }
        catch
        {
            return Encoding.GetEncoding(949).GetString(data);
        }
    }

    /// <summary>
    /// Parse monster entries from mobinfo.lub data (from GRF).
    /// Returns list of MobEntry objects for display in the UI.
    /// Stats (HP, exp, etc.) are server-side; GRF mobinfo has names/display only.
    /// </summary>
    public static List<MobEntry> ParseMobEntriesFromData(byte[]? data)
    {
        var result = new List<MobEntry>();
        if (data == null || data.Length == 0)
            return result;

        try
        {
            var content = GetContentFromData(data);
            var matches = IdBlockRegex.Matches(content);
            System.Diagnostics.Debug.WriteLine($"[MobInfoLubParser] ParseMobEntriesFromData: regex match count: {matches.Count}");

            foreach (Match m in matches)
            {
                if (!int.TryParse(m.Groups[1].Value, out var id))
                    continue;

                var block = m.Groups[2].Value;
                var fields = ParseBlockFields(block);

                var name = GetFieldWithFallbacks(fields, "koreanName", "Name", "displayName", "name");
                var aegisName = GetFieldWithFallbacks(fields, "sprite", "spriteName", "Sprite") ?? $"MOB_{id}";

                var entry = new MobEntry
                {
                    Id = id,
                    AegisName = !string.IsNullOrEmpty(aegisName) ? aegisName : $"MOB_{id}",
                    Name = !string.IsNullOrEmpty(name) ? name : $"Monster {id}",
                    SourceFile = "mobinfo.lub"
                };

                result.Add(entry);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MobInfoLubParser] ParseMobEntriesFromData failed: {ex.Message}");
        }

        return result;
    }

    private static string? GetFieldWithFallbacks(Dictionary<string, string> fields, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (fields.TryGetValue(key, out var val) && !string.IsNullOrEmpty(val))
                return val;
        }
        return null;
    }

    private static Dictionary<string, string> ParseBlockFields(string block)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match fm in FieldRegex.Matches(block))
        {
            var key = !string.IsNullOrEmpty(fm.Groups[1].Value)
                ? fm.Groups[1].Value
                : fm.Groups[2].Value;

            var value = !string.IsNullOrEmpty(fm.Groups[3].Value)
                ? fm.Groups[3].Value
                : fm.Groups[4].Value;

            if (!string.IsNullOrEmpty(key))
                fields[key] = value;
        }

        return fields;
    }
}
