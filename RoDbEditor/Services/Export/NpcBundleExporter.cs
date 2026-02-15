using System;
using System.IO;
using System.Text;
using RoDbEditor.Models;

namespace RoDbEditor.Services.Export;

public sealed class NpcBundleExporter
{
    public void AddNpc(ExportBundle bundle, NpcScriptEntry npc, string? editedScriptText, bool includeClientIdentity, bool includeAssetNotes)
    {
        var npcPath = SuggestNpcCustomPath(npc);
        bundle.Files.Add(new ExportFile(
            npcPath,
            RenderNpcScript(npc, editedScriptText),
            ExportWriteMode.Replace));

        bundle.Files.Add(new ExportFile(
            "npc/custom/scripts_custom.conf",
            $"npc: {npcPath.Replace('\\', '/')} // RoDbEditor {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            ExportWriteMode.Append));

        if (includeClientIdentity && !string.IsNullOrWhiteSpace(npc.SpriteId) && !string.IsNullOrWhiteSpace(npc.Name))
        {
            bundle.Files.Add(new ExportFile(
                @"data/luafiles514/lua files/datainfo/npcidentity.lub",
                $"[\"{npc.Name.ToUpperInvariant()}\"] = {npc.SpriteId}, // RoDbEditor {DateTime.Now:yyyy-MM-dd}",
                ExportWriteMode.Append));
        }

        bundle.Commands.Add("@reloadscript");
        bundle.Notes.Add("Use npc/custom and include file via npc/custom/scripts_custom.conf.");
        if (includeAssetNotes)
            bundle.Notes.Add("Install NPC sprite assets through your client overlay/GRF pipeline.");
    }

    private static string SuggestNpcCustomPath(NpcScriptEntry npc)
    {
        var fromFile = Path.GetFileNameWithoutExtension(npc.FilePath ?? "");
        if (!string.IsNullOrWhiteSpace(fromFile))
            return $"npc/custom/{fromFile}.txt";

        var safeName = string.IsNullOrWhiteSpace(npc.Name) ? "custom_npc" : npc.Name;
        foreach (var c in Path.GetInvalidFileNameChars())
            safeName = safeName.Replace(c, '_');
        return $"npc/custom/{safeName}.txt";
    }

    private static string RenderNpcScript(NpcScriptEntry npc, string? editedScriptText)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// RoDbEditor: {DateTime.Now:yyyy-MM-dd HH:mm:ss} NPC EXPORT");

        if (!string.IsNullOrWhiteSpace(editedScriptText))
        {
            sb.AppendLine(editedScriptText.TrimEnd());
            return sb.ToString();
        }

        if (npc.Type == NpcScriptType.Script)
        {
            if (!string.IsNullOrWhiteSpace(npc.RawLine))
                sb.AppendLine(npc.RawLine.TrimEnd());
            if (!string.IsNullOrWhiteSpace(npc.ScriptBody))
                sb.AppendLine(npc.ScriptBody.TrimEnd());
            return sb.ToString();
        }

        if (!string.IsNullOrWhiteSpace(npc.RawLine))
        {
            sb.AppendLine(npc.RawLine.TrimEnd());
            return sb.ToString();
        }

        sb.AppendLine($"{npc.Map},{npc.X},{npc.Y},{npc.Direction}\tscript\t{npc.Name}\t{npc.SpriteId},{{");
        sb.AppendLine("\tmes \"Hello.\";");
        sb.AppendLine("\tclose;");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
