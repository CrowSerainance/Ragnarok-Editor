using System.Collections.Generic;
using RoDbEditor;

namespace RoDbEditor.Services.Analysis;

/// <summary>
/// For NPCs with numeric SpriteId >= 30000: check npcidentity/jobname overlay and sprite.
/// </summary>
public class NpcIdentityValidator : IValidator
{
    private readonly SpriteLookupService _lookup;

    public NpcIdentityValidator(SpriteLookupService lookup)
    {
        _lookup = lookup;
    }

    public IEnumerable<DiagnosticRecord> Validate(WorkspaceIndex index)
    {
        var npcIndex = App.NpcIndexService;
        var identityService = App.ClientNpcIdentityService;
        if (npcIndex == null) yield break;

        foreach (var npc in npcIndex.All)
        {
            if (string.IsNullOrWhiteSpace(npc.SpriteId)) continue;
            if (!int.TryParse(npc.SpriteId, out var spriteId) || spriteId < 30000) continue;

            var spriteName = npc.SpriteId;
            if (identityService != null && !identityService.TryGet(spriteName, out _))
            {
                yield return new DiagnosticRecord
                {
                    Severity = DiagnosticSeverity.Warning,
                    Code = "ROV_NPC_IDENTITY",
                    Message = $"NPC {npc.Name} uses class id {spriteId} but npcidentity overlay does not define it. Use Tools > Append npcidentity/jobname overlay.",
                    FilePath = npc.FilePath ?? "",
                    LineNumber = npc.LineIndex
                };
            }

            var (act, spr) = _lookup.FindNpcSprite(spriteName);
            if (act == null && spr == null)
            {
                yield return new DiagnosticRecord
                {
                    Severity = DiagnosticSeverity.Error,
                    Code = "ROV_NPC_SPRITE",
                    Message = $"NPC {npc.Name} (sprite '{spriteName}'): sprite files not found.",
                    FilePath = npc.FilePath ?? "",
                    LineNumber = npc.LineIndex
                };
            }
        }
    }
}
