using System.Collections.Generic;

namespace RoDbEditor.Models;

public enum NpcScriptType { Script, Shop, Warp }

public class NpcScriptEntry
{
    public string Map { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Direction { get; set; }
    public NpcScriptType Type { get; set; }
    public string Name { get; set; } = "";
    public string SpriteId { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int LineIndex { get; set; }
    /// <summary>Full definition line (for shop/warp one-liner) or header line for script.</summary>
    public string RawLine { get; set; } = "";
    /// <summary>Script body between { } for script-type NPCs.</summary>
    public string ScriptBody { get; set; } = "";
    public List<ShopItemEntry> ShopItems { get; set; } = new();
    public WarpTarget? WarpTarget { get; set; }

    public string DisplayName => string.IsNullOrEmpty(Name) ? $"{Map} ({X},{Y})" : Name;
}

public class ShopItemEntry
{
    public int ItemId { get; set; }
    public int Price { get; set; } // -1 = from item_db
}

public class WarpTarget
{
    public string Map { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
}
