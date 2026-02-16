using System.Collections.Generic;
using RoDbEditor;

namespace RoDbEditor.Services.Analysis;

/// <summary>
/// For each mob: effective sprite (mob_avail override or AegisName) must exist.
/// </summary>
public class MobAvailSpriteValidator : IValidator
{
    private readonly SpriteLookupService _lookup;

    public MobAvailSpriteValidator(SpriteLookupService lookup)
    {
        _lookup = lookup;
    }

    public IEnumerable<DiagnosticRecord> Validate(WorkspaceIndex index)
    {
        var mobDb = App.MobDbService;
        var mobAvail = App.MobAvailService;
        if (mobDb == null) yield break;

        foreach (var kv in index.ById)
        {
            if (kv.Key.Item1 != EntityKind.Mob) continue;
            var id = kv.Key.Item2;

            var mob = mobDb.GetMobById(id);
            if (mob == null) continue;

            var effectiveSprite = mobAvail?.Get(mob.AegisName)?.Sprite ?? mob.AegisName;
            if (string.IsNullOrEmpty(effectiveSprite)) continue;

            var (act, spr) = _lookup.FindMonsterSprite(effectiveSprite);
            if (act == null && spr == null)
            {
                yield return new DiagnosticRecord
                {
                    Severity = DiagnosticSeverity.Error,
                    Code = "ROV_MOB_SPRITE",
                    Message = $"Mob {id} ({mob.AegisName}): effective sprite '{effectiveSprite}' not found (.spr/.act).",
                    FilePath = "",
                    LineNumber = 0
                };
            }
        }
    }
}
