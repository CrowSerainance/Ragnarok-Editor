using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RoDbEditor.Models;

namespace RoDbEditor.Services.Export;

public sealed class ItemBundleExporter
{
    private readonly string? _clientRoot;

    public ItemBundleExporter(string? clientRoot)
    {
        _clientRoot = clientRoot;
    }

    public void AddItem(ExportBundle bundle, ItemEntry item, bool includeClient, bool includeAssetNotes)
    {
        bundle.Files.Add(new ExportFile(
            "db/import/item_db.yml",
            RenderItemDbYamlBodyEntry(item),
            ExportWriteMode.Append));

        if (includeClient)
        {
            bundle.Files.Add(new ExportFile(
                ResolveClientItemInfoPath(),
                RenderItemInfoLuaEntry(item),
                ExportWriteMode.Append));

            if ((item.View ?? 0) > 0)
            {
                var constName = ToAccessoryConstantName(item.AegisName);
                bundle.Files.Add(new ExportFile(
                    @"data/luafiles514/lua files/datainfo/accessoryid.lua",
                    $"{constName} = {item.View ?? 0}, // RoDbEditor: {Stamp(item)}",
                    ExportWriteMode.Append));
                bundle.Files.Add(new ExportFile(
                    @"data/luafiles514/lua files/datainfo/accname.lua",
                    $"[ACCESSORY_IDs.{constName}] = \"_{EscapeLua(ResourceName(item))}\", // RoDbEditor: {Stamp(item)}",
                    ExportWriteMode.Append));
            }
        }

        bundle.Commands.Add("@reloaditemdb");
        bundle.Notes.Add("Use import overlay only: db/import/item_db.yml.");
        if (includeAssetNotes)
            bundle.Notes.Add("Install icon/sprite assets using ResourceName first, fallback AegisName.");
    }

    private string ResolveClientItemInfoPath()
    {
        if (!string.IsNullOrWhiteSpace(_clientRoot))
        {
            var systemEn = System.IO.Path.Combine(_clientRoot, "SystemEN");
            if (System.IO.Directory.Exists(systemEn))
                return "SystemEN/itemInfo_C.lua";
        }

        return "System/itemInfo_C.lua";
    }

    private static string RenderItemDbYamlBodyEntry(ItemEntry item)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"  - Id: {item.Id}");
        sb.AppendLine($"    AegisName: {item.AegisName}");
        sb.AppendLine($"    Name: {EscapeYaml(item.Name)}");
        if (!string.IsNullOrWhiteSpace(item.Type) && !string.Equals(item.Type, "Etc", StringComparison.OrdinalIgnoreCase))
            sb.AppendLine($"    Type: {item.Type}");
        if (!string.IsNullOrWhiteSpace(item.SubType) &&
            (item.Type == "Weapon" || item.Type == "Ammo" || item.Type == "Card"))
            sb.AppendLine($"    SubType: {item.SubType}");
        if (item.Buy is > 0) sb.AppendLine($"    Buy: {item.Buy.Value}");
        if (item.Sell is > 0) sb.AppendLine($"    Sell: {item.Sell.Value}");
        if (item.Weight is > 0) sb.AppendLine($"    Weight: {item.Weight.Value}");
        if (item.Attack is > 0) sb.AppendLine($"    Attack: {item.Attack.Value}");
        if (item.MagicAttack is > 0) sb.AppendLine($"    MagicAttack: {item.MagicAttack.Value}");
        if (item.Defense is > 0) sb.AppendLine($"    Defense: {item.Defense.Value}");
        if (item.Range is > 0) sb.AppendLine($"    Range: {item.Range.Value}");
        if (item.Slots is > 0) sb.AppendLine($"    Slots: {item.Slots.Value}");
        if (item.Jobs.Count > 0) AppendBoolMap(sb, "Jobs", item.Jobs);
        if (item.Classes.Count > 0) AppendBoolMap(sb, "Classes", item.Classes);
        if (!string.IsNullOrWhiteSpace(item.Gender) && !string.Equals(item.Gender, "Both", StringComparison.OrdinalIgnoreCase))
            sb.AppendLine($"    Gender: {item.Gender}");
        if (item.Locations.Count > 0) AppendBoolMap(sb, "Locations", item.Locations);
        if (item.WeaponLevel is > 0) sb.AppendLine($"    WeaponLevel: {item.WeaponLevel.Value}");
        if (item.ArmorLevel is > 0) sb.AppendLine($"    ArmorLevel: {item.ArmorLevel.Value}");
        if (item.EquipLevelMin is > 0) sb.AppendLine($"    EquipLevelMin: {item.EquipLevelMin.Value}");
        if (item.EquipLevelMax is > 0) sb.AppendLine($"    EquipLevelMax: {item.EquipLevelMax.Value}");
        if (item.Refineable) sb.AppendLine("    Refineable: true");
        if (item.Gradable) sb.AppendLine("    Gradable: true");
        if (item.View is > 0) sb.AppendLine($"    View: {item.View.Value}");
        if (!string.IsNullOrWhiteSpace(item.AliasName)) sb.AppendLine($"    AliasName: {item.AliasName}");
        AppendScript(sb, "Script", item.Script);
        AppendScript(sb, "EquipScript", item.EquipScript);
        AppendScript(sb, "UnEquipScript", item.UnEquipScript);
        sb.AppendLine($"    # RoDbEditor: {Stamp(item)}");
        return sb.ToString();
    }

    private static string RenderItemInfoLuaEntry(ItemEntry item)
    {
        var resource = ResourceName(item);
        var displayName = string.IsNullOrWhiteSpace(item.Name) ? item.AegisName : item.Name;
        var sb = new StringBuilder();
        sb.AppendLine($"-- RoDbEditor: {Stamp(item)}");
        sb.AppendLine($"[{item.Id}] = {{");
        sb.AppendLine("  unidentifiedDisplayName = \"Unknown Item\",");
        sb.AppendLine($"  unidentifiedResourceName = \"{EscapeLua(resource)}\",");
        sb.AppendLine("  unidentifiedDescriptionName = { \"\" },");
        sb.AppendLine($"  identifiedDisplayName = \"{EscapeLua(displayName ?? "Custom Item")}\",");
        sb.AppendLine($"  identifiedResourceName = \"{EscapeLua(resource)}\",");
        sb.AppendLine("  identifiedDescriptionName = { \"Custom item added by RoDbEditor.\" },");
        sb.AppendLine($"  slotCount = {item.Slots ?? 0},");
        sb.AppendLine($"  ClassNum = {item.View ?? 0},");
        sb.AppendLine("  costume = false,");
        sb.AppendLine("  Custom = true");
        sb.AppendLine("},");
        return sb.ToString();
    }

    private static void AppendBoolMap(StringBuilder sb, string name, Dictionary<string, bool> map)
    {
        var enabled = map.Where(kv => kv.Value).ToList();
        if (enabled.Count == 0)
            return;

        sb.AppendLine($"    {name}:");
        foreach (var kv in enabled)
            sb.AppendLine($"      {kv.Key}: true");
    }

    private static void AppendScript(StringBuilder sb, string key, string? script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return;

        var lines = script.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 1)
        {
            sb.AppendLine($"    {key}: {lines[0]}");
            return;
        }

        sb.AppendLine($"    {key}: |");
        foreach (var line in lines)
            sb.AppendLine($"      {line}");
    }

    private static string ResourceName(ItemEntry item)
        => string.IsNullOrWhiteSpace(item.ResourceName)
            ? (string.IsNullOrWhiteSpace(item.AegisName) ? "Custom_Item" : item.AegisName)
            : item.ResourceName;

    private static string ToAccessoryConstantName(string? aegisName)
    {
        if (string.IsNullOrWhiteSpace(aegisName))
            return "ACCESSORY_Custom_Item";

        var safe = new string(aegisName.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (string.IsNullOrWhiteSpace(safe))
            safe = "Custom_Item";
        return "ACCESSORY_" + safe;
    }

    private static string Stamp(ItemEntry item)
        => $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} ITEM {item.Id} {item.AegisName}";

    private static string EscapeYaml(string value)
        => value?.Replace("\"", "''") ?? "";

    private static string EscapeLua(string value)
        => (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
}
