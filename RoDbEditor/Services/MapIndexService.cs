using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RoDbEditor.Services;

public class MapIndexService
{
    private readonly HashSet<string> _maps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _displayNames = new(StringComparer.OrdinalIgnoreCase);

    public void LoadFromDataPath(string? dataPath)
    {
        _maps.Clear();
        _displayNames.Clear();

        if (string.IsNullOrWhiteSpace(dataPath) || !Directory.Exists(dataPath))
            return;

        var mapIndexPaths = new[]
        {
            Path.Combine(dataPath, "db", "map_index.txt"),
            Path.Combine(dataPath, "db", "mapindex.txt"),
            Path.Combine(dataPath, "db", "import", "map_index.txt"),
            Path.Combine(dataPath, "db", "re", "map_index.txt"),
            Path.Combine(dataPath, "db", "pre-re", "map_index.txt"),
        };

        foreach (var path in mapIndexPaths.Where(File.Exists))
            ParseMapIndexFile(path);

        var mapNameTablePath = Path.Combine(dataPath, "data", "mapnametable.txt");
        if (File.Exists(mapNameTablePath))
            LoadMapNameTable(mapNameTablePath);
    }

    private void ParseMapIndexFile(string filePath)
    {
        foreach (var line in File.ReadAllLines(filePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("//") || trimmed.StartsWith("#"))
                continue;

            var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                var mapName = parts[0];
                _maps.Add(mapName);
            }
        }
    }

    private void LoadMapNameTable(string filePath)
    {
        foreach (var line in File.ReadAllLines(filePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("//") || trimmed.StartsWith("#"))
                continue;

            var idx = trimmed.IndexOf('#');
            if (idx < 0) continue;

            var mapPart = trimmed.Substring(0, idx).Trim();
            var displayPart = idx + 1 < trimmed.Length ? trimmed.Substring(idx + 1) : "";

            var hash2 = displayPart.IndexOf('#');
            var displayName = hash2 >= 0 ? displayPart.Substring(0, hash2).Trim() : displayPart.Trim();

            if (string.IsNullOrEmpty(mapPart)) continue;

            var mapName = mapPart.EndsWith(".rsw", StringComparison.OrdinalIgnoreCase)
                ? mapPart.Substring(0, mapPart.Length - 4)
                : mapPart;

            if (!string.IsNullOrEmpty(displayName))
                _displayNames[mapName] = displayName;
        }
    }

    public bool MapExists(string mapName) => !string.IsNullOrEmpty(mapName) && _maps.Contains(mapName);

    public bool Exists(string mapName) => MapExists(mapName);

    public string GetDisplayName(string mapName)
    {
        if (string.IsNullOrEmpty(mapName)) return mapName ?? "";
        if (_displayNames.TryGetValue(mapName, out var name)) return name;
        return mapName;
    }

    public bool HasServerIndex => _maps.Count > 0;

    public IReadOnlyCollection<string> AllMaps => _maps;
}
