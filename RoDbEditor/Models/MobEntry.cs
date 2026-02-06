using System.Collections.Generic;

namespace RoDbEditor.Models;

public class MobEntry
{
    public int Id { get; set; }
    public string AegisName { get; set; } = "";
    public string Name { get; set; } = "";
    
    // Stats
    public int Level { get; set; }
    public int Hp { get; set; }
    public int Sp { get; set; }
    public int BaseExp { get; set; }
    public int JobExp { get; set; }
    public int MvpExp { get; set; }
    
    // Attack
    public int Attack { get; set; }
    public int Attack2 { get; set; }
    public int Defense { get; set; }
    public int MagicDefense { get; set; }
    
    // Attributes
    public int Str { get; set; }
    public int Agi { get; set; }
    public int Vit { get; set; }
    public int Int { get; set; }
    public int Dex { get; set; }
    public int Luk { get; set; }
    
    // Range
    public int AttackRange { get; set; }
    public int SkillRange { get; set; }
    public int ChaseRange { get; set; }
    
    // Type
    public string Size { get; set; } = "Medium";
    public string Race { get; set; } = "Formless";
    public string Element { get; set; } = "Neutral";
    public int ElementLevel { get; set; } = 1;
    
    // Speed
    public int WalkSpeed { get; set; } = 200;
    public int AttackDelay { get; set; }
    public int AttackMotion { get; set; }
    public int DamageMotion { get; set; }
    
    // AI
    public string Ai { get; set; } = "06";
    public string Class { get; set; } = "Normal";
    public Dictionary<string, bool> Modes { get; set; } = new();
    
    // Drops
    public List<MobDropEntry> Drops { get; set; } = new();
    public List<MobDropEntry> MvpDrops { get; set; } = new();
    
    // Metadata
    public string? SourceFile { get; set; }
    public int SourceIndex { get; set; }

    public string DisplayName => string.IsNullOrEmpty(Name) ? AegisName : Name;
    
    /// <summary>
    /// Calculates hit rate for a given player DEX.
    /// </summary>
    public int CalculateHit100(int playerDex)
    {
        // Formula: Hit = Level + DEX + 175
        return Level + Dex + 175;
    }
    
    /// <summary>
    /// Calculates flee rate.
    /// </summary>
    public int CalculateFlee95()
    {
        // Formula: Flee = Level + AGI + 100
        return Level + Agi + 100;
    }
}
