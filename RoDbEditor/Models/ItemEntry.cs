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
    
    // Price
    public int? Buy { get; set; }
    public int? Sell { get; set; }
    public int? Weight { get; set; }
    
    // Combat stats
    public int? Attack { get; set; }
    public int? MagicAttack { get; set; }
    public int? Defense { get; set; }
    public int? Range { get; set; }
    public int? Slots { get; set; }
    
    // Levels
    public int? WeaponLevel { get; set; }
    public int? ArmorLevel { get; set; }
    public int? EquipLevelMin { get; set; }
    public int? EquipLevelMax { get; set; }
    
    // Flags
    public bool Refineable { get; set; }
    public bool Gradable { get; set; }
    
    // Restrictions
    public Dictionary<string, bool> Jobs { get; set; } = new();
    public Dictionary<string, bool> Classes { get; set; } = new();
    public string Gender { get; set; } = "Both";
    public Dictionary<string, bool> Locations { get; set; } = new();
    
    // Visual
    public int? View { get; set; }
    public string? AliasName { get; set; }
    
    // Scripts
    public string? Script { get; set; }
    public string? EquipScript { get; set; }
    public string? UnEquipScript { get; set; }
    
    // Metadata
    public string? SourceFile { get; set; }
    public int SourceIndex { get; set; }

    public string DisplayName => string.IsNullOrEmpty(Name) ? AegisName : Name;
}
