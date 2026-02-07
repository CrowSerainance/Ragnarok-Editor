using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoDbEditor.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RoDbEditor.Services;

/// <summary>
/// Loads and manages item_db from rAthena YAML, OR from GRF iteminfo.lub as fallback.
/// </summary>
public class ItemDbService
{
    private List<ItemEntry> _items = new();
    private bool _loadedFromYaml;
    private string? _dataPath;

    public IReadOnlyList<ItemEntry> Items => _items;
    public string? LastError { get; private set; }
    public bool IsLoadedFromYaml => _loadedFromYaml;

    /// <summary>
    /// Load items from GRF's iteminfo.lub (when no data folder is available).
    /// </summary>
    public void LoadFromGrfData(byte[]? iteminfoLubData)
    {
        _items.Clear();
        _loadedFromYaml = false;
        LastError = null;

        if (iteminfoLubData == null || iteminfoLubData.Length == 0)
        {
            LastError = "No iteminfo.lub data provided";
            return;
        }

        try
        {
            _items = ItemInfoLubParser.ParseItemEntriesFromData(iteminfoLubData);
            System.Diagnostics.Debug.WriteLine($"[ItemDbService] Loaded {_items.Count} items from GRF iteminfo.lub");
        }
        catch (Exception ex)
        {
            LastError = $"Failed to parse iteminfo.lub: {ex.Message}";
        }
    }

    /// <summary>
    /// Load items from rAthena data path (YAML files).
    /// Loads ALL item_db files: item_db.yml, item_db_equip.yml, item_db_etc.yml
    /// </summary>
    public void LoadFromDataPath(string dataPath)
    {
        _items.Clear();
        _loadedFromYaml = false;
        _dataPath = dataPath;
        LastError = null;

        if (string.IsNullOrEmpty(dataPath) || !Directory.Exists(dataPath))
        {
            LastError = $"Data path does not exist: {dataPath}";
            return;
        }

        // Try different database folder structures
        var dbFolders = new[]
        {
            Path.Combine(dataPath, "db", "re"),
            Path.Combine(dataPath, "db", "pre-re"),
            Path.Combine(dataPath, "db"),
            dataPath
        };

        // Item database files to load (in order)
        var itemDbFiles = new[]
        {
            "item_db.yml",
            "item_db_equip.yml",
            "item_db_etc.yml",
            "item_db_usable.yml"
        };

        int totalLoaded = 0;
        string? foundDbFolder = null;

        foreach (var dbFolder in dbFolders)
        {
            if (!Directory.Exists(dbFolder))
                continue;

            // Check if this folder has item_db files
            bool hasItemDb = itemDbFiles.Any(f => File.Exists(Path.Combine(dbFolder, f)));
            if (!hasItemDb)
                continue;

            foundDbFolder = dbFolder;

            // Load all item_db files from this folder
            foreach (var itemDbFile in itemDbFiles)
            {
                var path = Path.Combine(dbFolder, itemDbFile);
                if (File.Exists(path))
                {
                    int before = _items.Count;
                    TryLoadYaml(path);
                    int loaded = _items.Count - before;
                    totalLoaded += loaded;
                    System.Diagnostics.Debug.WriteLine($"[ItemDbService] Loaded {loaded} items from {itemDbFile}");
                }
            }

            // Also load import folder items (these override base items)
            var importFolder = Path.Combine(dataPath, "db", "import");
            if (Directory.Exists(importFolder))
            {
                var importItemDb = Path.Combine(importFolder, "item_db.yml");
                if (File.Exists(importItemDb))
                {
                    int before = _items.Count;
                    TryLoadYaml(importItemDb);
                    int loaded = _items.Count - before;
                    totalLoaded += loaded;
                    System.Diagnostics.Debug.WriteLine($"[ItemDbService] Loaded {loaded} items from import/item_db.yml");
                }
            }

            break; // Found and loaded from a valid db folder
        }

        if (_items.Count > 0)
        {
            _loadedFromYaml = true;
            System.Diagnostics.Debug.WriteLine($"[ItemDbService] Total items loaded: {_items.Count} from {foundDbFolder}");
        }
        else
        {
            LastError = "No valid item_db.yml found in data path";
        }
    }

    private void TryLoadYaml(string path)
    {
        try
        {
            var yaml = File.ReadAllText(path);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(PascalCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var doc = deserializer.Deserialize<ItemDbDocument>(yaml);
            if (doc?.Body != null)
            {
                foreach (var item in doc.Body)
                {
                    item.SourceFile = path;

                    // Check if item already exists (for import overrides)
                    var existing = _items.FindIndex(i => i.Id == item.Id);
                    if (existing >= 0)
                    {
                        _items[existing] = item; // Replace with import version
                    }
                    else
                    {
                        _items.Add(item);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ItemDbService] YAML parse error in {path}: {ex.Message}");
            if (string.IsNullOrEmpty(LastError))
                LastError = $"YAML parse error: {ex.Message}";
        }
    }

    public IEnumerable<ItemEntry> Search(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return _items;

        var f = filter.Trim();
        return _items.Where(i =>
            i.Name.Contains(f, StringComparison.OrdinalIgnoreCase) ||
            i.AegisName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
            i.Id.ToString().Contains(f));
    }

    public ItemEntry? GetById(int id)
    {
        return _items.FirstOrDefault(i => i.Id == id);
    }

    public ItemEntry? GetByAegisName(string aegisName)
    {
        return _items.FirstOrDefault(i =>
            string.Equals(i.AegisName, aegisName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Add item to in-memory list (for newly created items).
    /// The item should already have SourceFile set.
    /// </summary>
    public void AddItem(ItemEntry item)
    {
        var existing = _items.FindIndex(i => i.Id == item.Id);
        if (existing >= 0)
            _items[existing] = item;
        else
            _items.Add(item);
    }

    /// <summary>
    /// Get the next available custom item ID (50000+ range).
    /// </summary>
    public int GetNextCustomItemId()
    {
        int maxCustom = 49999;
        foreach (var item in _items)
        {
            if (item.Id >= 50000 && item.Id > maxCustom)
                maxCustom = item.Id;
        }
        return maxCustom + 1;
    }

    /// <summary>
    /// Save an item to its YAML source file. Handles both editing existing items
    /// and appending new items to db/import/item_db.yml.
    /// </summary>
    public void SaveItem(ItemEntry item)
    {
        if (item == null) return;

        // Determine the target file path
        var path = ResolveItemFilePath(item);
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(PascalCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            var serializer = new SerializerBuilder()
                .WithNamingConvention(PascalCaseNamingConvention.Instance)
                .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitDefaults)
                .Build();

            var yaml = File.ReadAllText(path);
            var doc = deserializer.Deserialize<Dictionary<object, object>>(new StringReader(yaml));
            if (doc == null) doc = new Dictionary<object, object>();

            // Get or create Body list
            List<object> body;
            if (doc.TryGetValue("Body", out var bodyObj) && bodyObj is List<object> existingBody)
            {
                body = existingBody;
            }
            else
            {
                body = new List<object>();
                doc["Body"] = body;
            }

            // Build the YAML entry dictionary
            var entry = BuildItemEntry(item);

            if (item.SourceIndex >= 0 && item.SourceIndex < body.Count)
            {
                // Update existing entry
                body[item.SourceIndex] = entry;
            }
            else
            {
                // Append new entry
                item.SourceIndex = body.Count;
                item.SourceFile = Path.GetFileName(path);
                body.Add(entry);
            }

            // Ensure Header exists
            if (!doc.ContainsKey("Header"))
            {
                doc["Header"] = new Dictionary<object, object>
                {
                    ["Type"] = "ITEM_DB",
                    ["Version"] = 3
                };
            }

            File.WriteAllText(path, serializer.Serialize(doc));
            System.Diagnostics.Debug.WriteLine($"[ItemDbService] Saved item {item.Id} ({item.AegisName}) to {path}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ItemDbService] SaveItem error: {ex.Message}");
            LastError = $"SaveItem failed: {ex.Message}";
        }
    }

    private string? ResolveItemFilePath(ItemEntry item)
    {
        // If item already has a known source file, find it
        if (!string.IsNullOrEmpty(item.SourceFile))
        {
            // SourceFile may be just filename or full path
            if (File.Exists(item.SourceFile))
                return item.SourceFile;

            // Search common locations
            var candidates = GetDbFolders()
                .Select(f => Path.Combine(f, item.SourceFile))
                .Where(File.Exists);
            var found = candidates.FirstOrDefault();
            if (found != null) return found;
        }

        // For new items or unknown source, use db/import/item_db.yml (custom items file)
        var importPath = GetImportItemDbPath();
        if (importPath != null) return importPath;

        // Fallback: use first available item_db.yml
        foreach (var folder in GetDbFolders())
        {
            var path = Path.Combine(folder, "item_db.yml");
            if (File.Exists(path)) return path;
        }

        return null;
    }

    private string? GetImportItemDbPath()
    {
        if (string.IsNullOrEmpty(_dataPath)) return null;
        var importPath = Path.Combine(_dataPath, "db", "import", "item_db.yml");
        return File.Exists(importPath) ? importPath : null;
    }

    private IEnumerable<string> GetDbFolders()
    {
        if (string.IsNullOrEmpty(_dataPath)) yield break;
        yield return Path.Combine(_dataPath, "db", "re");
        yield return Path.Combine(_dataPath, "db", "pre-re");
        yield return Path.Combine(_dataPath, "db");
        yield return Path.Combine(_dataPath, "db", "import");
    }

    private static Dictionary<object, object> BuildItemEntry(ItemEntry item)
    {
        var entry = new Dictionary<object, object>();
        entry["Id"] = item.Id;
        entry["AegisName"] = item.AegisName;
        entry["Name"] = item.Name;

        if (!string.IsNullOrEmpty(item.Type) && item.Type != "Etc")
            entry["Type"] = item.Type;
        if (!string.IsNullOrEmpty(item.SubType))
            entry["SubType"] = item.SubType;
        if (item.Buy.HasValue && item.Buy.Value > 0)
            entry["Buy"] = item.Buy.Value;
        if (item.Sell.HasValue && item.Sell.Value > 0)
            entry["Sell"] = item.Sell.Value;
        if (item.Weight.HasValue && item.Weight.Value > 0)
            entry["Weight"] = item.Weight.Value;
        if (item.Attack.HasValue && item.Attack.Value > 0)
            entry["Attack"] = item.Attack.Value;
        if (item.MagicAttack.HasValue && item.MagicAttack.Value > 0)
            entry["MagicAttack"] = item.MagicAttack.Value;
        if (item.Defense.HasValue && item.Defense.Value > 0)
            entry["Defense"] = item.Defense.Value;
        if (item.Range.HasValue && item.Range.Value > 0)
            entry["Range"] = item.Range.Value;
        if (item.Slots.HasValue && item.Slots.Value > 0)
            entry["Slots"] = item.Slots.Value;

        if (item.Jobs.Count > 0)
        {
            var jobs = new Dictionary<object, object>();
            foreach (var kvp in item.Jobs) jobs[kvp.Key] = kvp.Value;
            entry["Jobs"] = jobs;
        }

        if (item.Classes.Count > 0)
        {
            var classes = new Dictionary<object, object>();
            foreach (var kvp in item.Classes) classes[kvp.Key] = kvp.Value;
            entry["Classes"] = classes;
        }

        if (item.Gender != "Both" && !string.IsNullOrEmpty(item.Gender))
            entry["Gender"] = item.Gender;

        if (item.Locations.Count > 0)
        {
            var locs = new Dictionary<object, object>();
            foreach (var kvp in item.Locations) locs[kvp.Key] = kvp.Value;
            entry["Locations"] = locs;
        }

        if (item.WeaponLevel.HasValue && item.WeaponLevel.Value > 0)
            entry["WeaponLevel"] = item.WeaponLevel.Value;
        if (item.ArmorLevel.HasValue && item.ArmorLevel.Value > 0)
            entry["ArmorLevel"] = item.ArmorLevel.Value;
        if (item.EquipLevelMin.HasValue && item.EquipLevelMin.Value > 0)
            entry["EquipLevelMin"] = item.EquipLevelMin.Value;
        if (item.EquipLevelMax.HasValue && item.EquipLevelMax.Value > 0)
            entry["EquipLevelMax"] = item.EquipLevelMax.Value;
        if (item.Refineable)
            entry["Refineable"] = true;
        if (item.Gradable)
            entry["Gradable"] = true;
        if (item.View.HasValue && item.View.Value > 0)
            entry["View"] = item.View.Value;
        if (!string.IsNullOrEmpty(item.AliasName))
            entry["AliasName"] = item.AliasName;
        if (!string.IsNullOrEmpty(item.Script))
            entry["Script"] = item.Script;
        if (!string.IsNullOrEmpty(item.EquipScript))
            entry["EquipScript"] = item.EquipScript;
        if (!string.IsNullOrEmpty(item.UnEquipScript))
            entry["UnEquipScript"] = item.UnEquipScript;

        return entry;
    }

    private class ItemDbDocument
    {
        public List<ItemEntry>? Body { get; set; }
    }
}
