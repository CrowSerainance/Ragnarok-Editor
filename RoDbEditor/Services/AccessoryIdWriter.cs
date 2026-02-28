using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using GRF;
using GRF.Core;
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

    // Legacy wrong paths (missing "lua files" subfolder) — used for migration only
    private const string AccIdGrfPathLegacy = @"data\luafiles514\datainfo\accessoryid.lua";
    private const string AccIdLubGrfPathLegacy = @"data\luafiles514\datainfo\accessoryid.lub";
    private const string AccNameGrfPathLegacy = @"data\luafiles514\datainfo\accname.lua";
    private const string AccNameLubGrfPathLegacy = @"data\luafiles514\datainfo\accname.lub";
    private const string AccIdGrfPathLegacyRoot = @"data\lua files\datainfo\accessoryid.lua";
    private const string AccIdLubGrfPathLegacyRoot = @"data\lua files\datainfo\accessoryid.lub";
    private const string AccNameGrfPathLegacyRoot = @"data\lua files\datainfo\accname.lua";
    private const string AccNameLubGrfPathLegacyRoot = @"data\lua files\datainfo\accname.lub";

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
        {
            Debug.WriteLine($"[AccessoryIdWriter] Skipped: View={item.View} (no view or <= 0) for {item.AegisName}");
            return; // No View = not headgear with sprite mapping
        }

        var rawName = !string.IsNullOrWhiteSpace(item.ResourceName) ? item.ResourceName : item.AegisName;
        var resourceName = NormalizeResourceName(string.IsNullOrWhiteSpace(rawName) ? "Custom_Item" : rawName!);
        var constName = ToAccessoryConstantName(resourceName);

        Debug.WriteLine($"[AccessoryIdWriter] WriteEntry: Id={item.Id}, View={view}, Resource={resourceName}, Const={constName}");

        var targetGrfPath = ResolveTargetGrfPath();
        if (string.IsNullOrWhiteSpace(targetGrfPath))
            throw new InvalidOperationException("Target GRF path is not configured.");

        Debug.WriteLine($"[AccessoryIdWriter] Target GRF: {targetGrfPath}");

        var accessoryIdContent = LoadContent(
            ref _cachedAccessoryIdContent,
            AccIdGrfPath,
            AccIdLubGrfPath,
            "ACCESSORY_IDs = {\n}\n",
            AccIdGrfPathLegacy,
            AccIdLubGrfPathLegacy,
            AccIdGrfPathLegacyRoot,
            AccIdLubGrfPathLegacyRoot);

        // Build on top of current base client data to avoid stale custom.grf tables
        // missing newer ACCESSORY_* constants used by files like TB_Layer_Priority.
        var baseAccessoryId = TryLoadBaseDatainfoFromDataGrf(AccIdLubGrfPath);
        if (!string.IsNullOrWhiteSpace(baseAccessoryId))
            accessoryIdContent = MergeAccessoryIdTable(baseAccessoryId, accessoryIdContent);

        Debug.WriteLine($"[AccessoryIdWriter] accessoryid content loaded: {accessoryIdContent.Length} chars");
        var expectedAccIdLine = $"{constName} = {view},";
        accessoryIdContent = UpsertTableEntry(
            accessoryIdContent,
            "ACCESSORY_IDs",
            constName,
            expectedAccIdLine);

        // Post-upsert verification: make sure the entry actually exists in the content
        if (!accessoryIdContent.Contains(expectedAccIdLine))
            throw new InvalidOperationException(
                $"[AccessoryIdWriter] BUG: UpsertTableEntry did not insert '{expectedAccIdLine}' into accessoryid content.");

        WriteDatainfoLuaAndLub(targetGrfPath, AccIdGrfPath, AccIdLubGrfPath, accessoryIdContent);
        WriteDatainfoLuaAndLub(targetGrfPath, AccIdGrfPathLegacy, AccIdLubGrfPathLegacy, accessoryIdContent);
        WriteDatainfoLuaAndLub(targetGrfPath, AccIdGrfPathLegacyRoot, AccIdLubGrfPathLegacyRoot, accessoryIdContent);
        _cachedAccessoryIdContent = accessoryIdContent;
        Debug.WriteLine($"[AccessoryIdWriter] accessoryid.lua written to GRF ({accessoryIdContent.Length} chars) — verified '{expectedAccIdLine}'");

        var accNameContent = LoadContent(
            ref _cachedAccNameContent,
            AccNameGrfPath,
            AccNameLubGrfPath,
            "AccNameTable = {\n}\n",
            AccNameGrfPathLegacy,
            AccNameLubGrfPathLegacy,
            AccNameGrfPathLegacyRoot,
            AccNameLubGrfPathLegacyRoot);

        var baseAccName = TryLoadBaseDatainfoFromDataGrf(AccNameLubGrfPath);
        if (!string.IsNullOrWhiteSpace(baseAccName))
            accNameContent = MergeAccNameTable(baseAccName, accNameContent);

        Debug.WriteLine($"[AccessoryIdWriter] accname content loaded: {accNameContent.Length} chars");

        var expectedAccNameLine = $"[ACCESSORY_IDs.{constName}] = \"_{EscapeLua(resourceName)}\",";
        accNameContent = UpsertTableEntry(
            accNameContent,
            "AccNameTable",
            $"[ACCESSORY_IDs.{constName}]",
            expectedAccNameLine);

        // Post-upsert verification
        if (!accNameContent.Contains(expectedAccNameLine))
            throw new InvalidOperationException(
                $"[AccessoryIdWriter] BUG: UpsertTableEntry did not insert '{expectedAccNameLine}' into accname content.");

        WriteDatainfoLuaAndLub(targetGrfPath, AccNameGrfPath, AccNameLubGrfPath, accNameContent);
        WriteDatainfoLuaAndLub(targetGrfPath, AccNameGrfPathLegacy, AccNameLubGrfPathLegacy, accNameContent);
        WriteDatainfoLuaAndLub(targetGrfPath, AccNameGrfPathLegacyRoot, AccNameLubGrfPathLegacyRoot, accNameContent);
        _cachedAccNameContent = accNameContent;
        Debug.WriteLine($"[AccessoryIdWriter] accname.lua written to GRF ({accNameContent.Length} chars) — verified '{expectedAccNameLine}'");
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
            "ACCESSORY_IDs = {\n}\n",
            AccIdGrfPathLegacy,
            AccIdLubGrfPathLegacy,
            AccIdGrfPathLegacyRoot,
            AccIdLubGrfPathLegacyRoot);
        var updatedAccessoryId = RemoveAccessoryIdByView(accessoryIdContent, viewId);
        if (!ReferenceEquals(updatedAccessoryId, accessoryIdContent))
        {
            WriteDatainfoLuaAndLub(targetGrfPath, AccIdGrfPath, AccIdLubGrfPath, updatedAccessoryId);
            WriteDatainfoLuaAndLub(targetGrfPath, AccIdGrfPathLegacy, AccIdLubGrfPathLegacy, updatedAccessoryId);
            WriteDatainfoLuaAndLub(targetGrfPath, AccIdGrfPathLegacyRoot, AccIdLubGrfPathLegacyRoot, updatedAccessoryId);
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
                "AccNameTable = {\n}\n",
                AccNameGrfPathLegacy,
                AccNameLubGrfPathLegacy,
                AccNameGrfPathLegacyRoot,
                AccNameLubGrfPathLegacyRoot);
            var updatedAccName = RemoveAccNameByConstant(accNameContent, constName!);
            if (!ReferenceEquals(updatedAccName, accNameContent))
            {
                WriteDatainfoLuaAndLub(targetGrfPath, AccNameGrfPath, AccNameLubGrfPath, updatedAccName);
                WriteDatainfoLuaAndLub(targetGrfPath, AccNameGrfPathLegacy, AccNameLubGrfPathLegacy, updatedAccName);
                WriteDatainfoLuaAndLub(targetGrfPath, AccNameGrfPathLegacyRoot, AccNameLubGrfPathLegacyRoot, updatedAccName);
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
        return "ACCESSORY_" + name.Replace(" ", "_").ToUpperInvariant();
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
        string fallbackContent,
        string? legacyLuaGrfPath = null,
        string? legacyLubGrfPath = null,
        string? legacyLuaGrfPath2 = null,
        string? legacyLubGrfPath2 = null)
    {
        if (!string.IsNullOrEmpty(cache))
        {
            Debug.WriteLine($"[AccessoryIdWriter] LoadContent: using cached content for {luaGrfPath}");
            return cache;
        }

        if (_grfService != null)
        {
            // 1) Try the correct path first (.lua)
            var luaData = _grfService.GetData(luaGrfPath);
            if (luaData != null && luaData.Length > 0)
            {
                Debug.WriteLine($"[AccessoryIdWriter] LoadContent: read {luaData.Length} bytes from GRF .lua: {luaGrfPath}");
                cache = DecompileToLua(luaData);
                return cache;
            }

            // 2) Check legacy wrong path — migrate content if found there
            if (!string.IsNullOrEmpty(legacyLuaGrfPath))
            {
                var legacyData = _grfService.GetData(legacyLuaGrfPath);
                if (legacyData != null && legacyData.Length > 0)
                {
                    Debug.WriteLine($"[AccessoryIdWriter] LoadContent: MIGRATING from legacy path: {legacyLuaGrfPath} ({legacyData.Length} bytes)");
                    cache = DecompileToLua(legacyData);
                    return cache;
                }
            }

            // 3) Check legacy .lub path as well.
            if (!string.IsNullOrEmpty(legacyLubGrfPath))
            {
                var legacyLubData = _grfService.GetData(legacyLubGrfPath);
                if (legacyLubData != null && legacyLubData.Length > 0)
                {
                    Debug.WriteLine($"[AccessoryIdWriter] LoadContent: MIGRATING from legacy .lub path: {legacyLubGrfPath} ({legacyLubData.Length} bytes)");
                    cache = DecompileToLua(legacyLubData);
                    return cache;
                }
            }

            // 3b) Check secondary legacy path(s) as well.
            if (!string.IsNullOrEmpty(legacyLuaGrfPath2))
            {
                var legacyData2 = _grfService.GetData(legacyLuaGrfPath2);
                if (legacyData2 != null && legacyData2.Length > 0)
                {
                    Debug.WriteLine($"[AccessoryIdWriter] LoadContent: MIGRATING from secondary legacy path: {legacyLuaGrfPath2} ({legacyData2.Length} bytes)");
                    cache = DecompileToLua(legacyData2);
                    return cache;
                }
            }

            if (!string.IsNullOrEmpty(legacyLubGrfPath2))
            {
                var legacyLubData2 = _grfService.GetData(legacyLubGrfPath2);
                if (legacyLubData2 != null && legacyLubData2.Length > 0)
                {
                    Debug.WriteLine($"[AccessoryIdWriter] LoadContent: MIGRATING from secondary legacy .lub path: {legacyLubGrfPath2} ({legacyLubData2.Length} bytes)");
                    cache = DecompileToLua(legacyLubData2);
                    return cache;
                }
            }

            // 4) Fall back to .lub (compiled version from base GRFs)
            var lubData = _grfService.GetData(lubGrfPath);
            if (lubData != null && lubData.Length > 0)
            {
                Debug.WriteLine($"[AccessoryIdWriter] LoadContent: read {lubData.Length} bytes from GRF .lub: {lubGrfPath} (decompiling)");
                cache = DecompileToLua(lubData);
                return cache;
            }

            Debug.WriteLine($"[AccessoryIdWriter] LoadContent: no data found in GRF for {luaGrfPath} or {lubGrfPath}");
        }
        else
        {
            Debug.WriteLine($"[AccessoryIdWriter] LoadContent: GrfService is null, using fallback");
        }

        cache = fallbackContent;
        return cache;
    }

    private void WriteDatainfoLuaAndLub(string grfPath, string luaGrfPath, string lubGrfPath, string content)
    {
        WriteContentToGrf(grfPath, luaGrfPath, content);

        // Some clients only request datainfo .lub paths at runtime.
        // Writing Lua text under the .lub filename preserves custom.grf override precedence.
        WriteContentToGrf(grfPath, lubGrfPath, content);
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
        var lastNonWs = insertAt - 1;
        while (lastNonWs >= 0 && char.IsWhiteSpace(content[lastNonWs]))
            lastNonWs--;

        var hasExistingEntries = lastNonWs >= 0 && content[lastNonWs] != '{';
        var needsSeparator = hasExistingEntries && content[lastNonWs] != ',' && content[lastNonWs] != ';';

        var prefix = insertAt > 0 && content[insertAt - 1] != '\n' && content[insertAt - 1] != '\r'
            ? nlInsert
            : string.Empty;
        if (needsSeparator)
            prefix = "," + nlInsert;

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

    /// <summary>Generate preview lines for accessoryid and accname (no file I/O). Used by output preview tab.</summary>
    public static string GetPreviewLines(int viewId, string? resourceNameOrAegisName)
    {
        if (viewId <= 0) return "";
        var raw = string.IsNullOrWhiteSpace(resourceNameOrAegisName) ? "Custom_Item" : resourceNameOrAegisName.Trim();
        var resourceName = NormalizeResourceNamePublic(raw);
        var constName = ToAccessoryConstantNamePublic(raw);
        var accNameLine = $"[ACCESSORY_IDs.{constName}] = \"_{EscapeLua(resourceName)}\",";
        var accIdLine = $"{constName} = {viewId},";
        return "-- accessoryid.lua\n" + accIdLine + "\n\n-- accname.lua\n" + accNameLine + "\n";
    }

    private static string EscapeLua(string s)
    {
        return s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
    }

    private static string ToAccessoryConstantNamePublic(string aegisName)
    {
        var sb = new StringBuilder();
        foreach (var c in aegisName)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c == ' ' ? '_' : c);
        }
        var name = sb.ToString();
        if (string.IsNullOrEmpty(name)) name = "Custom_Item";
        return "ACCESSORY_" + name.Replace(" ", "_").ToUpperInvariant();
    }

    private static string NormalizeResourceNamePublic(string value)
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

    private string? TryLoadBaseDatainfoFromDataGrf(string lubGrfPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_clientRoot))
                return null;

            var dataGrf = Path.Combine(_clientRoot, "data.grf");
            if (!File.Exists(dataGrf))
                return null;

            using var grf = new GrfHolder(dataGrf, GrfLoadOptions.OpenOrNew);

            var e = grf.FileTable[lubGrfPath];
            if (e != null)
                return DecompileToLua(e.GetDecompressedData());

            var luaPath = lubGrfPath.EndsWith(".lub", StringComparison.OrdinalIgnoreCase)
                ? lubGrfPath[..^4] + ".lua"
                : lubGrfPath;
            e = grf.FileTable[luaPath];
            if (e != null)
                return DecompileToLua(e.GetDecompressedData());
        }
        catch
        {
            // Non-fatal; keep existing behavior.
        }

        return null;
    }

    private static string MergeAccessoryIdTable(string baseContent, string overlayContent)
    {
        if (string.IsNullOrWhiteSpace(baseContent))
            return overlayContent;
        if (string.IsNullOrWhiteSpace(overlayContent))
            return baseContent;

        var merged = baseContent;
        var matches = Regex.Matches(overlayContent, @"(?m)^\s*(?<key>ACCESSORY_[A-Za-z0-9_]+)\s*=\s*[^,\r\n]+,\s*$");
        foreach (Match m in matches)
        {
            var key = m.Groups["key"].Value;
            if (string.IsNullOrWhiteSpace(key)) continue;
            var line = m.Value.Trim();
            merged = UpsertTableEntry(merged, "ACCESSORY_IDs", key, line);
        }
        return merged;
    }

    private static string MergeAccNameTable(string baseContent, string overlayContent)
    {
        if (string.IsNullOrWhiteSpace(baseContent))
            return overlayContent;
        if (string.IsNullOrWhiteSpace(overlayContent))
            return baseContent;

        var merged = baseContent;
        var matches = Regex.Matches(overlayContent, @"(?m)^\s*(?<key>\[ACCESSORY_IDs\.[^\]]+\])\s*=\s*.*?,\s*$");
        foreach (Match m in matches)
        {
            var key = m.Groups["key"].Value;
            if (string.IsNullOrWhiteSpace(key)) continue;
            var line = m.Value.Trim();
            merged = UpsertTableEntry(merged, "AccNameTable", key, line);
        }
        return merged;
    }
}
