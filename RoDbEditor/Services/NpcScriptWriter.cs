using System;
using System.IO;
using System.Text;

namespace RoDbEditor.Services;

/// <summary>
/// Creates minimal NPC script files in npc/custom/ for new NPCs from Extracted Assets.
/// </summary>
public class NpcScriptWriter
{
    private readonly string _dataPath;

    public NpcScriptWriter(string dataPath)
    {
        _dataPath = dataPath ?? "";
    }

    public string GetNpcCustomDir() => Path.Combine(_dataPath, "npc", "custom");

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

        var spritePart = int.TryParse(spriteId, out var num) ? num.ToString() : spriteId;
        var content = $@"// RoDbEditor - Custom NPC
{map},{x},{y},{direction}	script	{name}	{spritePart},{{
	mes ""Hello."";
	close;
}}
";

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
