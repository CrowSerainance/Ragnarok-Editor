using System;
using System.Collections.Generic;
using System.Linq;
using RoDbEditor.Models;

namespace RoDbEditor.Services;

/// <summary>
/// Turns mob_skill_db rows into RMS-like UI rows.
/// Also extracts "Slaves" based on NPC_SUMMONSLAVE patterns.
/// </summary>
public sealed class MobSkillPanelService
{
    private readonly MobSkillDbService _mobSkills;
    private readonly SkillDbMiniService _skills;
    private readonly MobDbService _mobs; // for MobID -> Name (slaves)

    public MobSkillPanelService(MobSkillDbService mobSkills, SkillDbMiniService skills, MobDbService mobs)
    {
        _mobSkills = mobSkills;
        _skills = skills;
        _mobs = mobs;
    }

    public List<MonsterSkillRow> BuildMonsterSkills(MobEntry mob)
    {
        if (mob == null) return new List<MonsterSkillRow>();

        var rows = ResolveApplicableRows(mob);

        return rows.Select(r =>
        {
            var name = _skills.ResolveDisplayName(r.SkillId);
            return new MonsterSkillRow
            {
                Skill = name,
                SkillId = r.SkillId,
                SkillLv = r.SkillLv,
                State = r.State,
                Target = r.Target,
                Rate = FormatRate(r.Rate),
                Cast = FormatMs(r.CastTimeMs),
                Delay = FormatMs(r.DelayMs),
                Condition = FormatCondition(r.ConditionType, r.ConditionValue, r.Val1),
                Source = $"{System.IO.Path.GetFileName(r.SourceFile)}:{r.SourceLine}",
                SourceRow = r
            };
        }).ToList();
    }

    public List<MonsterSlaveRow> BuildSlaves(MobEntry mob)
    {
        if (mob == null) return new List<MonsterSlaveRow>();

        var rows = ResolveApplicableRows(mob);

        // "NPC_SUMMONSLAVE"/"NPC_CALLSLAVE*": Val1..Val5 are slave mob IDs; SkillLv is count per slave type.
        // If we can't resolve the skill name, we still try a best-effort match by string/id.
        var slaves = new List<MonsterSlaveRow>();

        foreach (var r in rows)
        {
            var skillName = _skills.ResolveName(r.SkillId);

            // rAthena slave-summon skills are NPC_SUMMONSLAVE* and NPC_CALLSLAVE* (recall slaves).
            // We prefer name-based detection (via skill_db.yml), with a fallback on known IDs.
            bool isSummonOrCallByName =
                skillName.StartsWith("NPC_SUMMONSLAVE", StringComparison.OrdinalIgnoreCase) ||
                skillName.StartsWith("NPC_CALLSLAVE", StringComparison.OrdinalIgnoreCase);

            // Known IDs in official skill_db: 196 = NPC_SUMMONSLAVE. Some forks also alias 197.
            bool isSummonOrCallById = r.SkillId == 196 || r.SkillId == 197;

            if (!isSummonOrCallByName && !isSummonOrCallById)
                continue;

            var ids = new[] { r.Val1, r.Val2, r.Val3, r.Val4, r.Val5 }
                .Where(x => x > 0)
                .ToArray();

            if (ids.Length == 0)
                continue;

            // rAthena summons `amount` (SkillLv) slaves total in round-robin across
            // the slave type array. E.g. amount=16, ids=[A,B,C] → 6A, 5B, 5C.
            int totalAmount = Math.Max(1, r.SkillLv);
            for (int i = 0; i < ids.Length; i++)
            {
                var slaveId = ids[i];
                var slave = _mobs.GetMobById(slaveId);
                var slaveName = slave != null ? slave.Name : $"Mob#{slaveId}";

                // Round-robin count: total/types + 1 for the first (total%types) slots
                int countForThis = totalAmount / ids.Length + (i < totalAmount % ids.Length ? 1 : 0);

                // Condition text (\"When\") should include both the mob_skill_db condition
                // and the state (idle/walk/chase/attack) so it reads RMS-style:
                //   \"On spawn/respawn (chase)\", \"When rude attacked (attack)\", etc.
                var whenText = FormatCondition(r.ConditionType, r.ConditionValue, r.Val1);
                if (!string.IsNullOrWhiteSpace(r.State))
                    whenText = $"{whenText} ({r.State})";

                slaves.Add(new MonsterSlaveRow
                {
                    MobId = slaveId,
                    MobName = slaveName,
                    Count = countForThis,
                    When = whenText,
                    Source = $"{System.IO.Path.GetFileName(r.SourceFile)}:{r.SourceLine}",
                    SourceRow = r
                });
            }
        }

        // Combine duplicates (same slaveId)
        return slaves
            .GroupBy(s => s.MobId)
            .Select(g =>
            {
                var first = g.First();
                return new MonsterSlaveRow
                {
                    MobId = first.MobId,
                    MobName = first.MobName,
                    Count = g.Max(x => x.Count), // typical: same count per type
                    When = string.Join(" | ", g.Select(x => x.When).Distinct()),
                    Source = string.Join(", ", g.Select(x => x.Source).Distinct()),
                    SourceRow = first.SourceRow
                };
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.MobName)
            .ToList();
    }

    // rAthena global mob_skill_db rules:
    // -3 all mobs, -2 normal, -1 boss. "Boss" detection uses mob.Class + modes.
    private List<MobSkillDbRow> ResolveApplicableRows(MobEntry mob)
    {
        var list = new List<MobSkillDbRow>();
        list.AddRange(_mobSkills.GetRowsForMob(mob.Id));

        list.AddRange(_mobSkills.GetRowsForMob(-3));

        var isBoss = IsBossMob(mob);
        list.AddRange(_mobSkills.GetRowsForMob(isBoss ? -1 : -2));

        // RMS-ish: stable sorting
        list.Sort((a, b) =>
        {
            var s = string.Compare(a.State, b.State, StringComparison.OrdinalIgnoreCase);
            if (s != 0) return s;
            return a.SkillId.CompareTo(b.SkillId);
        });

        return list;
    }

    private static bool IsBossMob(MobEntry mob)
    {
        if (!string.IsNullOrWhiteSpace(mob.Class) &&
            mob.Class.Equals("Boss", StringComparison.OrdinalIgnoreCase))
            return true;

        // Modes dictionary exists in your MobEntry; boss commonly appears as mode
        if (mob.Modes != null && mob.Modes.TryGetValue("Boss", out var b) && b)
            return true;

        return false;
    }

    private static string FormatRate(int rate)
        => $"{(rate / 100.0):0.00}%"; // 10000 => 100.00%

    private static string FormatMs(int ms)
    {
        if (ms <= 0) return "0s";
        var s = ms / 1000.0;
        return $"{s:0.###}s";
    }

    private static string FormatCondition(string type, int val, int? val1)
    {
        return MobSkillConditionText.Format(type, val, val1);
    }
}
