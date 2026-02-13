using System.Collections.Generic;

namespace RoDbEditor.Models;

public class SpriteAssignmentResult
{
    public bool Success { get; set; }
    public string ManifestPath { get; set; } = "";
    public List<string> CopiedFiles { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

