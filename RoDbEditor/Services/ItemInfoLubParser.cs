using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace RoDbEditor.Services;

/// <summary>
/// Minimal parser for iteminfo.lub / iteminfo.lua client table to read description by item id.
/// Expects table format like: [501] = { description = "..." } or [501] = { ["description"] = "..." }
/// </summary>
public static class ItemInfoLubParser
{
    private static readonly Regex IdBlockRegex = new Regex(
        @"\[\s*(\d+)\s*\]\s*=\s*\{([^{}]*(?:\{[^{}]*\}[^{}]*)*)\}",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex DescriptionRegex = new Regex(
        @"(?:description|[""]description[""])\s*=\s*[""]([^""]*?)[""]",
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Parse file and return map of item id -> description. Returns empty dict on error.
    /// </summary>
    public static IReadOnlyDictionary<int, string> ParseDescriptions(string? filePath)
    {
        var result = new Dictionary<int, string>();
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return result;

        try
        {
            var content = File.ReadAllText(filePath);
            foreach (Match m in IdBlockRegex.Matches(content))
            {
                if (!int.TryParse(m.Groups[1].Value, out var id))
                    continue;
                var block = m.Groups[2].Value;
                var descMatch = DescriptionRegex.Match(block);
                if (descMatch.Success)
                    result[id] = descMatch.Groups[1].Value.Replace("\\n", "\n").Replace("\\\"", "\"");
            }
        }
        catch
        {
            // ignore
        }

        return result;
    }
}
