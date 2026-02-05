using System.Collections.Generic;

namespace RoDbEditor.Models;

/// <summary>
/// One item from item_db YAML (rAthena style). Used for list + edit.
/// </summary>
public class ItemEntry
{
    public int Id { get; set; }
    public string AegisName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Etc";
    public string? SubType { get; set; }
    public int? Buy { get; set; }
    public int? Sell { get; set; }
    public int? Weight { get; set; }
    public int? View { get; set; }
    public string? Script { get; set; }
    public string? EquipScript { get; set; }
    public string? UnEquipScript { get; set; }
    public string? SourceFile { get; set; }
    public int SourceIndex { get; set; }

    public string DisplayName => string.IsNullOrEmpty(Name) ? AegisName : Name;
}
