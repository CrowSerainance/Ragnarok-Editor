using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using RoDbEditor.Models;

namespace RoDbEditor.Services;

public sealed class MobSkillDbService
{
    private readonly List<MobSkillDbRow> _all = new();
    private readonly Dictionary<int, List<MobSkillDbRow>> _byMob = new();

    public IReadOnlyList<MobSkillDbRow> All => _all;

    public void LoadFromDataPath(string dataPath)
    {
        _all.Clear();
        _byMob.Clear();

        if (string.IsNullOrWhiteSpace(dataPath) || !Directory.Exists(dataPath))
            return;

        // Priority (import overrides first, then re, then pre-re, then legacy)
        var candidates = new[]
        {
            Path.Combine(dataPath, "db", "import", "mob_skill_db.txt"),
            Path.Combine(dataPath, "db", "re", "mob_skill_db.txt"),
            Path.Combine(dataPath, "db", "pre-re", "mob_skill_db.txt"),
            Path.Combine(dataPath, "db", "mob_skill_db.txt"),
        };

        foreach (var file in candidates)
        {
            if (!File.Exists(file)) continue;
            ParseFile(file);
        }

        // Index
        foreach (var row in _all)
        {
            if (!_byMob.TryGetValue(row.MobId, out var list))
            {
                list = new List<MobSkillDbRow>();
                _byMob[row.MobId] = list;
            }
            list.Add(row);
        }

        // Stable ordering for UI
        foreach (var kv in _byMob)
        {
            kv.Value.Sort((a, b) =>
            {
                var s = string.Compare(a.State, b.State, StringComparison.OrdinalIgnoreCase);
                if (s != 0) return s;
                var sk = a.SkillId.CompareTo(b.SkillId);
                if (sk != 0) return sk;
                return a.SourceLine.CompareTo(b.SourceLine);
            });
        }
    }

    public IReadOnlyList<MobSkillDbRow> GetRowsForMob(int mobId)
        => _byMob.TryGetValue(mobId, out var list) ? list : Array.Empty<MobSkillDbRow>();

    private void ParseFile(string filePath)
    {
        int lineNo = 0;
        foreach (var raw in File.ReadLines(filePath))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("//")) continue;

            // CSV-like: fields separated by commas, no fancy quoting in most official db,
            // BUT we still handle minimal quotes safely.
            var fields = SplitCsvPreserveQuotes(raw);
            if (fields.Count < 19) continue;

            // Chat may contain commas -> treat "extra fields" as Chat tail
            // Expected layout:
            // 0 MobID, 1 Dummy, 2 State, 3 SkillID, 4 SkillLv, 5 Rate, 6 CastTime, 7 Delay, 8 Cancel, 9 Target,
            // 10 CondType, 11 CondVal, 12 Val1, 13 Val2, 14 Val3, 15 Val4, 16 Val5, 17 Emotion, 18 Chat
            if (fields.Count > 19)
            {
                fields[18] = string.Join(",", fields.Skip(18));
                fields.RemoveRange(19, fields.Count - 19);
            }

            if (!TryInt(fields[0], out var mobId)) continue;
            if (!TryInt(fields[3], out var skillId)) continue;

            var row = new MobSkillDbRow
            {
                MobId = mobId,
                Dummy = fields[1].Trim(),
                State = fields[2].Trim(),
                SkillId = skillId,
                SkillLv = ToInt(fields[4]),
                Rate = ToInt(fields[5]),
                CastTimeMs = ToInt(fields[6]),
                DelayMs = ToInt(fields[7]),
                Cancelable = ToInt(fields[8]),
                Target = fields[9].Trim(),
                ConditionType = fields[10].Trim(),
                ConditionValue = ToInt(fields[11]),
                Val1 = ToInt(fields[12]),
                Val2 = ToInt(fields[13]),
                Val3 = ToInt(fields[14]),
                Val4 = ToInt(fields[15]),
                Val5 = ToInt(fields[16]),
                Emotion = ToInt(fields[17]),
                Chat = fields[18].Trim(),
                SourceFile = filePath,
                SourceLine = lineNo
            };

            _all.Add(row);
        }
    }

    private static bool TryInt(string s, out int v)
        => int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

    private static int ToInt(string s)
        => int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static List<string> SplitCsvPreserveQuotes(string line)
    {
        var result = new List<string>();
        if (line == null) return result;

        var cur = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (c == ',' && !inQuotes)
            {
                result.Add(cur.ToString());
                cur.Clear();
                continue;
            }

            cur.Append(c);
        }

        result.Add(cur.ToString());
        return result;
    }
}
