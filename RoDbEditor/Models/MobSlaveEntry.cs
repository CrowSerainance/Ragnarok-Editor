namespace RoDbEditor.Models;

/// <summary>
/// RMS-style "Slaves" view, derived from mob_skill_db summon/call lines.
/// </summary>
public sealed class MobSlaveEntry
{
    public int SlaveMobId { get; set; }
    public string SlaveMobName { get; set; } = "";

    public int Amount { get; set; } = 1;
    public int Rate { get; set; }                   // 1..10000

    public string ConditionType { get; set; } = "";
    public string ConditionValue { get; set; } = "";

    public string SourceInfo { get; set; } = "";
}
