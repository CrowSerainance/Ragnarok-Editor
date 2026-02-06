using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RoDbEditor.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RoDbEditor.Services;

public class MobDbService
{
    public const string MobDbFile = "mob_db.yml";

    private readonly List<MobEntry> _mobs = new();
    private string? _dataPath;

    public IReadOnlyList<MobEntry> Mobs => _mobs;
    public string? LastError { get; private set; }

    public void LoadFromDataPath(string? dataPath)
    {
        _dataPath = dataPath;
        _mobs.Clear();
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
        string? path = null;
        var candidates = new[]
        {
            Path.Combine(dataPath, "db", "re", MobDbFile),
            Path.Combine(dataPath, "db", "pre-re", MobDbFile),
            Path.Combine(dataPath, "db", MobDbFile)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                path = candidate;
                break;
            }
        }

        if (path == null)
        {
            LastError = $"mob_db.yml not found in {dataPath}/db/re, db/pre-re, or db";
            Debug.WriteLine(LastError);
            return;
        }

        Debug.WriteLine($"MobDbService: Loading from {path}");

        try
        {
            var yaml = File.ReadAllText(path);

            if (string.IsNullOrWhiteSpace(yaml))
            {
                LastError = $"mob_db.yml is empty: {path}";
                Debug.WriteLine(LastError);
                return;
            }

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var doc = deserializer.Deserialize<Dictionary<object, object>>(new StringReader(yaml));
            if (doc == null)
            {
                LastError = "Failed to deserialize mob_db.yml";
                Debug.WriteLine(LastError);
                return;
            }

            // rAthena uses "Body" with capital B
            if (!doc.TryGetValue("Body", out var bodyObj) && !doc.TryGetValue("body", out bodyObj))
            {
                LastError = $"No Body section in mob_db.yml. Keys found: {string.Join(", ", doc.Keys)}";
                Debug.WriteLine(LastError);
                return;
            }

            if (bodyObj is not List<object> body)
            {
                LastError = $"Body is not a list in mob_db.yml. Type: {bodyObj?.GetType().Name ?? "null"}";
                Debug.WriteLine(LastError);
                return;
            }

            Debug.WriteLine($"MobDbService: Found {body.Count} entries");

            int loadedCount = 0;
            for (int i = 0; i < body.Count; i++)
            {
                if (body[i] is not Dictionary<object, object> entry) continue;
                var mob = ParseEntry(entry);
                if (mob == null) continue;
                mob.SourceFile = MobDbFile;
                mob.SourceIndex = i;
                _mobs.Add(mob);
                loadedCount++;
            }

            _mobs.Sort((a, b) => a.Id.CompareTo(b.Id));
            Debug.WriteLine($"MobDbService: Loaded {loadedCount} mobs");
        }
        catch (Exception ex)
        {
            LastError = $"Error loading mob_db.yml: {ex.Message}";
            Debug.WriteLine(LastError);
        }
    }

    private static MobEntry? ParseEntry(Dictionary<object, object> entry)
    {
        // rAthena uses PascalCase: Id, AegisName, Name, etc.
        if (!entry.TryGetValue("Id", out var idObj) && !entry.TryGetValue("id", out idObj))
            return null;

        var id = Convert.ToInt32(idObj);
        var aegis = GetStr(entry, "AegisName") ?? GetStr(entry, "aegisName") ?? "";
        var name = GetStr(entry, "Name") ?? GetStr(entry, "name") ?? "";

        var mob = new MobEntry
        {
            Id = id,
            AegisName = aegis,
            Name = name,
            Level = GetInt(entry, "Level") ?? GetInt(entry, "level") ?? 1,
            Hp = GetInt(entry, "Hp") ?? GetInt(entry, "hp") ?? 1,
            Sp = GetInt(entry, "Sp") ?? GetInt(entry, "sp") ?? 0,
            BaseExp = GetInt(entry, "BaseExp") ?? GetInt(entry, "baseExp") ?? 0,
            JobExp = GetInt(entry, "JobExp") ?? GetInt(entry, "jobExp") ?? 0,
            MvpExp = GetInt(entry, "MvpExp") ?? GetInt(entry, "mvpExp") ?? 0,
            Attack = GetInt(entry, "Attack") ?? GetInt(entry, "attack") ?? 0,
            Attack2 = GetInt(entry, "Attack2") ?? GetInt(entry, "attack2") ?? 0,
            Defense = GetInt(entry, "Defense") ?? GetInt(entry, "defense") ?? 0,
            MagicDefense = GetInt(entry, "MagicDefense") ?? GetInt(entry, "magicDefense") ?? 0,
            Str = GetInt(entry, "Str") ?? GetInt(entry, "str") ?? 1,
            Agi = GetInt(entry, "Agi") ?? GetInt(entry, "agi") ?? 1,
            Vit = GetInt(entry, "Vit") ?? GetInt(entry, "vit") ?? 1,
            Int = GetInt(entry, "Int") ?? GetInt(entry, "int") ?? 1,
            Dex = GetInt(entry, "Dex") ?? GetInt(entry, "dex") ?? 1,
            Luk = GetInt(entry, "Luk") ?? GetInt(entry, "luk") ?? 1,
            AttackRange = GetInt(entry, "AttackRange") ?? GetInt(entry, "attackRange") ?? 1,
            SkillRange = GetInt(entry, "SkillRange") ?? GetInt(entry, "skillRange") ?? 1,
            ChaseRange = GetInt(entry, "ChaseRange") ?? GetInt(entry, "chaseRange") ?? 1,
            Size = GetStr(entry, "Size") ?? GetStr(entry, "size") ?? "Medium",
            Race = GetStr(entry, "Race") ?? GetStr(entry, "race") ?? "Formless",
            Element = GetStr(entry, "Element") ?? GetStr(entry, "element") ?? "Neutral",
            ElementLevel = GetInt(entry, "ElementLevel") ?? GetInt(entry, "elementLevel") ?? 1,
            WalkSpeed = GetInt(entry, "WalkSpeed") ?? GetInt(entry, "walkSpeed") ?? 200,
            AttackDelay = GetInt(entry, "AttackDelay") ?? GetInt(entry, "attackDelay") ?? 0,
            AttackMotion = GetInt(entry, "AttackMotion") ?? GetInt(entry, "attackMotion") ?? 0,
            DamageMotion = GetInt(entry, "DamageMotion") ?? GetInt(entry, "damageMotion") ?? 0,
            Ai = GetStr(entry, "Ai") ?? GetStr(entry, "ai") ?? "06",
            Class = GetStr(entry, "Class") ?? GetStr(entry, "class") ?? "Normal",
            Drops = ParseDrops(entry, "Drops", "drops"),
            MvpDrops = ParseDrops(entry, "MvpDrops", "mvpDrops")
        };

        return mob;
    }

    private static List<MobDropEntry> ParseDrops(Dictionary<object, object> entry, string keyPascal, string keyCamel)
    {
        var list = new List<MobDropEntry>();
        if (!entry.TryGetValue(keyPascal, out var dropsObj) && !entry.TryGetValue(keyCamel, out dropsObj))
            return list;
        if (dropsObj is not List<object> drops)
            return list;

        foreach (var d in drops)
        {
            if (d is not Dictionary<object, object> dict) continue;
            var item = GetStr(dict, "Item") ?? GetStr(dict, "item") ?? "";
            if (string.IsNullOrEmpty(item)) continue;
            list.Add(new MobDropEntry
            {
                Item = item,
                Rate = GetInt(dict, "Rate") ?? GetInt(dict, "rate") ?? 0,
                StealProtected = GetBool(dict, "StealProtected", "stealProtected"),
                Index = GetInt(dict, "Index") ?? GetInt(dict, "index")
            });
        }
        return list;
    }

    private static bool GetBool(Dictionary<object, object> d, string k1, string k2)
    {
        if (d.TryGetValue(k1, out var v) && v is bool b) return b;
        if (d.TryGetValue(k2, out v) && v is bool b2) return b2;
        return false;
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
        return int.TryParse(v.ToString(), out var n) ? n : null;
    }

    public void SaveMob(MobEntry mob)
    {
        if (string.IsNullOrEmpty(_dataPath) || string.IsNullOrEmpty(mob.SourceFile)) return;

        var path = Path.Combine(_dataPath, "db", "re", mob.SourceFile);
        if (!File.Exists(path)) path = Path.Combine(_dataPath, "db", "pre-re", mob.SourceFile);
        if (!File.Exists(path)) path = Path.Combine(_dataPath, "db", mob.SourceFile);
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

        if (!doc.TryGetValue("Body", out var bodyObj) && !doc.TryGetValue("body", out bodyObj)) return;
        if (bodyObj is not List<object> body || mob.SourceIndex < 0 || mob.SourceIndex >= body.Count) return;

        var entry = new Dictionary<object, object>
        {
            ["Id"] = mob.Id,
            ["AegisName"] = mob.AegisName,
            ["Name"] = mob.Name,
            ["Level"] = mob.Level,
            ["Hp"] = mob.Hp,
            ["Sp"] = mob.Sp,
            ["BaseExp"] = mob.BaseExp,
            ["JobExp"] = mob.JobExp,
            ["Attack"] = mob.Attack,
            ["Attack2"] = mob.Attack2,
            ["Defense"] = mob.Defense,
            ["MagicDefense"] = mob.MagicDefense,
            ["Str"] = mob.Str,
            ["Agi"] = mob.Agi,
            ["Vit"] = mob.Vit,
            ["Int"] = mob.Int,
            ["Dex"] = mob.Dex,
            ["Luk"] = mob.Luk,
            ["AttackRange"] = mob.AttackRange,
            ["SkillRange"] = mob.SkillRange,
            ["ChaseRange"] = mob.ChaseRange,
            ["Size"] = mob.Size,
            ["Race"] = mob.Race,
            ["Element"] = mob.Element,
            ["ElementLevel"] = mob.ElementLevel,
            ["WalkSpeed"] = mob.WalkSpeed,
            ["AttackDelay"] = mob.AttackDelay,
            ["AttackMotion"] = mob.AttackMotion,
            ["DamageMotion"] = mob.DamageMotion,
            ["Ai"] = mob.Ai,
            ["Class"] = mob.Class
        };

        if (mob.MvpExp > 0) entry["MvpExp"] = mob.MvpExp;

        if (mob.Drops.Count > 0)
        {
            var dropsList = new List<object>();
            foreach (var d in mob.Drops)
            {
                var de = new Dictionary<object, object> { ["Item"] = d.Item, ["Rate"] = d.Rate };
                if (d.StealProtected) de["StealProtected"] = true;
                if (d.Index.HasValue) de["Index"] = d.Index.Value;
                dropsList.Add(de);
            }
            entry["Drops"] = dropsList;
        }

        if (mob.MvpDrops.Count > 0)
        {
            var mvpList = new List<object>();
            foreach (var d in mob.MvpDrops)
            {
                var de = new Dictionary<object, object> { ["Item"] = d.Item, ["Rate"] = d.Rate };
                if (d.Index.HasValue) de["Index"] = d.Index.Value;
                mvpList.Add(de);
            }
            entry["MvpDrops"] = mvpList;
        }

        body[mob.SourceIndex] = entry;
        File.WriteAllText(path, serializer.Serialize(doc));
    }

    public IEnumerable<MobEntry> Search(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return _mobs;
        var f = filter.Trim();
        return _mobs.Where(m =>
            m.Id.ToString().Contains(f, StringComparison.OrdinalIgnoreCase) ||
            (m.Name?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (m.AegisName?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false));
    }
}
