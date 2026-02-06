using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    public string? LastError { get; private set; }

    public void LoadFromDataPath(string? dataPath)
    {
        _dataPath = dataPath;
        _items.Clear();
        _sourceMap.Clear();
        LastError = null;

        if (string.IsNullOrWhiteSpace(dataPath))
        {
            LastError = "Data path is empty";
            return;
        }

        if (!Directory.Exists(dataPath))
        {
            LastError = $"Data path does not exist: {dataPath}";
            return;
        }

        // Try db/re first (Renewal), then db/pre-re (Pre-Renewal), then db (fallback)
        var dbDirs = new[]
        {
            Path.Combine(dataPath, "db", "re"),
            Path.Combine(dataPath, "db", "pre-re"),
            Path.Combine(dataPath, "db")
        };

        string? dbDir = null;
        foreach (var dir in dbDirs)
        {
            if (Directory.Exists(dir))
            {
                // Check if any item_db file exists in this directory
                if (ItemDbFiles.Any(f => File.Exists(Path.Combine(dir, f))))
                {
                    dbDir = dir;
                    break;
                }
            }
        }

        if (dbDir == null)
        {
            LastError = $"No item_db files found in {dataPath}/db/re, db/pre-re, or db";
            Debug.WriteLine(LastError);
            return;
        }

        Debug.WriteLine($"ItemDbService: Loading from {dbDir}");

        int filesLoaded = 0;
        foreach (var fileName in ItemDbFiles)
        {
            var path = Path.Combine(dbDir, fileName);
            if (!File.Exists(path))
            {
                Debug.WriteLine($"ItemDbService: File not found: {path}");
                continue;
            }

            if (LoadOneFile(path, fileName))
                filesLoaded++;
        }

        if (filesLoaded == 0)
        {
            LastError = $"No item_db files could be loaded from {dbDir}";
        }

        _items.Sort((a, b) => a.Id.CompareTo(b.Id));
        Debug.WriteLine($"ItemDbService: Loaded {_items.Count} items from {filesLoaded} files");
    }

    private bool LoadOneFile(string path, string fileName)
    {
        try
        {
            Debug.WriteLine($"ItemDbService: Reading {path}");
            var yaml = File.ReadAllText(path);

            if (string.IsNullOrWhiteSpace(yaml))
            {
                Debug.WriteLine($"ItemDbService: File is empty: {path}");
                return false;
            }

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var doc = deserializer.Deserialize<Dictionary<object, object>>(new StringReader(yaml));
            if (doc == null)
            {
                Debug.WriteLine($"ItemDbService: Failed to deserialize YAML: {path}");
                return false;
            }

            // rAthena uses "Body" with capital B
            if (!doc.TryGetValue("Body", out var bodyObj) && !doc.TryGetValue("body", out bodyObj))
            {
                Debug.WriteLine($"ItemDbService: No Body section in {path}. Keys found: {string.Join(", ", doc.Keys)}");
                return false;
            }

            if (bodyObj is not List<object> body)
            {
                Debug.WriteLine($"ItemDbService: Body is not a list in {path}. Type: {bodyObj?.GetType().Name ?? "null"}");
                return false;
            }

            Debug.WriteLine($"ItemDbService: Found {body.Count} entries in {fileName}");

            int loadedCount = 0;
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
                loadedCount++;
            }

            Debug.WriteLine($"ItemDbService: Successfully parsed {loadedCount} items from {fileName}");
            return loadedCount > 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ItemDbService: Error loading {path}: {ex.Message}");
            LastError = $"Error loading {fileName}: {ex.Message}";
            return false;
        }
    }

    private static ItemEntry? ParseEntry(Dictionary<object, object> entry)
    {
        // rAthena uses PascalCase: Id, AegisName, Name, Type, etc.
        if (!entry.TryGetValue("Id", out var idObj) && !entry.TryGetValue("id", out idObj))
            return null;

        var id = Convert.ToInt32(idObj);
        var aegis = GetStr(entry, "AegisName") ?? GetStr(entry, "aegisName") ?? "";
        var name = GetStr(entry, "Name") ?? GetStr(entry, "name") ?? "";
        var type = GetStr(entry, "Type") ?? GetStr(entry, "type") ?? "Etc";

        var item = new ItemEntry
        {
            Id = id,
            AegisName = aegis,
            Name = name,
            Type = type,
            SubType = GetStr(entry, "SubType") ?? GetStr(entry, "subType"),
            Buy = GetInt(entry, "Buy") ?? GetInt(entry, "buy"),
            Sell = GetInt(entry, "Sell") ?? GetInt(entry, "sell"),
            Weight = GetInt(entry, "Weight") ?? GetInt(entry, "weight"),
            Attack = GetInt(entry, "Attack") ?? GetInt(entry, "attack"),
            MagicAttack = GetInt(entry, "MagicAttack") ?? GetInt(entry, "magicAttack"),
            Defense = GetInt(entry, "Defense") ?? GetInt(entry, "defense"),
            Range = GetInt(entry, "Range") ?? GetInt(entry, "range"),
            Slots = GetInt(entry, "Slots") ?? GetInt(entry, "slots"),
            WeaponLevel = GetInt(entry, "WeaponLevel") ?? GetInt(entry, "weaponLevel"),
            ArmorLevel = GetInt(entry, "ArmorLevel") ?? GetInt(entry, "armorLevel"),
            View = GetInt(entry, "View") ?? GetInt(entry, "view"),
            Script = GetStrOrMultiline(entry, "Script") ?? GetStrOrMultiline(entry, "script"),
            EquipScript = GetStrOrMultiline(entry, "EquipScript") ?? GetStrOrMultiline(entry, "equipScript"),
            UnEquipScript = GetStrOrMultiline(entry, "UnEquipScript") ?? GetStrOrMultiline(entry, "unEquipScript")
        };

        // Parse EquipLevelMin/Max from EquipLevel object or direct values
        if (entry.TryGetValue("EquipLevel", out var equipLevel) || entry.TryGetValue("equipLevel", out equipLevel))
        {
            if (equipLevel is Dictionary<object, object> equipDict)
            {
                item.EquipLevelMin = GetInt(equipDict, "Min") ?? GetInt(equipDict, "min");
                item.EquipLevelMax = GetInt(equipDict, "Max") ?? GetInt(equipDict, "max");
            }
            else if (equipLevel is int lvl)
            {
                item.EquipLevelMin = lvl;
            }
        }

        // Parse Flags
        if (entry.TryGetValue("Flags", out var flagsObj) || entry.TryGetValue("flags", out flagsObj))
        {
            if (flagsObj is Dictionary<object, object> flags)
            {
                item.Refineable = GetBool(flags, "Refineable") || GetBool(flags, "refineable");
                item.Gradable = GetBool(flags, "Gradable") || GetBool(flags, "gradable");
            }
        }

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
        if (v is long l) return (int)l;
        if (int.TryParse(v.ToString(), out var n)) return n;
        return null;
    }

    private static bool GetBool(Dictionary<object, object> d, string key)
    {
        if (!d.TryGetValue(key, out var v) || v == null) return false;
        if (v is bool b) return b;
        if (bool.TryParse(v.ToString(), out var result)) return result;
        return false;
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

        var dbDir = Path.Combine(_dataPath, "db", "re");
        if (!Directory.Exists(dbDir)) dbDir = Path.Combine(_dataPath, "db", "pre-re");
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

        if (!doc.TryGetValue("Body", out var bodyObj) && !doc.TryGetValue("body", out bodyObj))
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
        if (item.Attack.HasValue) entry["Attack"] = item.Attack.Value;
        if (item.Defense.HasValue) entry["Defense"] = item.Defense.Value;
        if (item.Range.HasValue) entry["Range"] = item.Range.Value;
        if (item.Slots.HasValue) entry["Slots"] = item.Slots.Value;
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
