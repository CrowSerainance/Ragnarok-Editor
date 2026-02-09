using System.Collections.Generic;

namespace RoDbEditor.Services.Analysis;

public class MobSpriteValidator : IValidator
{
    private readonly SpriteLookupService _lookup;

    public MobSpriteValidator(SpriteLookupService lookup)
    {
        _lookup = lookup;
    }

    public IEnumerable<DiagnosticRecord> Validate(WorkspaceIndex index)
    {
        foreach (var kv in index.ById)
        {
            if (kv.Key.Item1 != EntityKind.Mob) continue;
            var id = kv.Key.Item2;
            var key = kv.Value;
            var aegisName = key.Name; 

            if (string.IsNullOrEmpty(aegisName)) continue;

            var (act, spr) = _lookup.FindMonsterSprite(aegisName);
            if (act == null || spr == null)
            {
                yield return new DiagnosticRecord
                {
                    Severity = DiagnosticSeverity.Warning,
                    Code = "ROV004M",
                    Message = $"Missing sprite for mob {id} ({aegisName}).",
                    FilePath = "",
                    LineNumber = 0
                };
            }
        }
    }
}
