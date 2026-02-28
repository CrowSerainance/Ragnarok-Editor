using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RoDbEditor.Config;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RoDbEditor.Services;

/// <summary>
/// Centralized registry of used Item, View, and Mob IDs for collision-resistant allocation.
/// Scans db/import YAML and optionally client accessory tables to populate used sets;
/// provides Reserve, AllocateNext, and optional Release for deterministic allocation.
/// </summary>
public class IdRegistryService
{
    private readonly HashSet<int> _usedItemIds = new();
    private readonly HashSet<int> _usedViewIds = new();
    private readonly HashSet<int> _usedMobIds = new();

    public IReadOnlyCollection<int> UsedItemIds => _usedItemIds;
    public IReadOnlyCollection<int> UsedViewIds => _usedViewIds;
    public IReadOnlyCollection<int> UsedMobIds => _usedMobIds;

    /// <summary>Last allocated item ID (for "Last used" display).</summary>
    public int LastAllocatedItemId { get; private set; }
    /// <summary>Last allocated view ID.</summary>
    public int LastAllocatedViewId { get; private set; }
    /// <summary>Last allocated mob ID.</summary>
    public int LastAllocatedMobId { get; private set; }

    /// <summary>
    /// Scan db/import/item_db.yml and db/import/mob_db.yml under the given data path
    /// to populate used item IDs, view IDs (from item Body entries), and mob IDs.
    /// </summary>
    public void ScanServerDbImport(string? dataPath)
    {
        _usedItemIds.Clear();
        _usedViewIds.Clear();
        _usedMobIds.Clear();

        if (string.IsNullOrEmpty(dataPath) || !Directory.Exists(dataPath))
            return;

        var importDir = Path.Combine(dataPath, "db", "import");
        if (!Directory.Exists(importDir))
            return;

        var itemDbPath = Path.Combine(importDir, "item_db.yml");
        if (File.Exists(itemDbPath))
        {
            try
            {
                var text = File.ReadAllText(itemDbPath);
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(PascalCaseNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();
                var doc = deserializer.Deserialize<Dictionary<object, object>>(new StringReader(text));
                if (doc != null && doc.TryGetValue("Body", out var bodyObj) && bodyObj is System.Collections.IEnumerable bodyList)
                {
                    foreach (var entry in bodyList)
                    {
                        if (entry is not Dictionary<object, object> dict)
                            continue;
                        if (dict.TryGetValue("Id", out var idObj) && idObj != null && ParseInt(idObj, out var id))
                            _usedItemIds.Add(id);
                        if (dict.TryGetValue("View", out var viewObj) && viewObj != null && ParseInt(viewObj, out var view) && view > 0)
                            _usedViewIds.Add(view);
                    }
                }
            }
            catch
            {
                foreach (var line in File.ReadLines(itemDbPath))
                {
                    if (TryParseYamlIntLine(line, "Id:", out var id))
                        _usedItemIds.Add(id);
                    if (TryParseYamlIntLine(line, "View:", out var view) && view > 0)
                        _usedViewIds.Add(view);
                }
            }
        }

        var mobDbPath = Path.Combine(importDir, "mob_db.yml");
        if (File.Exists(mobDbPath))
        {
            try
            {
                var text = File.ReadAllText(mobDbPath);
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(PascalCaseNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();
                var doc = deserializer.Deserialize<Dictionary<object, object>>(new StringReader(text));
                if (doc != null && doc.TryGetValue("Body", out var bodyObj) && bodyObj is System.Collections.IEnumerable bodyList)
                {
                    foreach (var entry in bodyList)
                    {
                        if (entry is not Dictionary<object, object> dict)
                            continue;
                        if (dict.TryGetValue("Id", out var idObj) && idObj != null && ParseInt(idObj, out var id))
                            _usedMobIds.Add(id);
                    }
                }
            }
            catch
            {
                foreach (var line in File.ReadLines(mobDbPath))
                {
                    if (TryParseYamlIntLine(line, "Id:", out var id))
                        _usedMobIds.Add(id);
                }
            }
        }
    }

    /// <summary>
    /// Optionally scan client accessoryid.lua (from GRF or client root) to collect used View IDs.
    /// Heuristic: look for numeric values in ACC_* = number patterns.
    /// </summary>
    public void ScanClientAccessoryTables(Func<string, byte[]?>? getGrfData, string? clientRootPath)
    {
        var content = GetAccessoryIdContent(getGrfData, clientRootPath);
        if (string.IsNullOrEmpty(content))
            return;

        // Match patterns like [12345] = 12345 or ACC_Something = 32001
        var viewMatches = Regex.Matches(content, @"(?:=\s*)(\d{4,})");
        foreach (Match m in viewMatches)
        {
            if (m.Success && int.TryParse(m.Groups[1].Value, out var v) && v >= 32000)
                _usedViewIds.Add(v);
        }
    }

    private static string? GetAccessoryIdContent(Func<string, byte[]?>? getGrfData, string? clientRootPath)
    {
        var paths = new[]
        {
            "data/luafiles514/lua files/datainfo/accessoryid.lua",
            "data/luafiles514/lua files/datainfo/accessoryid.lub",
            @"data\luafiles514\lua files\datainfo\accessoryid.lua",
            @"data\luafiles514\lua files\datainfo\accessoryid.lub"
        };

        if (getGrfData != null)
        {
            foreach (var p in paths)
            {
                var data = getGrfData(p);
                if (data != null && data.Length > 0)
                {
                    try
                    {
                        return System.Text.Encoding.UTF8.GetString(data);
                    }
                    catch
                    {
                        // LUB is binary; skip or use LUB decompiler if available
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(clientRootPath) && Directory.Exists(clientRootPath))
        {
            var fsPath = Path.Combine(clientRootPath, "data", "luafiles514", "lua files", "datainfo", "accessoryid.lua");
            if (File.Exists(fsPath))
            {
                try
                {
                    return File.ReadAllText(fsPath);
                }
                catch { }
            }
        }

        return null;
    }

    /// <summary>Mark an ID as used (e.g. after allocation or when loading existing).</summary>
    public void ReserveItemId(int id) => _usedItemIds.Add(id);
    /// <summary>Mark a view ID as used.</summary>
    public void ReserveViewId(int id) => _usedViewIds.Add(id);
    /// <summary>Mark a mob ID as used.</summary>
    public void ReserveMobId(int id) => _usedMobIds.Add(id);

    /// <summary>Return the first free ID in [rangeMin, rangeMax] and reserve it.</summary>
    public int AllocateNextItemId(int rangeMin, int rangeMax)
    {
        var id = AllocateNext(_usedItemIds, rangeMin, rangeMax);
        LastAllocatedItemId = id;
        return id;
    }

    /// <summary>Return the first free view ID in range and reserve it.</summary>
    public int AllocateNextViewId(int rangeMin, int rangeMax)
    {
        var id = AllocateNext(_usedViewIds, rangeMin, rangeMax);
        LastAllocatedViewId = id;
        return id;
    }

    /// <summary>Return the first free mob ID in range and reserve it.</summary>
    public int AllocateNextMobId(int rangeMin, int rangeMax)
    {
        var id = AllocateNext(_usedMobIds, rangeMin, rangeMax);
        LastAllocatedMobId = id;
        return id;
    }

    private static int AllocateNext(HashSet<int> used, int rangeMin, int rangeMax)
    {
        for (var i = rangeMin; i <= rangeMax; i++)
        {
            if (!used.Contains(i))
            {
                used.Add(i);
                return i;
            }
        }
        return rangeMax + 1; // overflow; caller may warn
    }

    /// <summary>Remove an ID from the used set (e.g. undo).</summary>
    public void ReleaseItemId(int id) => _usedItemIds.Remove(id);
    public void ReleaseViewId(int id) => _usedViewIds.Remove(id);
    public void ReleaseMobId(int id) => _usedMobIds.Remove(id);

    public bool IsItemIdUsed(int id) => _usedItemIds.Contains(id);
    public bool IsViewIdUsed(int id) => _usedViewIds.Contains(id);
    public bool IsMobIdUsed(int id) => _usedMobIds.Contains(id);

    private static bool ParseInt(object value, out int result)
    {
        result = 0;
        if (value is int i)
        {
            result = i;
            return true;
        }
        if (value is long l)
        {
            result = (int)l;
            return true;
        }
        if (value != null && int.TryParse(value.ToString(), out var parsed))
        {
            result = parsed;
            return true;
        }
        return false;
    }

    private static bool TryParseYamlIntLine(string line, string key, out int value)
    {
        value = 0;
        var idx = line.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return false;
        var rest = line.Substring(idx + key.Length).Trim();
        return int.TryParse(rest, out value);
    }

}
