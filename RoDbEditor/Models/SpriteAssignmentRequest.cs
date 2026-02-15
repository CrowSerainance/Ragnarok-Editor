using System.Collections.Generic;

namespace RoDbEditor.Models;

public enum SpriteAssignmentEntityType
{
    Npc,
    Monster,
    Item
}

public class SpriteAssignmentRequest
{
    public SpriteAssignmentEntityType EntityType { get; set; }
    public string TargetKey { get; set; } = "";
    public string SourceActPath { get; set; } = "";
    public string SourceSprPath { get; set; } = "";
    public List<string> RelatedPaths { get; set; } = new();
    public string ClientRootPath { get; set; } = "";
    /// <summary>Full path to the target GRF file (e.g. custom.grf). When null, derived from ClientRootPath.</summary>
    public string? TargetGrfPath { get; set; }
    /// <summary>When true (headgear/accessory), copy sprites to 아이템 + 악세사리/남 + 악세사리/여.</summary>
    public bool IsHeadgear { get; set; }
}

