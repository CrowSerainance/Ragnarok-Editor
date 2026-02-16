using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using RoDbEditor.Models;

namespace RoDbEditor.Services.Client;

public sealed class ClientItemInfoService
{
    private readonly Dictionary<int, ClientItemInfoEntry> _byId = new();

    public IReadOnlyDictionary<int, ClientItemInfoEntry> Entries => _byId;

    public void Clear() => _byId.Clear();

    public void LoadFromClientSystem(string clientSystemPath)
    {
        Clear();
        if (string.IsNullOrWhiteSpace(clientSystemPath) || !Directory.Exists(clientSystemPath))
            return;

        var path = ClientFileLocator.TryFindSystemFile(clientSystemPath,
            "itemInfo.lub", "itemInfo.lua", "iteminfo.lub", "iteminfo.lua");

        if (path == null) return;

        var text = File.ReadAllText(path, Encoding.Default);
        ParseInto(text);
    }

    public bool TryGet(int itemId, out ClientItemInfoEntry? entry) => _byId.TryGetValue(itemId, out entry!);

    public void Upsert(ClientItemInfoEntry entry)
    {
        if (entry.Id <= 0) return;
        _byId[entry.Id] = entry;
    }

    private void ParseInto(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '[') continue;

            var id = TryParseBracketId(text, i, out var afterBracket);
            if (id == null) continue;

            var braceStart = text.IndexOf('{', afterBracket);
            if (braceStart < 0) continue;

            var braceEnd = FindMatchingBrace(text, braceStart);
            if (braceEnd < 0) continue;

            var obj = text.Substring(braceStart, braceEnd - braceStart + 1);
            var entry = ParseEntryObject(id.Value, obj);
            _byId[id.Value] = entry;

            i = braceEnd;
        }
    }

    private static int? TryParseBracketId(string s, int start, out int afterBracket)
    {
        afterBracket = start;
        if (start >= s.Length || s[start] != '[') return null;
        var close = s.IndexOf(']', start + 1);
        if (close < 0) return null;

        var inner = s.Substring(start + 1, close - start - 1).Trim();
        afterBracket = close + 1;

        return int.TryParse(inner, out var id) ? id : null;
    }

    private static int FindMatchingBrace(string s, int openIndex)
    {
        int depth = 0;
        bool inString = false;

        for (int i = openIndex; i < s.Length; i++)
        {
            var c = s[i];
            var prev = (i > 0) ? s[i - 1] : '\0';

            if (c == '"' && prev != '\\') inString = !inString;
            if (inString) continue;

            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static ClientItemInfoEntry ParseEntryObject(int id, string obj)
    {
        var e = new ClientItemInfoEntry { Id = id };

        e.UnidentifiedDisplayName = MatchString(obj, "unidentifiedDisplayName");
        e.UnidentifiedResourceName = MatchString(obj, "unidentifiedResourceName");
        e.UnidentifiedDescriptionName = MatchStringArray(obj, "unidentifiedDescriptionName");

        e.IdentifiedDisplayName = MatchString(obj, "identifiedDisplayName");
        e.IdentifiedResourceName = MatchString(obj, "identifiedResourceName");
        e.IdentifiedDescriptionName = MatchStringArray(obj, "identifiedDescriptionName");

        e.SlotCount = MatchInt(obj, "slotCount") ?? 0;
        e.ClassNum = MatchInt(obj, "ClassNum") ?? 0;

        e.EffectID = MatchInt(obj, "EffectID");
        e.Costume = MatchBool(obj, "costume");

        return e;
    }

    private static string? MatchString(string obj, string key)
    {
        var rx = new Regex(@"\b" + Regex.Escape(key) + @"\b\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);
        var m = rx.Match(obj);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static int? MatchInt(string obj, string key)
    {
        var rx = new Regex(@"\b" + Regex.Escape(key) + @"\b\s*=\s*(\d+)", RegexOptions.IgnoreCase);
        var m = rx.Match(obj);
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    private static bool? MatchBool(string obj, string key)
    {
        var rx = new Regex(@"\b" + Regex.Escape(key) + @"\b\s*=\s*(true|false)", RegexOptions.IgnoreCase);
        var m = rx.Match(obj);
        return m.Success ? string.Equals(m.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase) : null;
    }

    private static List<string> MatchStringArray(string obj, string key)
    {
        var rx = new Regex(@"\b" + Regex.Escape(key) + @"\b\s*=\s*{(.*?)}", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var m = rx.Match(obj);
        if (!m.Success) return new List<string>();

        var inner = m.Groups[1].Value;
        var list = new List<string>();

        var sr = new Regex(@"""([^""]*)""", RegexOptions.Singleline);
        foreach (Match sm in sr.Matches(inner))
            list.Add(sm.Groups[1].Value);

        return list;
    }
}
