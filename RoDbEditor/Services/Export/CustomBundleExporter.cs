using RoDbEditor.Models;

namespace RoDbEditor.Services.Export;

public sealed class CustomBundleExporter
{
    private readonly ItemBundleExporter _items;
    private readonly MobBundleExporter _mobs;
    private readonly NpcBundleExporter _npcs;

    public CustomBundleExporter(ItemBundleExporter items, MobBundleExporter mobs, NpcBundleExporter npcs)
    {
        _items = items;
        _mobs = mobs;
        _npcs = npcs;
    }

    public ExportBundle BuildForItem(ItemEntry item, bool includeClient, bool includeAssetNotes)
    {
        var bundle = new ExportBundle();
        _items.AddItem(bundle, item, includeClient, includeAssetNotes);
        return bundle;
    }

    public ExportBundle BuildForMob(MobEntry mob, bool includeMobAvail, bool includeMobSkills, bool includeAssetNotes)
    {
        var bundle = new ExportBundle();
        _mobs.AddMob(bundle, mob, includeMobAvail, includeMobSkills, includeAssetNotes);
        return bundle;
    }

    public ExportBundle BuildForNpc(NpcScriptEntry npc, string? editedScriptText, bool includeClientIdentity, bool includeAssetNotes)
    {
        var bundle = new ExportBundle();
        _npcs.AddNpc(bundle, npc, editedScriptText, includeClientIdentity, includeAssetNotes);
        return bundle;
    }
}
