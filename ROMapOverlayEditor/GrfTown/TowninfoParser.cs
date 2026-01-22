using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ROMapOverlayEditor;

namespace ROMapOverlayEditor.GrfTown
{
    public static class TowninfoParser
    {
        // Regex cannot correctly parse nested Lua tables.
        // Towninfo uses nested structure:
        // mapNPCInfoTable = { town = { { entry }, { entry } }, ... }
        // We MUST parse by brace depth scanning.

        private static int ReadInt(string entry, string key, int def = 0)
        {
            var m = Regex.Match(entry, $@"\b{Regex.Escape(key)}\b\s*=\s*(?<v>-?\d+)", RegexOptions.IgnoreCase);
            return m.Success ? int.Parse(m.Groups["v"].Value) : def;
        }

        private static string ReadString(string entry, string key, string def = "")
        {
            // Supports: key = "text" or key = [=[text]=]
            var m1 = Regex.Match(entry, $@"\b{Regex.Escape(key)}\b\s*=\s*""(?<v>[^""]*)""", RegexOptions.IgnoreCase);
            if (m1.Success) return m1.Groups["v"].Value;

            var m2 = Regex.Match(entry, $@"\b{Regex.Escape(key)}\b\s*=\s*\[=\[(?<v>.*?)\]=\]", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (m2.Success) return m2.Groups["v"].Value;

            return def;
        }

        public static List<TownEntry> ParseTowninfoText(string text, string sourcePath)
        {
            var towns = new List<TownEntry>();
            if (string.IsNullOrWhiteSpace(text))
                return towns;

            // 1) Extract the actual root table body.
            // Prefer: mapNPCInfoTable = { ... }
            // Fallback: first { ... } block in file.
            var rootBody = ExtractAssignedTableBody(text, "mapNPCInfoTable") ?? ExtractFirstTableBody(text);

            if (string.IsNullOrWhiteSpace(rootBody))
                return towns;

            // 2) Enumerate towns: townName = { ... }
            foreach (var (townName, townBody) in EnumerateTopLevelNamedTables(rootBody))
            {
                if (string.IsNullOrWhiteSpace(townName))
                    continue;

                var town = new TownEntry
                {
                    Name = townName,
                    SourcePath = sourcePath
                };

                // 3) Enumerate NPC entry blocks: { ... }, { ... }
                foreach (var entryBody in EnumerateImmediateChildTables(townBody))
                {
                    var name = ReadString(entryBody, "name", ReadString(entryBody, "Name", ""));
                    var x = ReadInt(entryBody, "X", ReadInt(entryBody, "x", 0));
                    var y = ReadInt(entryBody, "Y", ReadInt(entryBody, "y", 0));
                    var type = ReadInt(entryBody, "TYPE", ReadInt(entryBody, "Type", 0));
                    var sprite = ReadString(entryBody, "sprite", ReadString(entryBody, "Sprite", ""));

                    // Preserve your original mapping behavior.
                    if (string.IsNullOrWhiteSpace(sprite) && type >= 0 && type <= 7)
                        sprite = TowninfoImporter.TypeToSprite.GetValueOrDefault((TownNpcType)type, "4_M_MERCHANT");

                    // Skip fully-empty entries
                    if (string.IsNullOrWhiteSpace(name) && x == 0 && y == 0 && type == 0 && string.IsNullOrWhiteSpace(sprite))
                        continue;

                    town.Npcs.Add(new TownNpc
                    {
                        Name = string.IsNullOrWhiteSpace(name) ? "(Unnamed)" : name,
                        X = x,
                        Y = y,
                        Type = type,
                        Sprite = sprite ?? ""
                    });
                }

                if (town.Npcs.Count > 0)
                    towns.Add(town);
            }

            towns.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return towns;
        }

        // =========================
        // Parsing Helpers (Brace Scan)
        // =========================

        private static string? ExtractAssignedTableBody(string text, string varName)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(varName))
                return null;

            var idx = text.IndexOf(varName, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            var eq = text.IndexOf('=', idx);
            if (eq < 0) return null;

            var open = text.IndexOf('{', eq);
            if (open < 0) return null;

            var close = FindMatchingBrace(text, open);
            if (close < 0) return null;

            return text.Substring(open + 1, close - open - 1);
        }

        private static string? ExtractFirstTableBody(string text)
        {
            var open = text.IndexOf('{');
            if (open < 0) return null;

            var close = FindMatchingBrace(text, open);
            if (close < 0) return null;

            return text.Substring(open + 1, close - open - 1);
        }

        private static IEnumerable<(string Name, string Body)> EnumerateTopLevelNamedTables(string body)
        {
            int i = 0;

            while (i < body.Length)
            {
                SkipWhitespaceAndSeparators(body, ref i);
                if (i >= body.Length) yield break;

                // Read identifier town name (supports underscores/digits)
                if (!IsIdentStart(body[i]))
                {
                    i++;
                    continue;
                }

                int start = i;
                i++;
                while (i < body.Length && IsIdentChar(body[i])) i++;
                var name = body.Substring(start, i - start).Trim();

                SkipWhitespaceAndSeparators(body, ref i);

                if (i >= body.Length || body[i] != '=')
                    continue;

                i++; // skip '='
                SkipWhitespaceAndSeparators(body, ref i);

                if (i >= body.Length || body[i] != '{')
                    continue;

                int open = i;
                int close = FindMatchingBrace(body, open);
                if (close < 0) yield break;

                var subBody = body.Substring(open + 1, close - open - 1);
                yield return (name, subBody);

                i = close + 1;
            }
        }

        private static IEnumerable<string> EnumerateImmediateChildTables(string townBody)
        {
            int i = 0;

            while (i < townBody.Length)
            {
                SkipWhitespaceAndSeparators(townBody, ref i);
                if (i >= townBody.Length) yield break;

                if (townBody[i] != '{')
                {
                    i++;
                    continue;
                }

                int open = i;
                int close = FindMatchingBrace(townBody, open);
                if (close < 0) yield break;

                yield return townBody.Substring(open + 1, close - open - 1);
                i = close + 1;
            }
        }

        // Finds matching } for a { at openIndex, respecting:
        // - quoted strings "..." and '...'
        // - Lua long bracket strings [=[ ... ]=]
        private static int FindMatchingBrace(string s, int openIndex)
        {
            int depth = 0;
            bool inDouble = false;
            bool inSingle = false;

            for (int i = openIndex; i < s.Length; i++)
            {
                char c = s[i];

                // Escape handling inside quotes
                if ((inDouble || inSingle) && c == '\\')
                {
                    i++;
                    continue;
                }

                // Enter/exit "..."
                if (!inSingle && c == '"' && !inDouble)
                {
                    inDouble = true;
                    continue;
                }
                if (inDouble && c == '"')
                {
                    inDouble = false;
                    continue;
                }

                // Enter/exit '...'
                if (!inDouble && c == '\'' && !inSingle)
                {
                    inSingle = true;
                    continue;
                }
                if (inSingle && c == '\'')
                {
                    inSingle = false;
                    continue;
                }

                // Skip Lua long bracket strings [=*[ ... ]=*]
                if (!inDouble && !inSingle && c == '[')
                {
                    int skipTo = TrySkipLongBracket(s, i);
                    if (skipTo > i)
                    {
                        i = skipTo;
                        continue;
                    }
                }

                if (inDouble || inSingle)
                    continue;

                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }

        private static int TrySkipLongBracket(string s, int pos)
        {
            // Detect [=*[ pattern
            int i = pos + 1;
            int eqCount = 0;

            while (i < s.Length && s[i] == '=')
            {
                eqCount++;
                i++;
            }

            if (i >= s.Length || s[i] != '[')
                return pos;

            // Find closing ]=*=]
            int close = FindLongBracketClose(s, i + 1, eqCount);
            return close >= 0 ? close : pos;
        }

        private static int FindLongBracketClose(string s, int start, int eqCount)
        {
            for (int i = start; i < s.Length - (eqCount + 2); i++)
            {
                if (s[i] != ']') continue;

                int j = i + 1;
                int k = 0;

                while (k < eqCount && j < s.Length && s[j] == '=')
                {
                    k++;
                    j++;
                }

                if (k == eqCount && j < s.Length && s[j] == ']')
                    return j; // index of final ']'
            }

            return -1;
        }

        private static void SkipWhitespaceAndSeparators(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c) || c == ',' || c == ';')
                {
                    i++;
                    continue;
                }
                break;
            }
        }

        private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_' || char.IsDigit(c);
        private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';
    }
}
