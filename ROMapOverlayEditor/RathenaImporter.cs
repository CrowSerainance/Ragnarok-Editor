using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ROMapOverlayEditor;

public static class RAthenaImporter
{
    /// <summary>
    /// Imports rAthena warp lines:
    /// map,x,y,dir    warp    name    w,h,destmap,destx,desty
    /// Tabs/spaces are tolerated.
    /// </summary>
    public static List<WarpPlacable> ImportWarps(string fileText, string fallbackMapName)
    {
        fileText = StripBlockComments(fileText);
        var results = new List<WarpPlacable>();
        var lines = SplitLines(fileText);

        foreach (var raw in lines)
        {
            var line = StripComments(raw).Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            // quick filter
            if (!ContainsToken(line, "warp")) continue;

            // Normalize whitespace to tabs to make splitting easier
            var parts = SplitByWhitespace(line);
            // Expected tokens: [0]=map,x,y,dir  [1]=warp  [2]=name  [3]=w,h,destmap,destx,desty
            if (parts.Length < 4) continue;
            if (!parts[1].Equals("warp", StringComparison.OrdinalIgnoreCase)) continue;

            if (!TryParseMapXYDir(parts[0], fallbackMapName, out var map, out var x, out var y, out var dir))
                continue;

            var warpName = parts[2].Trim();
            if (string.IsNullOrWhiteSpace(warpName)) warpName = $"warp_{results.Count + 1}";

            // the dest bundle might have spaces if script writer did weird formatting, so re-join tail
            var tail = string.Join(" ", parts.Skip(3));
            var dest = tail.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            // w,h,destmap,destx,desty = 5 elements; allow more (e.g. duplicate warp / extra args)
            if (dest.Length < 5) continue;

            if (!int.TryParse(dest[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var w)) w = 1;
            if (!int.TryParse(dest[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)) h = 1;

            var destMap = dest[2];
            if (!int.TryParse(dest[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dx)) dx = 0;
            if (!int.TryParse(dest[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dy)) dy = 0;

            // Some formats include an extra "0" or more params; we ignore extras.

            var wp = new WarpPlacable
            {
                MapName = map,
                X = x,
                Y = y,
                Dir = dir,
                Label = "warp",
                WarpName = warpName,
                W = Math.Max(1, w),
                H = Math.Max(1, h),
                DestMap = destMap,
                DestX = dx,
                DestY = dy
            };

            results.Add(wp);
        }

        return results;
    }

    /// <summary>
    /// Imports rAthena NPC script blocks:
    /// map,x,y,dir    script    Name    Sprite,{
    ///     body...
    /// }
    /// </summary>
    public static List<NpcPlacable> ImportNpcs(string fileText, string fallbackMapName)
    {
        fileText = StripBlockComments(fileText);
        var results = new List<NpcPlacable>();
        var lines = SplitLines(fileText);

        int i = 0;
        while (i < lines.Count)
        {
            var rawLine = lines[i];
            var line = StripComments(rawLine).Trim();

            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            // Detect header line containing "\tscript\t" or whitespace version
            if (!ContainsToken(line, "script"))
            {
                i++;
                continue;
            }

            // Try parse header
            // Typical: map,x,y,dir    script    Name    Sprite,{
            var headerParts = SplitByWhitespace(line);
            if (headerParts.Length < 4)
            {
                i++;
                continue;
            }

            if (!headerParts[1].Equals("script", StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }

            if (!TryParseMapXYDir(headerParts[0], fallbackMapName, out var map, out var x, out var y, out var dir))
            {
                i++;
                continue;
            }

            var nameToken = headerParts[2].Trim();
            var spriteAndMaybeBrace = string.Join(" ", headerParts.Skip(3)).Trim();

            // We expect something like: Sprite,{
            // But some people write: Sprite, {
            // We'll find first '{' in the remainder
            var braceIndex = spriteAndMaybeBrace.IndexOf('{');
            if (braceIndex < 0)
            {
                // maybe next line begins "{"
                // still try: remove trailing comma
                // We'll treat sprite as up to end
            }

            string spriteToken;
            if (braceIndex >= 0)
            {
                spriteToken = spriteAndMaybeBrace[..braceIndex].Trim();
            }
            else
            {
                spriteToken = spriteAndMaybeBrace.Trim();
            }

            // remove trailing commas
            spriteToken = spriteToken.TrimEnd(',');
            if (string.IsNullOrWhiteSpace(spriteToken)) spriteToken = "4_M_01";

            // Now read body until the matching closing "}"
            // rAthena scripts rarely nest braces in a way that breaks this; we'll do a simple brace counter.
            var bodyLines = new List<string>();

            // If the header line contains '{', start braceCount at 1, else wait until we see one.
            int braceCount = (braceIndex >= 0) ? 1 : 0;

            i++; // move to next line after header
            while (i < lines.Count)
            {
                var bodyRaw = lines[i];

                // Count braces, but do not strip comments before counting because braces might be inside strings;
                // still good enough for most rAthena scripts.
                braceCount += CountChar(bodyRaw, '{');
                braceCount -= CountChar(bodyRaw, '}');

                // If this line contains only "}" (possibly with spaces), we end and don't include it
                var trimmed = bodyRaw.Trim();
                if (trimmed == "}" || trimmed == "};")
                {
                    i++; // consume it
                    break;
                }
                // If only "{" (sprite and brace split across lines), consume and do not add to body
                if (trimmed == "{")
                {
                    i++;
                    continue;
                }

                // Otherwise include line as-is (keep indentation)
                bodyLines.Add(bodyRaw.Replace("\r", ""));

                if (braceCount <= 0)
                {
                    i++; // consumed closing
                    break;
                }

                i++;
            }

            var npc = new NpcPlacable
            {
                MapName = map,
                X = x,
                Y = y,
                Dir = dir,
                Label = "npc",
                ScriptName = NormalizeTokenToName(nameToken),
                Sprite = NormalizeSprite(spriteToken),
                ScriptBody = NormalizeBody(bodyLines)
            };

            results.Add(npc);
        }

        return results;
    }

    // ---------------------------
    // Helpers
    // ---------------------------
    private static List<string> SplitLines(string text)
        => text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n').ToList();

    private static string StripComments(string line)
    {
        // rAthena uses // for comments, # in some contexts; we handle // safely.
        var idx = line.IndexOf("//", StringComparison.Ordinal);
        if (idx >= 0) return line[..idx];
        return line;
    }

    private static bool ContainsToken(string line, string token)
        => line.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

    private static string[] SplitByWhitespace(string line)
        => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static bool TryParseMapXYDir(string mapxy, string fallbackMapName, out string map, out int x, out int y, out int dir)
    {
        map = fallbackMapName;
        x = y = dir = 0;

        // map,x,y,dir  or  x,y,dir (map omitted => fallbackMapName)
        var a = mapxy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (a.Length == 3)
        {
            map = fallbackMapName;
            if (!int.TryParse(a[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x)) return false;
            if (!int.TryParse(a[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y)) return false;
            if (!int.TryParse(a[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out dir)) dir = 0;
            return true;
        }
        if (a.Length < 4) return false;

        map = string.IsNullOrWhiteSpace(a[0]) ? fallbackMapName : a[0];
        if (!int.TryParse(a[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out x)) return false;
        if (!int.TryParse(a[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out y)) return false;
        if (!int.TryParse(a[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out dir)) dir = 0;

        return true;
    }

    private static string StripBlockComments(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var r = text;
        while (true)
        {
            int s = r.IndexOf("/*", StringComparison.Ordinal);
            if (s < 0) break;
            int e = r.IndexOf("*/", s + 2, StringComparison.Ordinal);
            if (e < 0) break;
            r = r[..s] + r[(e + 2)..];
        }
        return r;
    }

    private static int CountChar(string s, char c)
    {
        int n = 0;
        foreach (var ch in s) if (ch == c) n++;
        return n;
    }

    private static string NormalizeBody(List<string> lines)
    {
        // Remove leading/trailing blank lines, keep internal formatting
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0])) lines.RemoveAt(0);
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1])) lines.RemoveAt(lines.Count - 1);

        // If the first lines are tab-indented, normalize by trimming ONE leading tab per line.
        // This makes editing nicer in the property box.
        bool hasTabs = lines.Any(l => l.StartsWith("\t"));
        if (hasTabs)
        {
            lines = lines.Select(l => l.StartsWith("\t") ? l[1..] : l).ToList();
        }

        return string.Join("\n", lines);
    }

    private static string NormalizeTokenToName(string token)
    {
        // reverse of exporter (which replaces spaces with underscores)
        // we keep underscores as-is.
        token = token.Trim();
        if (string.IsNullOrWhiteSpace(token)) return "MyNpc";
        return token;
    }

    private static string NormalizeSprite(string token)
    {
        token = token.Trim();
        if (string.IsNullOrWhiteSpace(token)) return "4_M_01";
        return token.Replace("\t", " ").Trim();
    }
}
