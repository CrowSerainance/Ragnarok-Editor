using RoDbEditor.Models;

namespace RoDbEditor.ViewModels;

public sealed class MobSkillRow
{
    public int SkillId { get; }
    public string SkillName { get; }
    public int SkillLevel { get; }
    public int Rate { get; }
    public string State { get; }

    public string DisplayText => $"{SkillName} [Lv{SkillLevel}]";

    public MobSkillEntry Raw { get; }

    public MobSkillRow(MobSkillEntry raw)
    {
        Raw = raw;
        SkillId = raw.SkillId;
        SkillName = !string.IsNullOrWhiteSpace(raw.SkillName) ? raw.SkillName! : $"Skill#{raw.SkillId}";
        SkillLevel = raw.SkillLevel;
        Rate = raw.Rate;
        State = raw.State ?? "";
    }
}
