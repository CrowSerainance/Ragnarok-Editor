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
}

