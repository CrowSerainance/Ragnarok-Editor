using System.Collections.Generic;
using System.Linq;
using RoDbEditor.Core;
using RoDbEditor.Models;
using RoDbEditor.Services;

namespace RoDbEditor.Services.Analysis;

public class NpcMapValidator : IValidator
{
    private readonly MapIndexService _mapIndex;
    private readonly NpcIndexService _npcIndex;

    public NpcMapValidator(MapIndexService mapIndex, NpcIndexService npcIndex)
    {
        _mapIndex = mapIndex;
        _npcIndex = npcIndex;
    }

    public IEnumerable<DiagnosticRecord> Validate(WorkspaceIndex index)
    {
        var checkedMaps = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var npc in _npcIndex.All)
        {
            if (string.IsNullOrEmpty(npc.Map) || npc.Map == "-" || checkedMaps.Contains(npc.Map))
                continue;

            checkedMaps.Add(npc.Map);

            // Check if map exists in map_index.txt (ground truth)
            if (!_mapIndex.Exists(npc.Map))
            {
                yield return new DiagnosticRecord
                {
                    Severity = DiagnosticSeverity.Warning,
                    Code = "ROV005",
                    Message = $"NPC '{npc.Name}' is on missing map '{npc.Map}' (not in map_index.txt).",
                    FilePath = npc.FilePath,
                    LineNumber = npc.LineIndex + 1, 
                    Snippet = npc.RawLine
                };
            }
        }
    }
}
