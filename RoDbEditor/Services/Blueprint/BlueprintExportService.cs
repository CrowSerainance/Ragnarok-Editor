using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RoDbEditor.Models;

namespace RoDbEditor.Services.Blueprint;

/// <summary>
/// Produces copy-paste friendly "custom content bundles" for Items, Mobs, NPCs:
/// - server import snippets
/// - client lua snippets (templates if full client DB not yet parsed)
/// - asset install manifest (best-effort)
/// - reload commands
/// </summary>
public class BlueprintExportService
{
    public string BuildItemBlueprint(ItemEntry item, string? scriptOverride = null)
    {
        var script = scriptOverride ?? item.Script ?? "";

        var sb = new StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine($"ITEM BLUEPRINT — {item.Id} {item.DisplayName}");
        sb.AppendLine("========================================");
        sb.AppendLine();

        var resourceName = item.ResourceName ?? item.AegisName ?? "";

        // SERVER: db/import/item_db.yml
        sb.AppendLine("[SERVER] db/import/item_db.yml");
        sb.AppendLine("  - Paste the entry under Body: (or create the file with Header/Body)");
        sb.AppendLine();
        sb.AppendLine(RenderItemYamlEntry(item, script));
        sb.AppendLine();

        // CLIENT: itemInfo.lua / itemInfo.lub
        sb.AppendLine("[CLIENT] itemInfo.lua / itemInfo.lub");
        sb.AppendLine("  - Add/override the entry in the itemInfo table:");
        sb.AppendLine();
        sb.AppendLine(RenderItemInfoLuaEntry(item, resourceName));
        sb.AppendLine();

        // CLIENT (OPTIONAL): accessoryid.lua + accname.lua
        if (item.View.HasValue && item.View.Value > 0)
        {
            sb.AppendLine("[CLIENT][OPTIONAL] accessoryid.lua + accname.lua (if this is a wearable view sprite)");
            sb.AppendLine("  - Only needed if you introduce a NEW view ID or NEW accessory name mapping.");
            sb.AppendLine("  - If you reuse an existing view ID, you may NOT need new lines.");
            sb.AppendLine();
            sb.AppendLine("-- accessoryid.lua");
            sb.AppendLine($"ACCESSORY_IDs[\"{item.AegisName}\"] = {item.View.Value}");
            sb.AppendLine();
            sb.AppendLine("-- accname.lua");
            sb.AppendLine($"ACCESSORY_NAME[{item.View.Value}] = \"{item.AegisName}\"");
            sb.AppendLine();
        }

        // ASSETS checklist (generic; your assignment system can later emit exact file list)
        sb.AppendLine("[ASSETS] checklist");
        sb.AppendLine($"  - icon bmp:    data/texture/유저인터페이스/item/{resourceName}.bmp");
        sb.AppendLine($"  - collection:  data/texture/유저인터페이스/collection/{resourceName}.bmp");
        sb.AppendLine($"  - item sprite: data/sprite/아이템/{resourceName}.spr + .act (if applicable)");
        if (item.View.HasValue && item.View.Value > 0)
            sb.AppendLine($"  - equip sprite: data/sprite/악세사리/(male|female)/{resourceName}.spr + .act (if applicable)");
        sb.AppendLine("  - pack into patch.grf or data/ folder overlay.");
        sb.AppendLine();

        sb.AppendLine("[RELOAD]");
        sb.AppendLine("  @reloaditemdb");
        sb.AppendLine("  (restart client to see client-side lua/icon changes)");
        sb.AppendLine();

        return sb.ToString();
    }

    public string BuildMobBlueprint(MobEntry mob, IEnumerable<MobDropEntry>? drops = null, IEnumerable<MobDropEntry>? mvpDrops = null)
    {
        drops ??= mob.Drops ?? Enumerable.Empty<MobDropEntry>();
        mvpDrops ??= mob.MvpDrops ?? Enumerable.Empty<MobDropEntry>();

        var sb = new StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine($"MOB BLUEPRINT — {mob.Id} {mob.DisplayName}");
        sb.AppendLine("========================================");
        sb.AppendLine();

        sb.AppendLine("[SERVER] db/import/mob_db.yml");
        sb.AppendLine("  - Paste the entry under Body: (or create the file with Header/Body)");
        sb.AppendLine();
        sb.AppendLine(RenderMobYamlEntry(mob, drops, mvpDrops));
        sb.AppendLine();

        sb.AppendLine("[SERVER] spawn script (recommended: your own npc/custom/*.txt)");
        sb.AppendLine("  - NOTE: spawn lines often hardcode the display name string.");
        sb.AppendLine("  - If you change mob_db Name but your spawn line still has \"Old Name\", you'll still see Old Name.");
        sb.AppendLine();
        sb.AppendLine("-- Example (adjust map/x/y/qty/delay):");
        sb.AppendLine($"prontera,150,150,0,0 monster \"{mob.DisplayName}\",{mob.Id},1,5000,0");
        sb.AppendLine();

        sb.AppendLine("[ASSETS] checklist (if using a custom sprite)");
        sb.AppendLine($"  - monster sprite: data/sprite/몬스터/<sprite>.spr + .act");
        sb.AppendLine($"  - monster textures: data/texture/… (if any)");
        sb.AppendLine("  - sprite mapping may require mob_avail.yml depending on your setup.");
        sb.AppendLine();

        sb.AppendLine("[RELOAD]");
        sb.AppendLine("  @reloadmobdb");
        sb.AppendLine("  @reloadscript");
        sb.AppendLine();

        return sb.ToString();
    }

    public string BuildNpcBlueprint(NpcScriptEntry npc)
    {
        var sb = new StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine($"NPC BLUEPRINT — {npc.Name}");
        sb.AppendLine("========================================");
        sb.AppendLine();

        var npcFileName = string.IsNullOrWhiteSpace(npc.FilePath)
            ? (string.IsNullOrWhiteSpace(npc.Name) ? "custom_npc" : npc.Name)
            : System.IO.Path.GetFileNameWithoutExtension(npc.FilePath);
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            npcFileName = npcFileName.Replace(c, '_');

        sb.AppendLine("[SERVER] npc/custom/<yourfile>.txt");
        sb.AppendLine("  - Put your script here (recommended).");
        sb.AppendLine();

        sb.AppendLine("[SERVER] conf/import/scripts_custom.conf");
        sb.AppendLine("  - Add a line:");
        sb.AppendLine($"npc: npc/custom/{npcFileName}.txt");
        sb.AppendLine();

        sb.AppendLine("[CLIENT][OPTIONAL] npcidentity.lua + jobname.lua (only if using a custom NPC sprite identity)");
        sb.AppendLine("  - Add new NPC identity ID in the common custom ranges (e.g. 30000+).");
        sb.AppendLine("  - Provide sprite assets for the NPC identity.");
        sb.AppendLine();

        sb.AppendLine("[RELOAD]");
        sb.AppendLine("  @reloadscript");
        sb.AppendLine();

        return sb.ToString();
    }

    // -------------------------
    // YAML builders (minimal, safe copy-paste snippets)
    // -------------------------

    private static string RenderItemYamlEntry(ItemEntry item, string script)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"  - Id: {item.Id}");
        sb.AppendLine($"    AegisName: {item.AegisName}");
        sb.AppendLine($"    Name: \"{EscapeYaml(item.DisplayName)}\"");
        sb.AppendLine($"    Type: {item.Type}");

        if (item.SubType != null) sb.AppendLine($"    SubType: {item.SubType}");
        if (item.Buy.HasValue) sb.AppendLine($"    Buy: {item.Buy.Value}");
        if (item.Sell.HasValue) sb.AppendLine($"    Sell: {item.Sell.Value}");
        if (item.Weight.HasValue) sb.AppendLine($"    Weight: {item.Weight.Value}");
        if (item.Attack.HasValue) sb.AppendLine($"    Attack: {item.Attack.Value}");
        if (item.MagicAttack.HasValue) sb.AppendLine($"    MagicAttack: {item.MagicAttack.Value}");
        if (item.Defense.HasValue) sb.AppendLine($"    Defense: {item.Defense.Value}");
        if (item.Range.HasValue) sb.AppendLine($"    Range: {item.Range.Value}");
        if (item.Slots.HasValue) sb.AppendLine($"    Slots: {item.Slots.Value}");
        if (item.WeaponLevel.HasValue) sb.AppendLine($"    WeaponLevel: {item.WeaponLevel.Value}");
        if (item.ArmorLevel.HasValue) sb.AppendLine($"    ArmorLevel: {item.ArmorLevel.Value}");
        if (item.EquipLevelMin.HasValue) sb.AppendLine($"    EquipLevelMin: {item.EquipLevelMin.Value}");
        if (item.EquipLevelMax.HasValue) sb.AppendLine($"    EquipLevelMax: {item.EquipLevelMax.Value}");

        if (item.Refineable) sb.AppendLine("    Refineable: true");
        if (item.Gradable) sb.AppendLine("    Gradable: true");

        if (item.View.HasValue) sb.AppendLine($"    View: {item.View.Value}");
        if (item.AliasName != null) sb.AppendLine($"    AliasName: {item.AliasName}");

        if (!string.IsNullOrWhiteSpace(script))
        {
            sb.AppendLine("    Script: |");
            foreach (var line in NormalizeNewlines(script).Split('\n'))
                sb.AppendLine("      " + line);
        }

        return sb.ToString();
    }

    private static string RenderMobYamlEntry(MobEntry mob, IEnumerable<MobDropEntry> drops, IEnumerable<MobDropEntry> mvpDrops)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"  - Id: {mob.Id}");
        sb.AppendLine($"    AegisName: {mob.AegisName}");
        sb.AppendLine($"    Name: \"{EscapeYaml(mob.DisplayName)}\"");

        if (mob.Level > 0) sb.AppendLine($"    Level: {mob.Level}");
        if (mob.Hp > 0) sb.AppendLine($"    Hp: {mob.Hp}");
        if (mob.BaseExp > 0) sb.AppendLine($"    BaseExp: {mob.BaseExp}");
        if (mob.JobExp > 0) sb.AppendLine($"    JobExp: {mob.JobExp}");

        var dropsList = drops.ToList();
        if (dropsList.Count > 0)
        {
            sb.AppendLine("    Drops:");
            foreach (var d in dropsList)
                sb.AppendLine($"      - Item: {d.Item}\n        Rate: {d.Rate}");
        }

        var mvpList = mvpDrops.ToList();
        if (mvpList.Count > 0)
        {
            sb.AppendLine("    MvpDrops:");
            foreach (var d in mvpList)
                sb.AppendLine($"      - Item: {d.Item}\n        Rate: {d.Rate}");
        }

        if (mob.Modes != null && mob.Modes.Any(kv => kv.Value))
        {
            sb.AppendLine("    Modes:");
            foreach (var kv in mob.Modes.Where(kv => kv.Value))
                sb.AppendLine($"      {kv.Key}: true");
        }

        return sb.ToString();
    }

    private static string RenderItemInfoLuaEntry(ItemEntry item, string resourceName)
    {
        var slotCount = item.Slots ?? 0;
        var classNum = item.View ?? 0;

        var sb = new StringBuilder();
        sb.AppendLine($"[{item.Id}] = {{");
        sb.AppendLine("  unidentifiedDisplayName = \"????\",");
        sb.AppendLine($"  unidentifiedResourceName = \"{EscapeLua(resourceName)}\",");
        sb.AppendLine("  unidentifiedDescriptionName = {");
        sb.AppendLine("    \"Unidentified item.\",");
        sb.AppendLine("  },");
        sb.AppendLine($"  identifiedDisplayName = \"{EscapeLua(item.DisplayName)}\",");
        sb.AppendLine($"  identifiedResourceName = \"{EscapeLua(resourceName)}\",");
        sb.AppendLine("  identifiedDescriptionName = {");
        sb.AppendLine("    \"TODO: description line 1\",");
        sb.AppendLine("  },");
        sb.AppendLine($"  slotCount = {slotCount},");
        sb.AppendLine($"  ClassNum = {classNum},");
        sb.AppendLine("},");
        return sb.ToString();
    }

    private static string NormalizeNewlines(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n");

    private static string EscapeYaml(string s) => (s ?? "").Replace("\"", "\\\"");
    private static string EscapeLua(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
}
