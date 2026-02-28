using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RoDbEditor.Models;

namespace RoDbEditor.Services.Client;

public sealed class ClientItemInfoWriter
{
    private const string AddItemFunction =
@"main = function()
  for ItemID,DESC in pairs(tbl) do
    result, msg = AddItem(ItemID, DESC.unidentifiedDisplayName, DESC.unidentifiedResourceName, DESC.identifiedDisplayName, DESC.identifiedResourceName, DESC.slotCount, DESC.ClassNum)
    if not result then return false, msg end
    for k,v in pairs(DESC.unidentifiedDescriptionName) do
      result, msg = AddItemUnidentifiedDesc(ItemID, v)
      if not result then return false, msg end
    end
    for k,v in pairs(DESC.identifiedDescriptionName) do
      result, msg = AddItemIdentifiedDesc(ItemID, v)
      if not result then return false, msg end
    end
    if DESC.EffectID ~= nil then
      result, msg = AddItemEffectInfo(ItemID, DESC.EffectID)
      if result ~= true then return false, msg end
    end
    if DESC.costume ~= nil then
      result, msg = AddItemIsCostume(ItemID, DESC.costume)
      if result ~= true then return false, msg end
    end
  end
  return true, ""good""
end
";

    public string WriteCustomFile(string clientPatchRoot, IEnumerable<ClientItemInfoEntry> entries, string? baseSystemItemInfoFileName = "itemInfo.lub")
    {
        if (string.IsNullOrWhiteSpace(clientPatchRoot))
            throw new ArgumentException("clientPatchRoot is null/empty");

        var systemOut = ClientFileLocator.EnsureSystemOut(clientPatchRoot);
        var outPath = Path.Combine(systemOut, "itemInfo_rodbeditor.lua");

        var sb = new StringBuilder();

        sb.AppendLine("-- RoDbEditor: Custom ItemInfo overlay");
        sb.AppendLine("-- SAFE: does not rewrite official itemInfo.lub; intended to be loaded by client patching or manual include.");
        sb.AppendLine("-- If your client supports dofile + AddItem API, this will work out-of-the-box.");
        sb.AppendLine();

        sb.AppendLine($"dofile(\"System/{baseSystemItemInfoFileName}\")");
        sb.AppendLine();

        sb.AppendLine("tbl = {");

        foreach (var e in entries.OrderBy(x => x.Id))
        {
            sb.Append(RenderEntry(e));
        }

        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("if AddItem ~= nil then");
        sb.AppendLine(AddItemFunction);
        sb.AppendLine("  main()");
        sb.AppendLine("else");
        sb.AppendLine("  -- Fallback: table merge for table-style itemInfo runtimes.");
        sb.AppendLine("  if itemInfo == nil and tbl2 == nil and tbl == nil then");
        sb.AppendLine("    itemInfo = {}");
        sb.AppendLine("  end");
        sb.AppendLine("  local target = itemInfo or tbl2 or tbl");
        sb.AppendLine("  for k,v in pairs(tbl) do target[k] = v end");
        sb.AppendLine("end");
        sb.AppendLine();

        File.WriteAllText(outPath, sb.ToString(), Encoding.Default);

        return outPath;
    }

    /// <summary>Render a single item entry as Lua table snippet for preview or overlay.</summary>
    public static string RenderEntry(ClientItemInfoEntry e)
    {
        var uName = EscapeLua(e.UnidentifiedDisplayName ?? "????");
        var uRes = EscapeLua(e.UnidentifiedResourceName ?? (e.IdentifiedResourceName ?? ""));
        var iName = EscapeLua(e.IdentifiedDisplayName ?? "");
        var iRes = EscapeLua(e.IdentifiedResourceName ?? e.UnidentifiedResourceName ?? "");

        var sb = new StringBuilder();
        sb.AppendLine($"  [{e.Id}] = {{");
        sb.AppendLine($"    unidentifiedDisplayName = \"{uName}\",");
        sb.AppendLine($"    unidentifiedResourceName = \"{uRes}\",");

        sb.AppendLine("    unidentifiedDescriptionName = {");
        foreach (var line in (e.UnidentifiedDescriptionName.Count > 0 ? e.UnidentifiedDescriptionName : new List<string> { "Unidentified item." }))
            sb.AppendLine($"      \"{EscapeLua(line)}\",");
        sb.AppendLine("    },");

        sb.AppendLine($"    identifiedDisplayName = \"{iName}\",");
        sb.AppendLine($"    identifiedResourceName = \"{iRes}\",");

        sb.AppendLine("    identifiedDescriptionName = {");
        foreach (var line in (e.IdentifiedDescriptionName.Count > 0 ? e.IdentifiedDescriptionName : new List<string> { "TODO: description" }))
            sb.AppendLine($"      \"{EscapeLua(line)}\",");
        sb.AppendLine("    },");

        sb.AppendLine($"    slotCount = {e.SlotCount},");
        sb.AppendLine($"    ClassNum = {e.ClassNum},");

        if (e.EffectID.HasValue)
            sb.AppendLine($"    EffectID = {e.EffectID.Value},");
        if (e.Costume.HasValue)
            sb.AppendLine($"    costume = {(e.Costume.Value ? "true" : "false")},");

        sb.AppendLine("  },");
        return sb.ToString();
    }

    private static string EscapeLua(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
