using System.Collections.Generic;

namespace RoDbEditor.Services.Analysis;

public class WorkspaceIndex
{
    public Dictionary<(EntityKind, int), SymbolKey> ById { get; } = new();
    public Dictionary<(EntityKind, string), List<SymbolKey>> ByName { get; } = new();
    public List<ReferenceRecord> References { get; } = new();
    public List<Models.DbOverrideRecord> Overrides { get; } = new();
}
