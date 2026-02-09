using System.Collections.Generic;
using System.IO;
using RoDbEditor.Models;

namespace RoDbEditor.Services.Analysis;

public class WorkspaceIndexBuilder
{
    private readonly ItemDbService _itemDb;
    private readonly MobDbService _mobDb;
    public readonly NpcIndexService _npcIndex;

    public WorkspaceIndexBuilder(
        ItemDbService itemDb,
        MobDbService mobDb,
        NpcIndexService npcIndex
    ){
        _itemDb = itemDb;
        _mobDb = mobDb;
        _npcIndex = npcIndex;
    }

    public WorkspaceIndex Build()
    {
        var index = new WorkspaceIndex();

        // Items
        foreach (var item in _itemDb.Items)
        {
            var key = new SymbolKey(EntityKind.Item, item.Id, item.Name);
            index.ById[(EntityKind.Item, item.Id)] = key;
            
            // Index by AegisName (clean)
            if (!string.IsNullOrEmpty(item.AegisName))
            {
                var cleanName = item.AegisName.Trim();
                if (!index.ByName.ContainsKey((EntityKind.Item, cleanName)))
                    index.ByName[(EntityKind.Item, cleanName)] = new List<SymbolKey>();
                index.ByName[(EntityKind.Item, cleanName)].Add(key);
            }
        }

        // Mobs
        foreach (var mob in _mobDb.Mobs)
        {
            var key = new SymbolKey(EntityKind.Mob, mob.Id, mob.Name);
            index.ById[(EntityKind.Mob, mob.Id)] = key;
            
             // Index by AegisName (clean)
            if (!string.IsNullOrEmpty(mob.AegisName))
            {
                var cleanName = mob.AegisName.Trim();
                if (!index.ByName.ContainsKey((EntityKind.Mob, cleanName)))
                    index.ByName[(EntityKind.Mob, cleanName)] = new List<SymbolKey>();
                index.ByName[(EntityKind.Mob, cleanName)].Add(key);
            }
        }

        // NPC definitions
        foreach (var npc in _npcIndex.All)
        {
            var fileName = Path.GetFileName(npc.FilePath);
            if (string.IsNullOrEmpty(fileName)) continue;
            
            var key = new SymbolKey(EntityKind.Npc, null, fileName);
            if (!index.ByName.ContainsKey((EntityKind.Npc, fileName)))
                index.ByName[(EntityKind.Npc, fileName)] = new List<SymbolKey>();
            
            index.ByName[(EntityKind.Npc, fileName)].Add(key);
            index.ByName[(EntityKind.Npc, fileName)].Add(key);
        }

        if (_itemDb.Overrides != null) index.Overrides.AddRange(_itemDb.Overrides);
        if (_mobDb.Overrides != null) index.Overrides.AddRange(_mobDb.Overrides);

        return index;
    }
}
