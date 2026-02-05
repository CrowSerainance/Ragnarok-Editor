using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoDbEditor.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RoDbEditor.Services;

/// <summary>
/// Loads and saves rAthena-style item_db YAML (item_db_equip.yml, item_db_etc.yml, item_db_use.yml).
/// </summary>
public class ItemDbService
{
    public const string ItemDbEquip = "item_db_equip.yml";
    public const string ItemDbEtc = "item_db_etc.yml";
    public const string ItemDbUse = "item_db_use.yml";

    private static readonly string[] ItemDbFiles = { ItemDbEquip, ItemDbEtc, ItemDbUse };

    private readonly List<ItemEntry> _items = new();
    private readonly Dictionary<string, (string path, int index, object raw)> _sourceMap = new();
    private string? _dataPath;

    public IReadOnlyList<ItemEntry> Items => _items;

    public void LoadFromDataPath(string? dataPath)
    {
        _dataPath = dataPath;
        _items.Clear();
        _sourceMap.Clear();
        if (string.IsNullOrWhiteSpace(dataPath))
            return;

        var dbDir = Path.Combine(dataPath, "db", "re");
        if (!Directory.Exists(dbDir))
            dbDir = Path.Combine(dataPath, "db");

        foreach (var fileName in ItemDbFiles)
        {
            var path = Path.Combine(dbDir, fileName);
            if (!File.Exists(path)) continue;
            LoadOneFile(path, fileName);
        }

        _items.Sort((a, b) => a.Id.CompareTo(b.Id));
    }

    private void LoadOneFile(string path, string fileName)
    {
        try
        {
            var yaml = File.ReadAllText(path);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var doc = deserializer.Deserialize<Dictionary<object, object>>(new StringReader(yaml));
            if (doc == null) return;

            if (!doc.TryGetValue("body", out var bodyObj) && !doc.TryGetValue("Body", out bodyObj))
                return;
            if (bodyObj is not List<object> body)
                return;

            for (int i = 0; i < body.Count; i++)
            {
                var raw = body[i];
                if (raw is not Dictionary<object, object> entry)
                    continue;
                var item = ParseEntry(entry);
                if (item == null) continue;
                item.SourceFile = fileName;
                item.SourceIndex = i;
                _items.Add(item);
                _sourceMap[$"{item.Id}"] = (path, i, raw);
            }
        }
        catch
        {
            // skip file
        }
    }

    private static ItemEntry? ParseEntry(Dictionary<object, object> entry)
    {
        if (!entry.TryGetValue("id", out var idObj) && !entry.TryGetValue("Id", out idObj))
            return null;
        var id = Convert.ToInt32(idObj);
        var aegis = GetStr(entry, "aegisName") ?? GetStr(entry, "AegisName") ?? "";
        var name = GetStr(entry, "name") ?? GetStr(entry, "Name") ?? "";
        var type = GetStr(entry, "type") ?? GetStr(entry, "Type") ?? "Etc";
        var item = new ItemEntry
        {
            Id = id,
            AegisName = aegis,
            Name = name,
            Type = type,
            SubType = GetStr(entry, "subType") ?? GetStr(entry, "SubType"),
            Buy = GetInt(entry, "buy") ?? GetInt(entry, "Buy"),
            Sell = GetInt(entry, "sell") ?? GetInt(entry, "Sell"),
            Weight = GetInt(entry, "weight") ?? GetInt(entry, "Weight"),
            View = GetInt(entry, "view") ?? GetInt(entry, "View"),
            Script = GetStrOrMultiline(entry, "script") ?? GetStrOrMultiline(entry, "Script"),
            EquipScript = GetStrOrMultiline(entry, "equipScript") ?? GetStrOrMultiline(entry, "EquipScript"),
            UnEquipScript = GetStrOrMultiline(entry, "unEquipScript") ?? GetStrOrMultiline(entry, "UnEquipScript")
        };
        return item;
    }

    private static string? GetStr(Dictionary<object, object> d, string key)
    {
        if (!d.TryGetValue(key, out var v) || v == null) return null;
        return v.ToString()?.Trim();
    }

    private static int? GetInt(Dictionary<object, object> d, string key)
    {
        if (!d.TryGetValue(key, out var v) || v == null) return null;
        if (v is int i) return i;
        if (int.TryParse(v.ToString(), out var n)) return n;
        return null;
    }

    private static string? GetStrOrMultiline(Dictionary<object, object> d, string key)
    {
        if (!d.TryGetValue(key, out var v) || v == null) return null;
        if (v is string s) return s.Trim();
        return v.ToString()?.Trim();
    }

    public void SaveItem(ItemEntry item)
    {
        if (string.IsNullOrEmpty(item.SourceFile) || string.IsNullOrEmpty(_dataPath))
            return;
        // Full save: re-load file, replace entry at SourceIndex, write back (preserving format is hard; we do a simple round-trip)
        var dbDir = Path.Combine(_dataPath, "db", "re");
        if (!Directory.Exists(dbDir)) dbDir = Path.Combine(_dataPath, "db");
        var path = Path.Combine(dbDir, item.SourceFile);
        if (!File.Exists(path)) return;

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var yaml = File.ReadAllText(path);
        var doc = deserializer.Deserialize<Dictionary<object, object>>(new StringReader(yaml));
        if (doc == null) return;
        if (!doc.TryGetValue("body", out var bodyObj) && !doc.TryGetValue("Body", out bodyObj))
            return;
        if (bodyObj is not List<object> body)
            return;
        if (item.SourceIndex < 0 || item.SourceIndex >= body.Count)
            return;

        var entry = new Dictionary<object, object>
        {
            ["Id"] = item.Id,
            ["AegisName"] = item.AegisName,
            ["Name"] = item.Name,
            ["Type"] = item.Type
        };
        if (!string.IsNullOrEmpty(item.SubType)) entry["SubType"] = item.SubType;
        if (item.Buy.HasValue) entry["Buy"] = item.Buy.Value;
        if (item.Sell.HasValue) entry["Sell"] = item.Sell.Value;
        if (item.Weight.HasValue) entry["Weight"] = item.Weight.Value;
        if (item.View.HasValue) entry["View"] = item.View.Value;
        if (!string.IsNullOrEmpty(item.Script)) entry["Script"] = item.Script;
        if (!string.IsNullOrEmpty(item.EquipScript)) entry["EquipScript"] = item.EquipScript;
        if (!string.IsNullOrEmpty(item.UnEquipScript)) entry["UnEquipScript"] = item.UnEquipScript;

        body[item.SourceIndex] = entry;
        var newYaml = serializer.Serialize(doc);
        File.WriteAllText(path, newYaml);
    }

    public IEnumerable<ItemEntry> Search(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return _items;
        var f = filter.Trim();
        return _items.Where(i =>
            i.Id.ToString().Contains(f, StringComparison.OrdinalIgnoreCase) ||
            (i.Name?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (i.AegisName?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false));
    }
}
