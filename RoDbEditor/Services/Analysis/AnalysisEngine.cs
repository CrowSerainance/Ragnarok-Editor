using System.Collections.Generic;

namespace RoDbEditor.Services.Analysis;

public class AnalysisEngine
{
    private readonly WorkspaceIndexBuilder _builder;
    private readonly Services.NpcIndexService _npcIndex;
    private readonly List<IValidator> _validators;

    public AnalysisEngine(
        WorkspaceIndexBuilder builder, 
        Core.GrfService grf, 
        Services.SpriteLookupService spriteLookup, 
        Services.FileSystemSpriteSource? fileSystem,
        Services.NpcIndexService npcIndex,
        Services.MapIndexService mapIndex)
    {
        _builder = builder;
        _npcIndex = npcIndex;
        _validators = new List<IValidator>
        {
            new DuplicateIdValidator(),
            new BrokenReferenceValidator(),
            new ItemIconValidator(grf, fileSystem),
            new MobSpriteValidator(spriteLookup),
            new NpcMapValidator(mapIndex, npcIndex)
        };
    }

    public (List<DiagnosticRecord> Diagnostics, WorkspaceIndex Index) Analyze()
    {
        var index = _builder.Build();

        // NPC script scan
        var extractor = new NpcScriptReferenceExtractor(index);
        foreach (var npc in _npcIndex.All)
            extractor.Scan(npc);

        var results = new List<DiagnosticRecord>();
        foreach (var v in _validators)
            results.AddRange(v.Validate(index));

        return (results, index);
    }
}
