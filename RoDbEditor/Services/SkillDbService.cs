using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;

namespace RoDbEditor.Services;

/// <summary>
/// Minimal loader for rAthena skill_db.yml, so we can display skill names.
/// Keys documented in rAthena comments: Id, Name, MaxLevel, etc.
/// </summary>
public sealed class SkillDbService
{
    private readonly Dictionary<int, string> _nameById = new();

    public IReadOnlyDictionary<int, string> NameById => _nameById;

    public void LoadFromDataPath(string? dataPath)
    {
        _nameById.Clear();

        if (string.IsNullOrWhiteSpace(dataPath) || !Directory.Exists(dataPath))
            return;

        var candidates = new[]
        {
            Path.Combine(dataPath, "db", "skill_db.yml"),
            Path.Combine(dataPath, "db", "re", "skill_db.yml"),
            Path.Combine(dataPath, "db", "pre-re", "skill_db.yml"),
            Path.Combine(dataPath, "db", "import", "skill_db.yml"),
        };

        foreach (var file in candidates.Where(File.Exists))
            LoadYaml(file);
    }

    public string? TryGetName(int skillId)
        => _nameById.TryGetValue(skillId, out var name) ? name : null;

    private void LoadYaml(string filePath)
    {
        try
        {
            var text = File.ReadAllText(filePath);

            var deserializer = new DeserializerBuilder()
                .IgnoreUnmatchedProperties()
                .Build();

            var root = deserializer.Deserialize<Dictionary<object, object>>(new System.IO.StringReader(text));
            if (root == null)
                return;

            if (!TryGetValueCaseInsensitive(root, "Body", out var bodyObj))
                return;

            if (bodyObj is not IEnumerable<object> body)
                return;

            foreach (var entryObj in body)
            {
                if (entryObj is not Dictionary<object, object> entry)
                    continue;

                var id = GetInt(entry, "Id", "ID", "SkillId", "SkillID");
                if (id == null)
                    continue;

                var name = GetString(entry, "Name", "AegisName", "Aegis", "SkillName");
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                _nameById[id.Value] = name!;
            }
        }
        catch
        {
            // Optional feature, silent failure is ok.
        }
    }

    private static bool TryGetValueCaseInsensitive(Dictionary<object, object> dict, string key, out object? value)
    {
        foreach (var kv in dict)
        {
            if (kv.Key?.ToString()?.Equals(key, StringComparison.OrdinalIgnoreCase) == true)
            {
                value = kv.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static int? GetInt(Dictionary<object, object> dict, params string[] keys)
    {
        var v = GetValue(dict, keys);
        if (v == null) return null;

        return v switch
        {
            int i => i,
            long l => (int)l,
            string str when int.TryParse(str.Trim(), out var parsed) => parsed,
            _ => null
        };
    }

    private static string? GetString(Dictionary<object, object> dict, params string[] keys)
        => GetValue(dict, keys)?.ToString()?.Trim();

    private static object? GetValue(Dictionary<object, object> dict, params string[] keys)
    {
        foreach (var key in keys)
        {
            foreach (var kv in dict)
            {
                if (kv.Key?.ToString()?.Equals(key, StringComparison.OrdinalIgnoreCase) == true)
                    return kv.Value;
            }
        }
        return null;
    }
}
