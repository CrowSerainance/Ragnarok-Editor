using System;
using System.Collections.Generic;

namespace RoDbEditor.Services;

internal static class MobSkillConditionText
{
    // Canonical condition keys in mob_skill_db are usually lowercase.
    // Sources: rAthena + Hercules condition docs.
    // Note: Some servers use 'onspawn' vs 'spawn'—support both.
    private static readonly Dictionary<string, Func<int, int?, string>> _fmt =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["always"]            = (v, v1) => "Unconditional",
            ["onspawn"]           = (v, v1) => "On spawn/respawn",
            ["spawn"]             = (v, v1) => "On spawn/respawn",

            ["myhpltmaxrate"]     = (v, v1) => $"HP < {v}%",
            // friendhpinrate: CondVal = lower bound, Val1 = upper bound (rAthena).
            ["myhpinrate"]        = (v, v1) => v1.HasValue ? $"HP in {v}%–{v1.Value}%" : $"HP in ≥ {v}%",

            ["mystatuson"]        = (v, v1) => $"Self status ON: {StatusNameOrId(v)}",
            ["mystatusoff"]       = (v, v1) => $"Self status OFF: {StatusNameOrId(v)}",

            ["friendhpltmaxrate"] = (v, v1) => $"Friend HP < {v}%",
            ["friendhpinrate"]    = (v, v1) => v1.HasValue ? $"Friend HP in {v}%–{v1.Value}%" : $"Friend HP in ≥ {v}%",
            ["friendstatuson"]    = (v, v1) => $"Friend status ON: {StatusNameOrId(v)}",
            ["friendstatusoff"]   = (v, v1) => $"Friend status OFF: {StatusNameOrId(v)}",

            ["attackpcgt"]        = (v, v1) => $"Attackers > {v}",
            ["attackpcge"]        = (v, v1) => $"Attackers ≥ {v}",

            ["slavelt"]           = (v, v1) => $"Slaves < {v}",
            ["slavele"]           = (v, v1) => $"Slaves ≤ {v}",

            ["closedattacked"]    = (v, v1) => "Melee attacked",
            ["longrangeattacked"] = (v, v1) => "Ranged attacked",

            ["skillused"]         = (v, v1) => $"Skill used on mob: {SkillNameOrId(v)}",
            ["afterskill"]        = (v, v1) => $"After casting: {SkillNameOrId(v)}",

            ["casttargeted"]      = (v, v1) => "Target in cast range",
            ["rudeattacked"]      = (v, v1) => "Rude attacked",

            ["mobnearbygt"]       = (v, v1) => $"Nearby mobs > {v}",
            ["groundattacked"]    = (v, v1) => "Hit by ground skill",
            ["damagedgt"]         = (v, v1) => $"Damage > {v}",

            // Seen in some docs; keep as pass-through friendly strings:
            ["alchemist"]         = (v, v1) => "Alchemist AI",
            ["trickcasting"]      = (v, v1) => "Trick casting",
            ["hiding"]            = (v, v1) => "Target hidden (server-dependent)"
        };

    // Hook points (optional): wire these to your existing services if you want real names.
    // Keep safe defaults for Phase A: just show numeric IDs.
    public static Func<int, string>? SkillNameResolver { get; set; }
    public static Func<int, string>? StatusNameResolver { get; set; }

    public static string Format(string? condType, int condVal, int? val1 = null)
    {
        var type = (condType ?? "").Trim();
        if (type.Length == 0) return "Unconditional";

        if (_fmt.TryGetValue(type, out var f))
            return f(condVal, val1);

        // Fallback: keep transparent raw form
        return condVal != 0 ? $"{type} ({condVal})" : type;
    }

    private static string SkillNameOrId(int id)
        => SkillNameResolver?.Invoke(id) ?? $"Skill#{id}";

    private static string StatusNameOrId(int id)
        => StatusNameResolver?.Invoke(id) ?? $"SC#{id}";
}
