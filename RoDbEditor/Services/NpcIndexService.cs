using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RoDbEditor.Models;

namespace RoDbEditor.Services;

public class NpcIndexService
{
    private readonly Dictionary<string, List<NpcScriptEntry>> _byMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<NpcScriptEntry> _all = new();

    public IReadOnlyDictionary<string, List<NpcScriptEntry>> ByMap => _byMap;
    public IReadOnlyList<NpcScriptEntry> All => _all;

    public void LoadFromDataPath(string? dataPath)
    {
        _byMap.Clear();
        _all.Clear();
        if (string.IsNullOrWhiteSpace(dataPath)) return;

        var npcRoot = Path.Combine(dataPath, "npc");
        if (!Directory.Exists(npcRoot)) return;

        foreach (var file in Directory.EnumerateFiles(npcRoot, "*.txt", SearchOption.AllDirectories))
        {
            try
            {
                IndexFile(file, dataPath);
            }
            catch { }
        }

        foreach (var list in _byMap.Values)
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }

    private void IndexFile(string filePath, string dataPathRoot)
    {
        var lines = File.ReadAllLines(filePath);
        var relPath = filePath.StartsWith(dataPathRoot, StringComparison.OrdinalIgnoreCase)
            ? filePath.Substring(dataPathRoot.TrimEnd(Path.DirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar)
            : filePath;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var entry = ParseNpcLine(line, lines, i, filePath, relPath);
            if (entry == null) continue;

            _all.Add(entry);
            var map = entry.Map;
            if (string.IsNullOrEmpty(map)) map = "-";
            if (!_byMap.ContainsKey(map)) _byMap[map] = new List<NpcScriptEntry>();
            _byMap[map].Add(entry);
        }
    }

    /// <summary>Parse one NPC definition line. Returns null if not a valid NPC line.</summary>
    private static NpcScriptEntry? ParseNpcLine(string line, string[] allLines, int lineIndex, string filePath, string relPath)
    {
        if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("//", StringComparison.Ordinal)) return null;
        var parts = line.Split('\t');
        if (parts.Length < 3) return null;

        var loc = parts[0].Trim();
        var typeStr = parts[1].Trim();
        var rest = parts[2].Trim();

        // map,x,y,dir or map,x,y
        var locParts = loc.Split(',');
        if (locParts.Length < 3) return null;
        if (!int.TryParse(locParts[1].Trim(), out var x) || !int.TryParse(locParts[2].Trim(), out var y)) return null;
        var map = locParts[0].Trim();
        var dir = 0;
        if (locParts.Length >= 4) int.TryParse(locParts[3].Trim(), out dir);

        NpcScriptType type;
        if (string.Equals(typeStr, "script", StringComparison.OrdinalIgnoreCase)) type = NpcScriptType.Script;
        else if (string.Equals(typeStr, "shop", StringComparison.OrdinalIgnoreCase)) type = NpcScriptType.Shop;
        else if (string.Equals(typeStr, "warp", StringComparison.OrdinalIgnoreCase)) type = NpcScriptType.Warp;
        else if (typeStr.StartsWith("duplicate(", StringComparison.OrdinalIgnoreCase)) return null; // skip duplicates for index
        else return null;

        var entry = new NpcScriptEntry
        {
            Map = map,
            X = x,
            Y = y,
            Direction = dir,
            Type = type,
            FilePath = filePath,
            LineIndex = lineIndex,
            RawLine = line
        };

        // name and sprite/id part: "Name\t93" or "Name\t93,4001:-1,..."
        var nameRest = rest.Split(new[] { '\t' }, 2);
        entry.Name = nameRest[0].Trim();
        if (nameRest.Length < 2) { entry.SpriteId = ""; return entry; }
        var spriteAndData = nameRest[1].Trim();

        if (type == NpcScriptType.Shop)
        {
            // sprite,item_id:price,item_id:price,...
            var shopParts = spriteAndData.Split(',');
            if (shopParts.Length > 0) entry.SpriteId = shopParts[0].Trim();
            for (int j = 1; j < shopParts.Length; j++)
            {
                var itemPrice = shopParts[j].Trim().Split(':');
                if (itemPrice.Length >= 1 && int.TryParse(itemPrice[0].Trim(), out var itemId))
                {
                    var price = itemPrice.Length >= 2 && int.TryParse(itemPrice[1].Trim(), out var p) ? p : -1;
                    entry.ShopItems.Add(new ShopItemEntry { ItemId = itemId, Price = price });
                }
            }
            return entry;
        }

        if (type == NpcScriptType.Warp)
        {
            // warp one-liner: sprite,x,y,"tomap",x,y
            entry.SpriteId = spriteAndData.Split(',')[0].Trim();
            var warpMatch = Regex.Match(spriteAndData, @"(\d+)\s*,\s*(\d+)\s*,\s*""([^""]*)""\s*,\s*(\d+)\s*,\s*(\d+)");
            if (warpMatch.Success)
                entry.WarpTarget = new WarpTarget
                {
                    Map = warpMatch.Groups[3].Value,
                    X = int.Parse(warpMatch.Groups[4].Value),
                    Y = int.Parse(warpMatch.Groups[5].Value)
                };
            return entry;
        }

        // script: name sprite_id,{
        var commaIdx = spriteAndData.IndexOf(',');
        if (commaIdx >= 0) entry.SpriteId = spriteAndData.Substring(0, commaIdx).Trim();
        else entry.SpriteId = spriteAndData;
        if (spriteAndData.EndsWith(",{"))
        {
            var body = ExtractScriptBody(allLines, lineIndex);
            entry.ScriptBody = body ?? "";
            if (body != null)
            {
                var warp = ParseWarpFromBody(body);
                if (warp != null) entry.WarpTarget = warp;
            }
        }
        return entry;
    }

    private static string? ExtractScriptBody(string[] lines, int startIndex)
    {
        int brace = 1;
        var sb = new System.Text.StringBuilder();
        for (int i = startIndex + 1; i < lines.Length && brace > 0; i++)
        {
            var l = lines[i];
            foreach (var c in l)
            {
                if (c == '{') brace++;
                else if (c == '}') brace--;
            }
            if (brace > 0) sb.AppendLine(l);
        }
        return sb.ToString().TrimEnd();
    }

    private static WarpTarget? ParseWarpFromBody(string body)
    {
        // warp "map",x,y;
        var m = Regex.Match(body, @"warp\s+""([^""]*)""\s*,\s*(\d+)\s*,\s*(\d+)", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return new WarpTarget { Map = m.Groups[1].Value, X = int.Parse(m.Groups[2].Value), Y = int.Parse(m.Groups[3].Value) };
    }

    public IReadOnlyList<string> GetMapNames()
    {
        var list = _byMap.Keys.Where(k => k != "-").OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        if (_byMap.ContainsKey("-")) list.Insert(0, "-");
        return list;
    }

    public IReadOnlyList<NpcScriptEntry> GetNpcsOnMap(string? map)
    {
        if (string.IsNullOrEmpty(map)) return _all;
        return _byMap.TryGetValue(map, out var list) ? list : Array.Empty<NpcScriptEntry>();
    }

    public void SaveNpc(NpcScriptEntry entry)
    {
        if (string.IsNullOrEmpty(entry.FilePath) || !File.Exists(entry.FilePath)) return;
        var lines = File.ReadAllLines(entry.FilePath).ToList();
        if (entry.LineIndex < 0 || entry.LineIndex >= lines.Count) return;

        if (entry.Type == NpcScriptType.Shop)
        {
            var itemPart = string.Join(",", entry.ShopItems.Select(s => $"{s.ItemId}:{s.Price}"));
            var loc = $"{entry.Map},{entry.X},{entry.Y},{entry.Direction}";
            lines[entry.LineIndex] = $"{loc}\tshop\t{entry.Name}\t{entry.SpriteId},{itemPart}";
        }
        else if (entry.Type == NpcScriptType.Warp && entry.WarpTarget != null)
        {
            var w = entry.WarpTarget;
            lines[entry.LineIndex] = $"{entry.Map},{entry.X},{entry.Y},{entry.Direction}\twarp\t{entry.Name}\t{entry.SpriteId},{entry.X},{entry.Y},\"{w.Map}\",{w.X},{w.Y}";
        }
        else if (entry.Type == NpcScriptType.Script && !string.IsNullOrEmpty(entry.ScriptBody))
        {
            var header = lines[entry.LineIndex];
            if (header.Contains(",{"))
            {
                var bodyLines = entry.ScriptBody.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                var newLines = new List<string>();
                for (int i = 0; i <= entry.LineIndex; i++) newLines.Add(lines[i]);
                newLines.AddRange(bodyLines);
                newLines.Add("}");
                int skip = entry.LineIndex + 1;
                int brace = 0;
                foreach (var c in lines[entry.LineIndex]) { if (c == '{') brace++; if (c == '}') brace--; }
                while (skip < lines.Count && brace > 0)
                {
                    foreach (var c in lines[skip]) { if (c == '{') brace++; if (c == '}') brace--; }
                    skip++;
                }
                for (int i = skip; i < lines.Count; i++) newLines.Add(lines[i]);
                lines = newLines;
            }
        }

        File.WriteAllLines(entry.FilePath, lines);
    }
}
