using System.Collections.Generic;

namespace RoDbEditor.Models;

/// <summary>
/// Represents one extracted asset group:
/// same base name + same folder, with all sibling file formats paired.
/// </summary>
public class ExtractedAssetEntry
{
    /// <summary>Display name shown in the list.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Base file name without extension.</summary>
    public string BaseName { get; set; } = "";

    /// <summary>Absolute path to paired .act if present.</summary>
    public string? ActPath { get; set; }

    /// <summary>Absolute path to paired .spr if present.</summary>
    public string? SprPath { get; set; }

    /// <summary>Absolute path used for static preview when sprite preview is unavailable.</summary>
    public string? PreviewPath { get; set; }

    /// <summary>Parent folder path (relative to extracted assets root when possible).</summary>
    public string FolderPath { get; set; } = "";

    /// <summary>
    /// Folder path relative to data root (always starts with "data\").
    /// Example: data\sprite\monster
    /// </summary>
    public string DataRelativeFolder { get; set; } = "";

    /// <summary>
    /// Source variant/server name inferred from extracted root layout.
    /// </summary>
    public string VariantName { get; set; } = "";

    /// <summary>All paired file paths sharing the same base name in the same folder.</summary>
    public List<string> SourcePaths { get; set; } = new();

    /// <summary>Extension summary for UI (e.g. ".spr, .act, .pal").</summary>
    public string ExtensionsSummary { get; set; } = "";

    /// <summary>
    /// Suggested destination category from path heuristics.
    /// This is a hint only; user chooses final save category.
    /// </summary>
    public string SuggestedCategory { get; set; } = "CUSTOM";
}
