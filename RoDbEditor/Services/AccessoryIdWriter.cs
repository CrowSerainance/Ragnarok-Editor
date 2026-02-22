using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using GRF;
using GRF.FileFormats.LubFormat;
using RoDbEditor;
using RoDbEditor.Models;
using RoDbEditor.Core;
using Utilities.Parsers.Lua;

namespace RoDbEditor.Services;

/// <summary>
/// Writes accessoryid.lua and accname.lua entries for headgear View ID mapping
/// so the client can render equipped sprites correctly.
/// </summary>
public class AccessoryIdWriter
{
    private readonly string _clientRoot;
    private readonly GrfService? _grfService;
    private readonly GrfWriterService? _grfWriter;

    // In-memory cache of last-written content to avoid stale reads in the same session.
    private string? _cachedAccessoryIdContent;
    private string? _cachedAccNameContent;

    private const string AccIdPath = "data/luafiles514/lua files/datainfo/accessoryid.lua";
    private const string AccNamePath = "data/luafiles514/lua files/datainfo/accname.lua";
    private const string AccIdGrfPath = @"data\luafiles514\lua files\datainfo\accessoryid.lua";
    private const string AccIdLubGrfPath = @"data\luafiles514\lua files\datainfo\accessoryid.lub";
    private const string AccNameGrfPath = @"data\luafiles514\lua files\datainfo\accname.lua";
    private const string AccNameLubGrfPath = @"data\luafiles514\lua files\datainfo\accname.lub";

    public AccessoryIdWriter(string clientRoot, GrfService? grfService = null, GrfWriterService? grfWriter = null)
    {
        _clientRoot = clientRoot ?? "";
        _grfService = grfService;
        _grfWriter = grfWriter;
    }

    public string GetAccessoryIdPath() => Path.Combine(_clientRoot, AccIdPath.Replace('/', Path.DirectorySeparatorChar));
    public string GetAccNamePath() => Path.Combine(_clientRoot, AccNamePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Write or update accessoryid + accname entries for headgear with a View value.</summary>
    public void WriteEntry(ItemEntry item)
    {
        var view = item.View ?? 0;
        if (view <= 0)
            return; // No View = not headgear with sprite mapping

        var rawName = !string.IsNullOrWhiteSpace(item.ResourceName) ? item.ResourceName : item.AegisName;
        var resourceName = NormalizeResourceName(string.IsNullOrWhiteSpace(rawName) ? "Custom_Item" : rawName!);
        var constName = ToAccessoryConstantName(resourceName);

        var targetGrfPath = ResolveTargetGrfPath();
        if (string.IsNullOrWhiteSpace(targetGrfPath))
            throw new InvalidOperationException("Target GRF path is not configured.");

        var accessoryIdContent = LoadContent(
            ref _cachedAccessoryIdContent,
            AccIdGrfPath,
            AccIdLubGrfPath,
            "ACCESSORY_IDs = {\n}\n");
        accessoryIdContent = UpsertTableEntry(
            accessoryIdContent,
            "ACCESSORY_IDs",
            constName,
            $"{constName} = {view},");
        WriteContentToGrf(targetGrfPath, AccIdGrfPath, accessoryIdContent);
        _cachedAccessoryIdContent = accessoryIdContent;

        var accNameContent = LoadContent(
            ref _cachedAccNameContent,
            AccNameGrfPath,
            AccNameLubGrfPath,
            "AccNameTable = {\n}\n");
        accNameContent = UpsertTableEntry(
            accNameContent,
            "AccNameTable",
            $"[ACCESSORY_IDs.{constName}]",
            $"[ACCESSORY_IDs.{constName}] = \"_{EscapeLua(resourceName)}\",");
        WriteContentToGrf(targetGrfPath, AccNameGrfPath, accNameContent);
        _cachedAccNameContent = accNameContent;
    }

    /// <summary>Remove accessoryid + accname entry. Pass viewId and aegisName (for const name).</summary>
    public bool RemoveEntry(int viewId, string? aegisName = null)
    {
        if (viewId <= 0) return true;
        var targetGrfPath = ResolveTargetGrfPath();
        if (string.IsNullOrWhiteSpace(targetGrfPath))
            return false;

        var changed = false;

        var accessoryIdContent = LoadContent(
            ref _cachedAccessoryIdContent,
            AccIdGrfPath,
            AccIdLubGrfPath,
            "ACCESSORY_IDs = {\n}\n");
        var updatedAccessoryId = RemoveAccessoryIdByView(accessoryIdContent, viewId);
        if (!ReferenceEquals(updatedAccessoryId, accessoryIdContent))
        {
            WriteContentToGrf(targetGrfPath, AccIdGrfPath, updatedAccessoryId);
            _cachedAccessoryIdContent = updatedAccessoryId;
            changed = true;
        }

        var constName = !string.IsNullOrWhiteSpace(aegisName) ? ToAccessoryConstantName(aegisName) : null;
        if (!string.IsNullOrWhiteSpace(constName))
        {
            var accNameContent = LoadContent(
                ref _cachedAccNameContent,
                AccNameGrfPath,
                AccNameLubGrfPath,
                "AccNameTable = {\n}\n");
            var updatedAccName = RemoveAccNameByConstant(accNameContent, constName!);
            if (!ReferenceEquals(updatedAccName, accNameContent))
            {
                WriteContentToGrf(targetGrfPath, AccNameGrfPath, updatedAccName);
                _cachedAccNameContent = updatedAccName;
                changed = true;
            }
        }

        return changed;
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

    private string? ResolveTargetGrfPath()
    {
        var config = App.Config;
        if (!string.IsNullOrWhiteSpace(config?.TargetGrfPath))
            return config.TargetGrfPath;

        if (string.IsNullOrWhiteSpace(_clientRoot))
            return null;

        var fileName = string.IsNullOrWhiteSpace(config?.TargetGrfFileName)
            ? "custom.grf"
            : config!.TargetGrfFileName;
        return Path.Combine(_clientRoot, fileName);
    }

    private string LoadContent(
        ref string? cache,
        string luaGrfPath,
        string lubGrfPath,
        string fallbackContent)
    {
        if (!string.IsNullOrEmpty(cache))
            return cache;

        if (_grfService != null)
        {
            var luaData = _grfService.GetData(luaGrfPath);
            if (luaData != null && luaData.Length > 0)
            {
                cache = DecompileToLua(luaData);
                return cache;
            }

            var lubData = _grfService.GetData(lubGrfPath);
            if (lubData != null && lubData.Length > 0)
            {
                cache = DecompileToLua(lubData);
                return cache;
            }
        }

        cache = fallbackContent;
        return cache;
    }

    private void WriteContentToGrf(string grfPath, string grfInternalPath, string content)
    {
        if (_grfWriter == null)
            throw new InvalidOperationException("GrfWriterService is not available.");

        var result = _grfWriter.AddContentToGrf(grfPath, grfInternalPath, content, new UTF8Encoding(false));
        if (result.AddedPaths.Count == 0)
        {
            var details = result.Errors.Count > 0
                ? string.Join("; ", result.Errors)
                : "Unknown write failure";
            throw new InvalidOperationException($"Failed writing '{grfInternalPath}' to '{grfPath}': {details}");
        }
    }

    private static string UpsertTableEntry(string content, string tableName, string key, string fullLine)
    {
        if (!TryFindTableRange(content, tableName, out var openBrace, out var closeBrace))
        {
            var nl = DetectNewline(content);
            var headerPrefix = string.IsNullOrWhiteSpace(content) ? "" : content.TrimEnd() + nl + nl;
            return $"{headerPrefix}{tableName} = {{{nl}    {fullLine}{nl}}}{nl}";
        }

        var tableSection = content.Substring(openBrace + 1, closeBrace - openBrace - 1);
        var lineRegex = new Regex(
            @"(?m)^(?<indent>[ \t]*)" + Regex.Escape(key) + @"\s*=\s*.*?(?:\r?\n|$)");
        var match = lineRegex.Match(tableSection);
        if (match.Success)
        {
            var replacement = match.Groups["indent"].Value + fullLine + DetectNewline(content);
            var updatedSection = lineRegex.Replace(tableSection, replacement, 1);
            return content[..(openBrace + 1)] + updatedSection + content[closeBrace..];
        }

        var indent = DetectIndent(tableSection);
        var nlInsert = DetectNewline(content);
        var insertAt = closeBrace;
        var prefix = insertAt > 0 && content[insertAt - 1] != '\n' && content[insertAt - 1] != '\r'
            ? nlInsert
            : string.Empty;
        var insertion = $"{prefix}{indent}{fullLine}{nlInsert}";
        return content.Insert(insertAt, insertion);
    }

    private static string RemoveAccessoryIdByView(string content, int viewId)
    {
        return RemoveTableLine(
            content,
            "ACCESSORY_IDs",
            @"(?m)^[ \t]*ACCESSORY_[\w_]+\s*=\s*" + Regex.Escape(viewId.ToString()) + @"\s*,?\s*(?:\r?\n)?");
    }

    private static string RemoveAccNameByConstant(string content, string constName)
    {
        return RemoveTableLine(
            content,
            "AccNameTable",
            @"(?m)^[ \t]*\[ACCESSORY_IDs\." + Regex.Escape(constName) + @"\]\s*=\s*.*?,?\s*(?:\r?\n)?");
    }

    private static string RemoveTableLine(string content, string tableName, string linePattern)
    {
        if (!TryFindTableRange(content, tableName, out var openBrace, out var closeBrace))
            return content;

        var tableSection = content.Substring(openBrace + 1, closeBrace - openBrace - 1);
        var regex = new Regex(linePattern);
        var match = regex.Match(tableSection);
        if (!match.Success)
            return content;

        var updatedSection = tableSection.Remove(match.Index, match.Length);
        return content[..(openBrace + 1)] + updatedSection + content[closeBrace..];
    }

    private static bool TryFindTableRange(string content, string tableName, out int openBrace, out int closeBrace)
    {
        openBrace = -1;
        closeBrace = -1;
        if (string.IsNullOrEmpty(content))
            return false;

        var tableStart = Regex.Match(content, Regex.Escape(tableName) + @"\s*=\s*\{");
        if (!tableStart.Success)
            return false;

        openBrace = content.IndexOf('{', tableStart.Index);
        if (openBrace < 0)
            return false;

        var depth = 0;
        for (var i = openBrace; i < content.Length; i++)
        {
            var c = content[i];
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    closeBrace = i;
                    return true;
                }
            }
        }

        return false;
    }

    private static string DetectIndent(string tableSection)
    {
        var match = Regex.Match(tableSection, @"(?m)^(?<indent>[ \t]+)\S");
        return match.Success ? match.Groups["indent"].Value : "    ";
    }

    private static string DetectNewline(string content)
    {
        return content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }

    private static string DecompileToLua(byte[] data)
    {
        try
        {
            if (LuaParser.IsLub(data))
            {
                MultiType mt = data;
                var lub = new Lub(mt);
                return lub.Decompile();
            }
            try { return Encoding.UTF8.GetString(data); }
            catch { return Encoding.GetEncoding(949).GetString(data); }
        }
        catch
        {
            try { return Encoding.UTF8.GetString(data); }
            catch { return Encoding.GetEncoding(949).GetString(data); }
        }
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
