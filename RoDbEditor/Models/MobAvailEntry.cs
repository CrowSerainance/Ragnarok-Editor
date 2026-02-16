namespace RoDbEditor.Models;

public sealed class MobAvailEntry
{
    public string Mob { get; set; } = "";
    public string Sprite { get; set; } = "";

    public string? Sex { get; set; }
    public int? HairStyle { get; set; }
    public int? HairColor { get; set; }
    public int? ClothColor { get; set; }
    public string? Weapon { get; set; }
    public string? Shield { get; set; }
}
