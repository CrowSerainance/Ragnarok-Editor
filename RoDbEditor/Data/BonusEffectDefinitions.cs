namespace RoDbEditor.Data;

/// <summary>
/// Defines an rAthena bonus script effect for the script builder UI.
/// </summary>
public sealed class BonusEffectDefinition
{
    public string BonusConstant { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Category { get; init; } = "";
    public bool TakesValue { get; init; } = true;
    public string ValueHint { get; init; } = "amount";

    public override string ToString() => $"{DisplayName} ({BonusConstant})";
}

/// <summary>
/// Static registry of common rAthena bonus constants with parse/build helpers.
/// </summary>
public static class BonusEffectRegistry
{
    public static IReadOnlyList<BonusEffectDefinition> All { get; } = new List<BonusEffectDefinition>
    {
        // Stats
        new() { BonusConstant = "bStr",      DisplayName = "STR +",           Category = "Stats",   ValueHint = "amount" },
        new() { BonusConstant = "bAgi",      DisplayName = "AGI +",           Category = "Stats",   ValueHint = "amount" },
        new() { BonusConstant = "bVit",      DisplayName = "VIT +",           Category = "Stats",   ValueHint = "amount" },
        new() { BonusConstant = "bInt",      DisplayName = "INT +",           Category = "Stats",   ValueHint = "amount" },
        new() { BonusConstant = "bDex",      DisplayName = "DEX +",           Category = "Stats",   ValueHint = "amount" },
        new() { BonusConstant = "bLuk",      DisplayName = "LUK +",           Category = "Stats",   ValueHint = "amount" },
        new() { BonusConstant = "bAllStats", DisplayName = "All Stats +",     Category = "Stats",   ValueHint = "amount" },

        // HP / SP
        new() { BonusConstant = "bMaxHP",       DisplayName = "Max HP +",        Category = "HP/SP",  ValueHint = "amount" },
        new() { BonusConstant = "bMaxSP",       DisplayName = "Max SP +",        Category = "HP/SP",  ValueHint = "amount" },
        new() { BonusConstant = "bMaxHPrate",   DisplayName = "Max HP +%",       Category = "HP/SP",  ValueHint = "percent" },
        new() { BonusConstant = "bMaxSPrate",   DisplayName = "Max SP +%",       Category = "HP/SP",  ValueHint = "percent" },
        new() { BonusConstant = "bHPrecovRate", DisplayName = "HP Recovery +%",  Category = "HP/SP",  ValueHint = "percent" },
        new() { BonusConstant = "bSPrecovRate", DisplayName = "SP Recovery +%",  Category = "HP/SP",  ValueHint = "percent" },
        new() { BonusConstant = "bHealPower",   DisplayName = "Heal Power +%",   Category = "HP/SP",  ValueHint = "percent" },

        // Offense
        new() { BonusConstant = "bBaseAtk",     DisplayName = "Base ATK +",          Category = "Offense", ValueHint = "amount" },
        new() { BonusConstant = "bAtk",         DisplayName = "ATK +",               Category = "Offense", ValueHint = "amount" },
        new() { BonusConstant = "bAtk2",        DisplayName = "ATK2 +",              Category = "Offense", ValueHint = "amount" },
        new() { BonusConstant = "bAtkRate",     DisplayName = "ATK +%",              Category = "Offense", ValueHint = "percent" },
        new() { BonusConstant = "bMatk",        DisplayName = "MATK +",              Category = "Offense", ValueHint = "amount" },
        new() { BonusConstant = "bMatkRate",    DisplayName = "MATK +%",             Category = "Offense", ValueHint = "percent" },
        new() { BonusConstant = "bLongAtkRate", DisplayName = "Long Range ATK +%",   Category = "Offense", ValueHint = "percent" },
        new() { BonusConstant = "bCritAtkRate", DisplayName = "Crit DMG +%",         Category = "Offense", ValueHint = "percent" },

        // Defense
        new() { BonusConstant = "bDef",      DisplayName = "DEF +",     Category = "Defense", ValueHint = "amount" },
        new() { BonusConstant = "bDef2",     DisplayName = "DEF2 +",    Category = "Defense", ValueHint = "amount" },
        new() { BonusConstant = "bDefRate",  DisplayName = "DEF +%",    Category = "Defense", ValueHint = "percent" },
        new() { BonusConstant = "bMdef",     DisplayName = "MDEF +",    Category = "Defense", ValueHint = "amount" },
        new() { BonusConstant = "bMdef2",    DisplayName = "MDEF2 +",   Category = "Defense", ValueHint = "amount" },
        new() { BonusConstant = "bMdefRate", DisplayName = "MDEF +%",   Category = "Defense", ValueHint = "percent" },

        // Combat
        new() { BonusConstant = "bHit",          DisplayName = "HIT +",           Category = "Combat", ValueHint = "amount" },
        new() { BonusConstant = "bHitRate",      DisplayName = "HIT +%",          Category = "Combat", ValueHint = "percent" },
        new() { BonusConstant = "bFlee",         DisplayName = "FLEE +",          Category = "Combat", ValueHint = "amount" },
        new() { BonusConstant = "bFleeRate",     DisplayName = "FLEE +%",         Category = "Combat", ValueHint = "percent" },
        new() { BonusConstant = "bFlee2",        DisplayName = "Perfect Dodge +", Category = "Combat", ValueHint = "amount" },
        new() { BonusConstant = "bCritical",     DisplayName = "CRIT +",          Category = "Combat", ValueHint = "amount" },
        new() { BonusConstant = "bCriticalRate", DisplayName = "CRIT +%",         Category = "Combat", ValueHint = "percent" },
        new() { BonusConstant = "bAspd",         DisplayName = "ASPD +",          Category = "Combat", ValueHint = "amount" },
        new() { BonusConstant = "bAspdRate",     DisplayName = "ASPD +%",         Category = "Combat", ValueHint = "percent" },

        // Misc
        new() { BonusConstant = "bSpeedRate",          DisplayName = "Move Speed +%",     Category = "Misc", ValueHint = "percent" },
        new() { BonusConstant = "bSpeedAddRate",       DisplayName = "Move Speed Add +%", Category = "Misc", ValueHint = "percent" },
        new() { BonusConstant = "bIntravision",        DisplayName = "See Hidden",        Category = "Misc", TakesValue = false },
        new() { BonusConstant = "bNoKnockback",        DisplayName = "No Knockback",      Category = "Misc", TakesValue = false },
        new() { BonusConstant = "bNoGemStone",         DisplayName = "No Gemstone",       Category = "Misc", TakesValue = false },
        new() { BonusConstant = "bUnbreakableWeapon",  DisplayName = "Unbreakable Weapon", Category = "Misc", TakesValue = false },
        new() { BonusConstant = "bUnbreakableArmor",   DisplayName = "Unbreakable Armor",  Category = "Misc", TakesValue = false },
        new() { BonusConstant = "bNoMagicDamage",      DisplayName = "Magic DMG Reduce %", Category = "Resistance", ValueHint = "percent" },
        new() { BonusConstant = "bNoMiscDamage",       DisplayName = "Misc DMG Reduce %",  Category = "Resistance", ValueHint = "percent" },
    };

    /// <summary>
    /// Parse a script string like "{ bonus bAgi,5; bonus bStr,3; }" into bonus pairs.
    /// </summary>
    public static List<(string bonusType, int value)> ParseScript(string? script)
    {
        var result = new List<(string, int)>();
        if (string.IsNullOrWhiteSpace(script)) return result;

        var text = script.Trim().Trim('{', '}').Trim();
        var parts = text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (!trimmed.StartsWith("bonus ", StringComparison.OrdinalIgnoreCase))
                continue;

            var remainder = trimmed.Substring(6).Trim();
            var commaIdx = remainder.IndexOf(',');
            if (commaIdx > 0)
            {
                var bonusType = remainder.Substring(0, commaIdx).Trim();
                int.TryParse(remainder.Substring(commaIdx + 1).Trim(), out var val);
                result.Add((bonusType, val));
            }
            else
            {
                result.Add((remainder.Trim(), 0));
            }
        }
        return result;
    }

    /// <summary>
    /// Build a script string from bonus rows. Returns e.g. "{ bonus bStr,5; bonus bAgi,3; }"
    /// </summary>
    public static string BuildScript(IEnumerable<(string bonusType, int value)> bonuses)
    {
        var parts = new List<string>();
        foreach (var (bt, val) in bonuses)
        {
            if (string.IsNullOrWhiteSpace(bt)) continue;
            var def = All.FirstOrDefault(d => d.BonusConstant == bt);
            if (def != null && !def.TakesValue)
                parts.Add($"bonus {bt};");
            else
                parts.Add($"bonus {bt},{val};");
        }
        if (parts.Count == 0) return "";
        return "{ " + string.Join(" ", parts) + " }";
    }
}
