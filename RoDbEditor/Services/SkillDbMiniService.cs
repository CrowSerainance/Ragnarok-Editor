using System;
using System.Collections.Generic;
using System.IO;
using RoDbEditor.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RoDbEditor.Services;

/// <summary>
/// Minimal mapping: SkillID -> Skill Name (and a couple of metadata fields).
/// Parses rAthena skill_db.yml (Header/Body YAML DB style).
/// </summary>
public sealed class SkillDbMiniService
{
    private readonly Dictionary<int, SkillMiniEntry> _byId = new();

    public void LoadFromDataPath(string dataPath)
    {
        _byId.Clear();

        if (string.IsNullOrWhiteSpace(dataPath) || !Directory.Exists(dataPath))
            return;

        var candidates = new[]
        {
            Path.Combine(dataPath, "db", "import", "skill_db.yml"),
            Path.Combine(dataPath, "db", "re", "skill_db.yml"),
            Path.Combine(dataPath, "db", "pre-re", "skill_db.yml"),
            Path.Combine(dataPath, "db", "skill_db.yml"),
        };

        foreach (var f in candidates)
        {
            if (!File.Exists(f)) continue;
            TryParseSkillDbYaml(f);
        }
    }

    public string ResolveName(int skillId)
        => _byId.TryGetValue(skillId, out var e) && !string.IsNullOrWhiteSpace(e.Name)
            ? e.Name
            : $"Skill#{skillId}";

    /// <summary>
    /// Returns "Description (AegisName)" for display, e.g. "Meteor Storm (WZ_METEOR)".
    /// Falls back to AegisName only, then "Skill#ID".
    /// </summary>
    public string ResolveDisplayName(int skillId)
    {
        if (!_byId.TryGetValue(skillId, out var e)) return $"Skill#{skillId}";
        if (!string.IsNullOrWhiteSpace(e.Description) && !string.IsNullOrWhiteSpace(e.Name))
            return $"{e.Description} ({e.Name})";
        return !string.IsNullOrWhiteSpace(e.Name) ? e.Name : $"Skill#{skillId}";
    }

    public int Count => _byId.Count;

    /// <summary>
    /// Returns all loaded skill entries for populating dropdowns.
    /// </summary>
    public IReadOnlyDictionary<int, SkillMiniEntry> GetAll() => _byId;

    private void TryParseSkillDbYaml(string filePath)
    {
        try
        {
            var text = File.ReadAllText(filePath);

            // rAthena YAML DB format is:
            // Header: { Type, Version }
            // Body: [ { Id, Name, Description, MaxLevel, ... }, ... ]
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(NullNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var root = deserializer.Deserialize<SkillDbRoot>(text);
            if (root?.Body == null) return;

            foreach (var s in root.Body)
            {
                if (s == null) continue;
                if (s.Id <= 0) continue;

                // Later files (import) should override earlier mapping
                _byId[s.Id] = s;
            }
        }
        catch
        {
            // keep editor alive; missing skill names just fallback to numeric
        }
    }

    private sealed class SkillDbRoot
    {
        public Dictionary<string, object>? Header { get; set; }
        public List<SkillMiniEntry>? Body { get; set; }
    }
}
