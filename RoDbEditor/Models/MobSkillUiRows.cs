namespace RoDbEditor.Models;

public sealed class MonsterSkillRow
{
    public string Skill { get; set; } = "";
    public int SkillId { get; set; }
    public int SkillLv { get; set; }

    public string State { get; set; } = "";
    public string Target { get; set; } = "";

    public string Rate { get; set; } = "";     // e.g. "10.00%"
    public string Cast { get; set; } = "";     // e.g. "0.7s"
    public string Delay { get; set; } = "";    // e.g. "30.0s"

    public string Condition { get; set; } = ""; // human readable
    public string Source { get; set; } = "";    // "mob_skill_db.txt:123"

    /// <summary>Back-reference to the raw mob_skill_db row for editing.</summary>
    public MobSkillDbRow? SourceRow { get; set; }
}

public sealed class MonsterSlaveRow
{
    public int MobId { get; set; }
    public string MobName { get; set; } = "";
    public int Count { get; set; }

    public string When { get; set; } = "";      // condition summary
    public string Source { get; set; } = "";

    /// <summary>Back-reference to the raw mob_skill_db row for editing.</summary>
    public MobSkillDbRow? SourceRow { get; set; }
}
