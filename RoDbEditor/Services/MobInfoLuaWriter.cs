using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using RoDbEditor.Models;

namespace RoDbEditor.Services;

/// <summary>
/// Writes custom mob entries to the client's mobinfo overlay so the client
/// can display custom monster sprites instead of "Unknown" or wrong sprites.
/// </summary>
public class MobInfoLuaWriter
{
    private readonly string _clientRoot;
    private const string MobInfoPath = "data/luafiles514/lua files/datainfo/mobinfo_custom.lua";

    public MobInfoLuaWriter(string clientRoot)
    {
        _clientRoot = clientRoot ?? "";
    }

    public string GetMobInfoPath() => Path.Combine(_clientRoot, MobInfoPath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Write or update a custom mob entry in mobinfo_custom.lua.</summary>
    public void WriteEntry(MobEntry mob)
    {
        var path = GetMobInfoPath();
        if (string.IsNullOrWhiteSpace(_clientRoot) || !Directory.Exists(_clientRoot))
            throw new InvalidOperationException("Client root is not set or does not exist.");

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var content = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
        var newContent = UpsertEntryInContent(content, mob);
        File.WriteAllText(path, newContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Remove an entry by mob ID.</summary>
    public bool RemoveEntry(int mobId)
    {
        var path = GetMobInfoPath();
        if (!File.Exists(path))
            return true;

        var content = File.ReadAllText(path, Encoding.UTF8);
        var newContent = RemoveEntryFromContent(content, mobId);
        if (newContent == null)
            return false;

        File.WriteAllText(path, newContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return true;
    }

    private string UpsertEntryInContent(string content, MobEntry mob)
    {
        var block = BuildLuaEntry(mob);
        var marker = $"[{mob.Id}] = {{";

        var existingStart = content.IndexOf(marker, StringComparison.Ordinal);
        if (existingStart >= 0)
        {
            var blockEnd = FindMatchingBlockEnd(content, existingStart + marker.Length - 1);
            if (blockEnd >= 0)
                return content.Remove(existingStart, blockEnd - existingStart).Insert(existingStart, block);
        }

        var header = string.IsNullOrWhiteSpace(content) || !content.Contains("mob_tbl")
            ? "-- Custom mobs (RoDbEditor)\nmob_tbl = mob_tbl or {}\n\n"
            : "";
        if (string.IsNullOrWhiteSpace(content))
            content = header;
        else if (!content.TrimEnd().EndsWith("\n"))
            content = content.TrimEnd() + "\n";
        content += block.TrimEnd() + "\n";
        return content;
    }

    private static int FindMatchingBlockEnd(string content, int openBraceIndex)
    {
        int depth = 1;
        for (int i = openBraceIndex + 1; i < content.Length; i++)
        {
            var c = content[i];
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    var j = i + 1;
                    while (j < content.Length && (content[j] == ',' || char.IsWhiteSpace(content[j])))
                        j++;
                    return j;
                }
            }
        }
        return -1;
    }

    private static string? RemoveEntryFromContent(string content, int mobId)
    {
        var marker = $"[{mobId}] = {{";
        var start = content.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;

        var blockEnd = FindMatchingBlockEnd(content, start + marker.Length - 1);
        if (blockEnd < 0) return null;

        var toRemove = content.Substring(start, blockEnd - start).TrimEnd();
        if (toRemove.EndsWith(","))
            toRemove = toRemove.TrimEnd(',').TrimEnd();
        return content.Remove(start, blockEnd - start).TrimEnd();
    }

    private static string BuildLuaEntry(MobEntry mob)
    {
        var sprite = string.IsNullOrEmpty(mob.AegisName) ? "Custom_Mob" : mob.AegisName;
        var name = string.IsNullOrEmpty(mob.Name) ? mob.AegisName : mob.Name;
        if (string.IsNullOrEmpty(name)) name = "Custom Monster";

        return $@"mob_tbl[{mob.Id}] = {{
	sprite = ""{EscapeLua(sprite)}"",
	koreanName = ""{EscapeLua(name)}""
}},
";
    }

    private static string EscapeLua(string s)
    {
        return s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
    }
}
