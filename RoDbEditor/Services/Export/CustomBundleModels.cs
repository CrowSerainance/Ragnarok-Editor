using System;
using System.Collections.Generic;
using System.Text;

namespace RoDbEditor.Services.Export;

public sealed class ExportBundle
{
    public List<ExportFile> Files { get; } = new();
    public List<string> Commands { get; } = new();
    public List<string> Notes { get; } = new();
}

public sealed record ExportFile(
    string VirtualPath,
    string Content,
    ExportWriteMode Mode
);

public enum ExportWriteMode
{
    Append,
    Replace
}

public static class ExportBundleText
{
    public static string ToClipboardText(ExportBundle bundle)
    {
        var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var sb = new StringBuilder();
        sb.AppendLine($"// RoDbEditor Export Bundle ({ts})");
        sb.AppendLine("// Paste each FILE block into the matching file.");
        sb.AppendLine();

        foreach (var file in bundle.Files)
        {
            sb.AppendLine($"===== FILE: {file.VirtualPath} (Mode: {file.Mode}) =====");
            sb.AppendLine((file.Content ?? "").TrimEnd());
            sb.AppendLine($"===== END FILE: {file.VirtualPath} =====");
            sb.AppendLine();
        }

        if (bundle.Commands.Count > 0)
        {
            sb.AppendLine("===== COMMANDS =====");
            foreach (var command in bundle.Commands)
                sb.AppendLine(command);
            sb.AppendLine("===== END COMMANDS =====");
            sb.AppendLine();
        }

        if (bundle.Notes.Count > 0)
        {
            sb.AppendLine("===== NOTES =====");
            foreach (var note in bundle.Notes)
                sb.AppendLine("- " + note);
            sb.AppendLine("===== END NOTES =====");
        }

        return sb.ToString();
    }
}
