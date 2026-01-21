using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ROMapOverlayEditor;

namespace ROMapOverlayEditor.GrfTown
{
    public static class TowninfoParser
    {
        // Very tolerant parsing: looks for "townname = { ... }"
        private static readonly Regex TownBlock =
            new(@"(?<town>[A-Za-z0-9_]+)\s*=\s*\{(?<body>.*?)\}\s*,?",
                RegexOptions.Singleline | RegexOptions.Compiled);

        // Parse NPC entries in the town block: { ... }
        private static readonly Regex EntryBlock =
            new(@"\{(?<entry>.*?)\}\s*,?",
                RegexOptions.Singleline | RegexOptions.Compiled);

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
            if (string.IsNullOrWhiteSpace(text)) return towns;

            foreach (Match tm in TownBlock.Matches(text))
            {
                var townName = tm.Groups["town"].Value.Trim();
                var body = tm.Groups["body"].Value;

                var town = new TownEntry
                {
                    Name = townName,
                    SourcePath = sourcePath
                };

                foreach (Match em in EntryBlock.Matches(body))
                {
                    var entry = em.Groups["entry"].Value;

                    // Common field names in Towninfo-like tables
                    var name = ReadString(entry, "name", ReadString(entry, "Name", ""));
                    var x = ReadInt(entry, "X", ReadInt(entry, "x", 0));
                    var y = ReadInt(entry, "Y", ReadInt(entry, "y", 0));
                    var type = ReadInt(entry, "TYPE", ReadInt(entry, "Type", 0));
                    var sprite = ReadString(entry, "sprite", ReadString(entry, "Sprite", ""));
                    if (string.IsNullOrWhiteSpace(sprite) && type >= 0 && type <= 7)
                        sprite = TowninfoImporter.TypeToSprite.GetValueOrDefault((TownNpcType)type, "4_M_MERCHANT");

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

                // Only add town if it has data
                if (town.Npcs.Count > 0)
                    towns.Add(town);
            }

            // Sort by name for dropdown UX
            towns.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return towns;
        }
    }
}
