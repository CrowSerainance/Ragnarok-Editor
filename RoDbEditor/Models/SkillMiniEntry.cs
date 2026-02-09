namespace RoDbEditor.Models;

public sealed class SkillMiniEntry
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int MaxLevel { get; set; }
}
