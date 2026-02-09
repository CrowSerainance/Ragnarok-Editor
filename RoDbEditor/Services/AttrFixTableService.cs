using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RoDbEditor.Services;

public sealed class AttrFixTableService
{
    public static readonly string[] ElementNames =
    [
        "Neutral", "Water", "Earth", "Fire", "Wind",
        "Poison", "Holy", "Shadow", "Ghost", "Undead"
    ];

    private readonly Dictionary<int, int[,]> _tablesByLevel = new();
    public bool IsLoaded => _tablesByLevel.Count > 0;

    public static IReadOnlyList<string> Elements => ElementNames;

    public void LoadFromDataPath(string? dataPath)
    {
        _tablesByLevel.Clear();

        if (string.IsNullOrWhiteSpace(dataPath) || !Directory.Exists(dataPath))
            return;

        var txtCandidates = new[]
        {
            Path.Combine(dataPath, "db", "import", "attr_fix.txt"),
            Path.Combine(dataPath, "db", "re", "attr_fix.txt"),
            Path.Combine(dataPath, "db", "pre-re", "attr_fix.txt"),
            Path.Combine(dataPath, "db", "attr_fix.txt"),
        };

        var path = txtCandidates.FirstOrDefault(File.Exists);
        if (path != null)
        {
            try { LoadTxt(path); } catch { }
        }

        if (_tablesByLevel.Count == 0)
        {
            var ymlCandidates = new[]
            {
                Path.Combine(dataPath, "db", "import", "attr_fix.yml"),
                Path.Combine(dataPath, "db", "re", "attr_fix.yml"),
                Path.Combine(dataPath, "db", "pre-re", "attr_fix.yml"),
                Path.Combine(dataPath, "db", "attr_fix.yml"),
            };
            path = ymlCandidates.FirstOrDefault(File.Exists);
            if (path != null)
            {
                try { LoadYaml(path); } catch { }
            }
        }
    }

    private void LoadTxt(string path)
    {
        var lines = File.ReadAllLines(path);
        var headerRe = new Regex(@"^\s*(?<lvl>[1-4])\s*,\s*10\b", RegexOptions.Compiled);
        var intRe = new Regex(@"-?\d+", RegexOptions.Compiled);

        for (int i = 0; i < lines.Length; i++)
        {
            var m = headerRe.Match(lines[i]);
            if (!m.Success) continue;

            var lvl = int.Parse(m.Groups["lvl"].Value, CultureInfo.InvariantCulture);
            var rows = new List<int[]>(capacity: 10);

            for (int j = i + 1; j < lines.Length && rows.Count < 10; j++)
            {
                var line = lines[j].Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("//") || line.StartsWith("#")) continue;

                var ints = intRe.Matches(lines[j]).Select(x => int.Parse(x.Value, CultureInfo.InvariantCulture)).ToArray();
                if (ints.Length < 10) continue;

                rows.Add(ints.Take(10).ToArray());
            }

            if (rows.Count == 10)
            {
                var table = new int[10, 10];
                for (int r = 0; r < 10; r++)
                    for (int c = 0; c < 10; c++)
                        table[r, c] = rows[r][c];

                _tablesByLevel[lvl] = table;
            }
        }
    }

    private void LoadYaml(string path)
    {
        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var doc = deserializer.Deserialize<Dictionary<object, object>>(new StringReader(yaml));
        if (doc == null || !doc.TryGetValue("Body", out var bodyObj) || bodyObj is not List<object> body)
            return;

        foreach (var item in body)
        {
            if (item is not Dictionary<object, object> levelBlock) continue;
            if (!levelBlock.TryGetValue("Level", out var levelObj)) continue;

            var level = Convert.ToInt32(levelObj);
            var matrix = new int[10, 10];

            foreach (var elem in ElementNames)
            {
                var key = levelBlock.Keys.Cast<object>().FirstOrDefault(k => string.Equals(k?.ToString(), elem, StringComparison.OrdinalIgnoreCase));
                if (key == null) continue;
                if (!levelBlock.TryGetValue(key, out var attackerObj) || attackerObj is not Dictionary<object, object> targets)
                    continue;

                if (!TryGetElementIndex(elem, out var attackerIdx)) continue;

                foreach (var targetElem in ElementNames)
                {
                    var tKey = targets.Keys.Cast<object>().FirstOrDefault(k => string.Equals(k?.ToString(), targetElem, StringComparison.OrdinalIgnoreCase));
                    if (tKey == null) continue;
                    if (!targets.TryGetValue(tKey, out var valObj)) continue;
                    if (!TryGetElementIndex(targetElem, out var defenderIdx)) continue;

                    var val = Convert.ToInt32(valObj);
                    matrix[attackerIdx, defenderIdx] = val;
                }
            }
            _tablesByLevel[level] = matrix;
        }
    }

    public int GetDamagePercent(string attackerElement, string defenderElement, int defenderLevel)
    {
        if (!TryGetElementIndex(attackerElement, out var atk) ||
            !TryGetElementIndex(defenderElement, out var def))
            return 100;

        if (defenderLevel is < 1 or > 4) defenderLevel = 1;

        if (_tablesByLevel.TryGetValue(defenderLevel, out var table))
            return table[atk, def];

        return 100;
    }

    public int GetDamagePercent(int attackerElementIndex, int defenderElementIndex, int defenderLevel)
    {
        attackerElementIndex = Math.Clamp(attackerElementIndex, 0, 9);
        defenderElementIndex = Math.Clamp(defenderElementIndex, 0, 9);
        if (defenderLevel is < 1 or > 4) defenderLevel = 1;

        if (_tablesByLevel.TryGetValue(defenderLevel, out var table))
            return table[attackerElementIndex, defenderElementIndex];

        return 100;
    }

    public static bool TryGetElementIndex(string element, out int index)
    {
        index = -1;
        if (string.IsNullOrWhiteSpace(element)) return false;

        for (int i = 0; i < ElementNames.Length; i++)
        {
            if (string.Equals(ElementNames[i], element.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                return true;
            }
        }
        if (string.Equals("Dark", element.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            index = 7;
            return true;
        }
        return false;
    }
}
