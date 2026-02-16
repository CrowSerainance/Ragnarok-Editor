using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using RoDbEditor.Models;

namespace RoDbEditor.Services;

/// <summary>
/// Writes accessoryid.lua and accname.lua entries for headgear View ID mapping
/// so the client can render equipped sprites correctly.
/// </summary>
public class AccessoryIdWriter
{
    private readonly string _clientRoot;
    private const string AccIdPath = "data/luafiles514/lua files/datainfo/accessoryid.lua";
    private const string AccNamePath = "data/luafiles514/lua files/datainfo/accname.lua";

    public AccessoryIdWriter(string clientRoot)
    {
        _clientRoot = clientRoot ?? "";
    }

    public string GetAccessoryIdPath() => Path.Combine(_clientRoot, AccIdPath.Replace('/', Path.DirectorySeparatorChar));
    public string GetAccNamePath() => Path.Combine(_clientRoot, AccNamePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Write or update accessoryid + accname entries for headgear with a View value.</summary>
    public void WriteEntry(ItemEntry item)
    {
        if (string.IsNullOrWhiteSpace(_clientRoot) || !Directory.Exists(_clientRoot))
            throw new InvalidOperationException("Client root is not set or does not exist.");

        var view = item.View ?? 0;
        if (view <= 0)
            return; // No View = not headgear with sprite mapping

        var rawName = !string.IsNullOrWhiteSpace(item.ResourceName) ? item.ResourceName : item.AegisName;
        var resourceName = NormalizeResourceName(string.IsNullOrWhiteSpace(rawName) ? "Custom_Item" : rawName!);
        var constName = ToAccessoryConstantName(resourceName);

        WriteAccessoryId(constName, view);
        WriteAccName(constName, resourceName);
    }

    /// <summary>Remove accessoryid + accname entry. Pass viewId and aegisName (for const name).</summary>
    public bool RemoveEntry(int viewId, string? aegisName = null)
    {
        if (viewId <= 0) return true;
        var idOk = RemoveFromAccessoryId(viewId);
        var constName = !string.IsNullOrEmpty(aegisName) ? ToAccessoryConstantName(aegisName) : null;
        var nameOk = constName != null && RemoveFromAccName(constName);
        return idOk || nameOk;
    }

    private static string ToAccessoryConstantName(string aegisName)
    {
        var sb = new StringBuilder();
        foreach (var c in aegisName)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c == ' ' ? '_' : c);
        }
        var name = sb.ToString();
        if (string.IsNullOrEmpty(name)) name = "Custom_Item";
        return "ACCESSORY_" + name.Replace(" ", "_");
    }

    private void WriteAccessoryId(string constName, int viewId)
    {
        var path = GetAccessoryIdPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var content = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
        var line = $"{constName} = {viewId},";

        var marker = $"{constName} = ";
        var existingIdx = content.IndexOf(marker, StringComparison.Ordinal);
        if (existingIdx >= 0)
        {
            var end = content.IndexOf('\n', existingIdx);
            if (end < 0) end = content.Length;
            content = content.Remove(existingIdx, end - existingIdx).Insert(existingIdx, line);
        }
        else
        {
            var header = string.IsNullOrWhiteSpace(content)
                ? "-- Custom accessories (RoDbEditor)\n"
                : "";
            if (!content.TrimEnd().EndsWith(",") && !string.IsNullOrEmpty(content.Trim()))
                content = content.TrimEnd() + "\n";
            content = header + content.TrimEnd() + (string.IsNullOrEmpty(content.Trim()) ? "" : "\n") + line + "\n";
        }

        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private bool RemoveFromAccessoryId(int viewId)
    {
        var path = GetAccessoryIdPath();
        if (!File.Exists(path)) return true;

        var content = File.ReadAllText(path, Encoding.UTF8);
        var pattern = $"ACCESSORY_[\\w_]+\\s*=\\s*{viewId}\\s*,?\\s*";
        var match = Regex.Match(content, pattern);
        if (!match.Success) return false;

        content = content.Remove(match.Index, match.Length);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return true;
    }

    private void WriteAccName(string constName, string resourceName)
    {
        var path = GetAccNamePath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var spriteName = NormalizeResourceName(resourceName);
        var content = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";

        var marker = $"[ACCESSORY_IDs.{constName}]";
        var existingIdx = content.IndexOf(marker, StringComparison.Ordinal);
        var line = $"[ACCESSORY_IDs.{constName}] = \"{EscapeLua(spriteName)}\",";

        if (existingIdx >= 0)
        {
            var end = content.IndexOf('\n', existingIdx);
            if (end < 0) end = content.Length;
            content = content.Remove(existingIdx, end - existingIdx).Insert(existingIdx, line);
        }
        else
        {
            var header = string.IsNullOrWhiteSpace(content)
                ? "-- Custom accessory names (RoDbEditor)\nACCESSORY_IDs = ACCESSORY_IDs or {}\n"
                : "";
            if (!content.Contains("ACCESSORY_IDs = ACCESSORY_IDs"))
            {
                var idx = content.IndexOf("ACCESSORY_IDs", StringComparison.Ordinal);
                if (idx < 0)
                    content = header + content.TrimEnd() + "\n" + line + "\n";
                else
                    content = content.TrimEnd() + "\n" + line + "\n";
            }
            else
                content = content.TrimEnd() + "\n" + line + "\n";
        }

        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private bool RemoveFromAccName(string constName)
    {
        var path = GetAccNamePath();
        if (!File.Exists(path)) return true;

        var content = File.ReadAllText(path, Encoding.UTF8);
        var marker = $"[ACCESSORY_IDs.{constName}]";
        var idx = content.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return false;

        var end = content.IndexOf('\n', idx);
        if (end < 0) end = content.Length;
        var toRemove = content.Substring(idx, end - idx).TrimEnd();
        content = content.Remove(idx, end - idx);
        if (idx > 0 && content[idx - 1] == '\n' && idx < content.Length && content[idx] == '\n')
            content = content.Remove(idx, 1);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return true;
    }

    private static string EscapeLua(string s)
    {
        return s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
    }

    private static string NormalizeResourceName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Custom_Item";

        var sb = new StringBuilder();
        foreach (var c in value.Trim())
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
            else if (char.IsWhiteSpace(c))
                sb.Append('_');
        }

        var normalized = sb.ToString();
        return string.IsNullOrWhiteSpace(normalized) ? "Custom_Item" : normalized;
    }
}
