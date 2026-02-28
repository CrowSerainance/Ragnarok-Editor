using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RoDbEditor.Services;

/// <summary>
/// Creates minimal NPC script files in npc/custom/ for new NPCs from Extracted Assets.
/// Supports dialogue, warp, and shop templates.
/// </summary>
public class NpcScriptWriter
{
    private readonly string _dataPath;

    public NpcScriptWriter(string dataPath)
    {
        _dataPath = dataPath ?? "";
    }

    public string GetNpcCustomDir() => Path.Combine(_dataPath, "npc", "custom");

    /// <summary>Get script content for a minimal NPC (no file I/O). Used by output preview tab.</summary>
    public static string GetEntryScriptContent(string map, int x, int y, int direction, string name, string spriteId)
    {
        var spritePart = int.TryParse(spriteId, out var num) ? num.ToString() : spriteId;
        return $@"// RoDbEditor - Custom NPC
{map},{x},{y},{direction}	script	{name}	{spritePart},{{
	mes ""Hello."";
	close;
}}
";
    }

    /// <summary>Create a minimal NPC script. Returns the file path, or null on failure.</summary>
    public string? WriteEntry(string map, int x, int y, int direction, string name, string spriteId)
    {
        if (string.IsNullOrWhiteSpace(_dataPath) || !Directory.Exists(_dataPath))
            return null;

        var dir = GetNpcCustomDir();
        try { Directory.CreateDirectory(dir); } catch { return null; }

        var safeName = MakeSafeFileName(name);
        if (string.IsNullOrEmpty(safeName)) safeName = "custom_npc";
        var path = Path.Combine(dir, safeName + ".txt");

        var content = GetEntryScriptContent(map, x, y, direction, name, spriteId);
        try
        {
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NpcScriptWriter] WriteEntry failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Generate dialogue NPC script (mes/next/close).</summary>
    public static string GetDialogueScriptContent(string map, int x, int y, int direction, string name, string spriteId, IReadOnlyList<string> dialogueLines)
    {
        var spritePart = int.TryParse(spriteId, out var num) ? num.ToString() : spriteId;
        var sb = new StringBuilder();
        sb.AppendLine($"// RoDbEditor - Dialogue NPC");
        sb.AppendLine($"{map},{x},{y},{direction}\tscript\t{name}\t{spritePart},{{");
        for (var i = 0; i < dialogueLines?.Count; i++)
        {
            var line = dialogueLines[i]?.Replace("\"", "\\\"") ?? "";
            sb.AppendLine($"\tmes \"{line}\";");
            if (i < dialogueLines.Count - 1)
                sb.AppendLine("\tnext;");
        }
        sb.AppendLine("\tclose;");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>Generate warp NPC script.</summary>
    public static string GetWarpScriptContent(string map, int x, int y, int direction, string name, string spriteId, string targetMap, int targetX, int targetY)
    {
        var spritePart = int.TryParse(spriteId, out var num) ? num.ToString() : spriteId;
        var sb = new StringBuilder();
        sb.AppendLine("// RoDbEditor - Warp NPC");
        sb.AppendLine($"{map},{x},{y},{direction}\tscript\t{name}\t{spritePart},{{");
        sb.AppendLine("\tmes \"Do you want to warp?\";");
        sb.AppendLine("\tnext;");
        sb.AppendLine("\tif(select(\"Yes\",\"No\") == 2) close;");
        sb.AppendLine($"\twarp \"{targetMap}\",{targetX},{targetY};");
        sb.AppendLine("\tclose;");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>Shop entry: ItemId and Price (-1 = use DB price).</summary>
    public readonly struct ShopEntry
    {
        public int ItemId { get; }
        public int Price { get; }
        public ShopEntry(int itemId, int price = -1) { ItemId = itemId; Price = price; }
    }

    /// <summary>Generate shop NPC script (rAthena one-line shop: map,x,y,dir shop Name spriteId,itemId:price,...). Prices &lt; 0 = use DB.</summary>
    public static string GetShopScriptContent(string map, int x, int y, int direction, string name, string spriteId, IReadOnlyList<ShopEntry> shopEntries, Func<int, int>? getPriceFromDb = null)
    {
        var spritePart = int.TryParse(spriteId, out var num) ? num.ToString() : spriteId;
        var sb = new StringBuilder();
        sb.AppendLine("// RoDbEditor - Shop NPC");
        if (shopEntries != null && shopEntries.Count > 0)
        {
            var parts = shopEntries.Select(e =>
            {
                var price = e.Price >= 0 ? e.Price : (getPriceFromDb?.Invoke(e.ItemId) ?? -1);
                return $"{e.ItemId}:{price}";
            }).ToList();
            sb.AppendLine($"{map},{x},{y},{direction}\tshop\t{name}\t{spritePart},{string.Join(",", parts)}");
        }
        else
            sb.AppendLine($"{map},{x},{y},{direction}\tshop\t{name}\t{spritePart},// Add itemId:price,itemId:price,...");
        return sb.ToString();
    }

    /// <summary>Write dialogue NPC to npc/custom/. Returns file path or null.</summary>
    public string? WriteDialogueNpc(string map, int x, int y, int direction, string name, string spriteId, IReadOnlyList<string> dialogueLines)
    {
        var content = GetDialogueScriptContent(map, x, y, direction, name, spriteId, dialogueLines);
        return WriteScriptFile(MakeSafeFileName(name) + ".txt", content);
    }

    /// <summary>Write warp NPC to npc/custom/. Returns file path or null.</summary>
    public string? WriteWarpNpc(string map, int x, int y, int direction, string name, string spriteId, string targetMap, int targetX, int targetY)
    {
        var content = GetWarpScriptContent(map, x, y, direction, name, spriteId, targetMap, targetX, targetY);
        return WriteScriptFile(MakeSafeFileName(name) + ".txt", content);
    }

    /// <summary>Write shop NPC to npc/custom/. Returns file path or null.</summary>
    public string? WriteShopNpc(string map, int x, int y, int direction, string name, string spriteId, IReadOnlyList<ShopEntry> shopEntries)
    {
        int? GetPrice(int itemId)
        {
            try
            {
                var item = RoDbEditor.App.ItemDbService?.GetById(itemId);
                return item?.Buy ?? 0;
            }
            catch { return 0; }
        }
        var content = GetShopScriptContent(map, x, y, direction, name, spriteId, shopEntries, id => GetPrice(id) ?? 0);
        return WriteScriptFile(MakeSafeFileName(name) + ".txt", content);
    }

    private string? WriteScriptFile(string fileName, string content)
    {
        if (string.IsNullOrWhiteSpace(_dataPath) || !Directory.Exists(_dataPath)) return null;
        var dir = GetNpcCustomDir();
        try { Directory.CreateDirectory(dir); } catch { return null; }
        var path = Path.Combine(dir, fileName);
        try
        {
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }
        catch { return null; }
    }

    /// <summary>Format GM test command for warp (e.g. @warp prontera 150 150).</summary>
    public static string FormatWarpTestCommand(string map, int x, int y) => $"@warp {map} {x} {y}";

    private static string MakeSafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (invalid.Contains(c) || c == ' ')
                sb.Append('_');
            else
                sb.Append(c);
        }
        return sb.ToString().Trim('_');
    }
}
