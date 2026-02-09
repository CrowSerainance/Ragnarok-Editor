using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.IO;
using RoDbEditor.Models;

namespace RoDbEditor.Services.Analysis;

public class NpcScriptReferenceExtractor
{
    private readonly WorkspaceIndex _index;
    private readonly HashSet<string> _scannedFiles = new(System.StringComparer.OrdinalIgnoreCase);

    public NpcScriptReferenceExtractor(WorkspaceIndex index)
    {
        _index = index;
    }

    public void Scan(NpcScriptEntry script)
    {
        if (string.IsNullOrEmpty(script.FilePath) || !File.Exists(script.FilePath))
            return;

        if (!_scannedFiles.Add(script.FilePath))
            return;

        var rawContent = File.ReadAllText(script.FilePath);
        var normalized = NormalizeScript(rawContent);
        var fileName = Path.GetFileName(script.FilePath);
        var lineIndex = BuildLineIndex(rawContent);

        const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant;

        // ITEM COMMANDS (space or paren form)
        var itemRegex = new Regex(@"
\b(?:getitem|getitem2|makeitem|delitem)\b
\s*
(?:\(\s*|\s+)
(?<val>-?\d+|""[^""]+""|[A-Za-z_][A-Za-z0-9_]*)
", Opt | RegexOptions.IgnorePatternWhitespace);

        foreach (Match m in itemRegex.Matches(normalized))
        {
            var val = m.Groups["val"].Value;
            AddRef(EntityKind.Item, val, "Item",
                ExtractSnippet(rawContent, m.Index),
                script.FilePath,
                GetLine(lineIndex, m.Index),
                fileName);
        }

        // MONSTER (space or paren): map,x,y,name,mob_id,...
        var monsterRegex = new Regex(@"
\bmonster\b
\s*
(?:\(\s*|\s+)
(?:(?:\s*""[^""]*""|[^,;()]+)\s*,){4}
\s*(?<val>-?\d+|""[^""]+""|[A-Za-z_][A-Za-z0-9_]*)
", Opt | RegexOptions.IgnorePatternWhitespace);

        foreach (Match m in monsterRegex.Matches(normalized))
        {
            var val = m.Groups["val"].Value;
            AddRef(EntityKind.Mob, val, "Monster",
                ExtractSnippet(rawContent, m.Index),
                script.FilePath,
                GetLine(lineIndex, m.Index),
                fileName);
        }

        // AREAMONSTER (space or paren): map,x1,y1,x2,y2,name,mob_id,...
        var areaMonsterRegex = new Regex(@"
\bareamonster\b
\s*
(?:\(\s*|\s+)
(?:(?:\s*""[^""]*""|[^,;()]+)\s*,){6}
\s*(?<val>-?\d+|""[^""]+""|[A-Za-z_][A-Za-z0-9_]*)
", Opt | RegexOptions.IgnorePatternWhitespace);

        foreach (Match m in areaMonsterRegex.Matches(normalized))
        {
            var val = m.Groups["val"].Value;
            AddRef(EntityKind.Mob, val, "Monster",
                ExtractSnippet(rawContent, m.Index),
                script.FilePath,
                GetLine(lineIndex, m.Index),
                fileName);
        }
    }

    private void AddRef(EntityKind kind, string value, string refKind, string snippet, string file, int line, string fileName)
    {
        int? id = null;
        string? name = null;

        if (int.TryParse(value, out var parsedId))
        {
            id = parsedId;
        }
        else
        {
            name = value.Trim('"');
        }

        var from = new SymbolKey(EntityKind.Npc, null, fileName);
        var to = new SymbolKey(kind, id, name);
        
        _index.References.Add(new ReferenceRecord
        {
            From = from,
            To = to,
            RefKind = refKind,
            Snippet = snippet,
            SourceFilePath = file,
            LineNumber = line
        });
    }

    private string NormalizeScript(string content)
    {
        if (string.IsNullOrEmpty(content)) return "";

        // We want to remove comments but PRESERVE newlines and string length
        // so that FindLineNumber works correctly on the original matching indices.
        // Strategy: Replace non-newline characters in comments with spaces.
        
        var chars = content.ToCharArray();
        bool inString = false;
        bool inLineComment = false;
        bool inBlockComment = false;

        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            char next = (i + 1 < chars.Length) ? chars[i + 1] : '\0';

            // Handle logic
            if (inBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    inBlockComment = false;
                    chars[i] = ' ';
                    chars[i + 1] = ' ';
                    i++; 
                }
                else if (c != '\n' && c != '\r')
                {
                    chars[i] = ' '; // Replace content with space
                }
                continue;
            }

            if (inLineComment)
            {
                if (c == '\n' || c == '\r')
                {
                    inLineComment = false;
                    // Keep newline
                }
                else
                {
                    chars[i] = ' ';
                }
                continue;
            }

            if (inString)
            {
                if (c == '"') inString = false;
                continue;
            }

            // Start logic
            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '/' && next == '*')
            {
                inBlockComment = true;
                chars[i] = ' ';
                chars[i + 1] = ' ';
                i++;
                continue;
            }

            if (c == '/' && next == '/')
            {
                inLineComment = true;
                chars[i] = ' ';
                chars[i + 1] = ' ';
                i++;
                continue;
            }
        }

        return new string(chars);
    }

    private static int[] BuildLineIndex(string s)
    {
        // lineStarts[0] = 0, lineStarts[i] = index of first char of line i+1
        var starts = new List<int>(capacity: 256) { 0 };
        for (int i = 0; i < s.Length; i++)
            if (s[i] == '\n') starts.Add(i + 1);
        return starts.ToArray();
    }

    private static int GetLine(int[] lineStarts, int index)
    {
        // binary search for last start <= index
        int lo = 0, hi = lineStarts.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (lineStarts[mid] <= index) lo = mid + 1;
            else hi = mid - 1;
        }
        return Math.Max(1, hi + 1);
    }

    private static string ExtractSnippet(string raw, int matchIndex)
    {
        // take "statement-ish" slice: from matchIndex to next ';' or max length
        if (matchIndex < 0 || matchIndex >= raw.Length) return "";
        int end = raw.IndexOf(';', matchIndex);
        if (end < 0) end = Math.Min(raw.Length, matchIndex + 200);
        else end = Math.Min(raw.Length, end + 1);
        return raw.Substring(matchIndex, end - matchIndex).Trim();
    }
}
