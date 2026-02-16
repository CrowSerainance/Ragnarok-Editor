using System.Collections.Generic;

namespace RoDbEditor.Models;

public sealed class ClientItemInfoEntry
{
    public int Id { get; set; }

    public string? UnidentifiedDisplayName { get; set; }
    public string? UnidentifiedResourceName { get; set; }
    public List<string> UnidentifiedDescriptionName { get; set; } = new();

    public string? IdentifiedDisplayName { get; set; }
    public string? IdentifiedResourceName { get; set; }
    public List<string> IdentifiedDescriptionName { get; set; } = new();

    public int SlotCount { get; set; }
    public int ClassNum { get; set; }

    public int? EffectID { get; set; }
    public bool? Costume { get; set; }
}
