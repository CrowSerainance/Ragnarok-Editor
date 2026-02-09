namespace RoDbEditor.Models;

/// <summary>
/// Parsed row from rAthena mob_skill_db.txt.
/// Schema reference is documented at the top of db/re/mob_skill_db.txt.
/// </summary>
public sealed class MobSkillEntry
{
    public int MobId { get; set; }
    public string Info { get; set; } = "";          // Dummy/info field
    public string State { get; set; } = "";         // idle/walk/attack/etc.

    public int SkillId { get; set; }
    public int SkillLevel { get; set; }

    public int Rate { get; set; }                   // 1..10000
    public int CastTimeMs { get; set; }
    public int DelayMs { get; set; }

    public string Cancelable { get; set; } = "";
    public string Target { get; set; } = "";

    public string ConditionType { get; set; } = "";
    public string ConditionValue { get; set; } = "";

    public int? Val1 { get; set; }
    public int? Val2 { get; set; }
    public int? Val3 { get; set; }
    public int? Val4 { get; set; }
    public int? Val5 { get; set; }

    public int? Emotion { get; set; }
    public string Chat { get; set; } = "";

    // Resolved later via SkillDbService (optional)
    public string? SkillName { get; set; }
}
