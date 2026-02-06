namespace RoDbEditor.Models;

public class SpawnEntry
{
    public string Map { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int MobId { get; set; }

    public string DisplayText => $"{Map} ({X}, {Y})";
}
