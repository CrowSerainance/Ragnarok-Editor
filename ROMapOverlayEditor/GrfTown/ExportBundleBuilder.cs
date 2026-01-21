using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ROMapOverlayEditor;

namespace ROMapOverlayEditor.GrfTown
{
    public static class ExportBundleBuilder
    {
        public static string BuildMarkdownBundle(
            TownEntry town,
            string grfFilePath,
            string towninfoPath,
            IReadOnlyList<NpcPlacable>? currentNpcs = null,
            IReadOnlyList<TownNpc>? originalNpcs = null)
        {
            var sb = new StringBuilder();
            var useCurrent = currentNpcs != null && currentNpcs.Count > 0;
            var list = useCurrent ? CurrentToView(currentNpcs!) : town.Npcs;

            // ─── Header ───
            sb.AppendLine("==================================================================================");
            sb.AppendLine($"TOWN: {town.Name}");
            sb.AppendLine($"SOURCE GRF: {grfFilePath}");
            sb.AppendLine($"SOURCE TOWNINFO: {towninfoPath}");
            sb.AppendLine($"EXPORTED: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("==================================================================================");
            sb.AppendLine();

            // ─── NPC table ───
            sb.AppendLine($"## NPC LIST ({list.Count} entries)");
            sb.AppendLine();
            sb.AppendLine("| # | Name | X | Y | Type | Sprite |");
            sb.AppendLine("|---|------|---|---|------|--------|");

            int i = 1;
            foreach (var n in list)
            {
                int t = n.Type;
                string typeStr = t >= 0 && t <= 7 && TowninfoImporter.TypeToLabel.TryGetValue((TownNpcType)t, out var L) ? $"{t} ({L})" : t.ToString();
                sb.AppendLine($"| {i} | {EscapePipe(n.Name)} | {n.X} | {n.Y} | {typeStr} | {EscapePipe(n.Sprite)} |");
                i++;
            }

            sb.AppendLine();
            sb.AppendLine("==================================================================================");
            sb.AppendLine("## RATHENA SCRIPT FORMAT (copy to npc/custom/)");
            sb.AppendLine("==================================================================================");
            sb.AppendLine();

            if (useCurrent)
            {
                foreach (var npc in currentNpcs!.Take(500))
                {
                    sb.AppendLine($"{town.Name},{npc.X},{npc.Y},{npc.Dir}\tscript\t{SanitizeNpcName(npc.ScriptName)}\t{(string.IsNullOrWhiteSpace(npc.Sprite) ? "4_M_01" : npc.Sprite)},{{");
                    foreach (var line in (npc.ScriptBody ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        sb.AppendLine($"    {line.TrimStart()}");
                    sb.AppendLine("}");
                    sb.AppendLine();
                }
            }
            else
            {
                foreach (var n in town.Npcs.Take(500))
                {
                    var sprite = string.IsNullOrWhiteSpace(n.Sprite) ? "4_M_01" : n.Sprite;
                    var body = GenerateDefaultScript(n.Name, n.Type);
                    sb.AppendLine($"{town.Name},{n.X},{n.Y},4\tscript\t{SanitizeNpcName(n.Name)}\t{sprite},{{");
                    foreach (var line in body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        sb.AppendLine($"    {line.TrimStart()}");
                    sb.AppendLine("}");
                    sb.AppendLine();
                }
            }

            sb.AppendLine("==================================================================================");
            sb.AppendLine("## LUA FORMAT (for Towninfo.lub replacement)");
            sb.AppendLine("==================================================================================");
            sb.AppendLine();
            sb.AppendLine($"{town.Name} = {{");

            foreach (var n in list)
                sb.AppendLine($"    {{ name = [=[{n.Name}]=], X = {n.X}, Y = {n.Y}, TYPE = {n.Type} }},");

            sb.AppendLine("},");
            sb.AppendLine();

            // ─── Changelog ───
            sb.AppendLine("==================================================================================");
            sb.AppendLine("## CHANGES FROM ORIGINAL (if any edits were made)");
            sb.AppendLine("==================================================================================");
            sb.AppendLine();

            var curView = useCurrent ? CurrentToView(currentNpcs!) : town.Npcs;
            if (originalNpcs != null && originalNpcs.Count > 0)
            {
                var origSet = new HashSet<(int X, int Y)>(originalNpcs.Select(o => (o.X, o.Y)));
                var curSet = new HashSet<(int X, int Y)>(curView.Select(c => (c.X, c.Y)));
                var origByPos = originalNpcs.ToDictionary(o => (o.X, o.Y), o => o);
                var curByPos = curView.ToDictionary(c => (c.X, c.Y), c => c);
                var curByName = curView.GroupBy(c => c.Name.Trim()).ToDictionary(g => g.Key, g => g.ToList());

                bool any = false;

                foreach (var c in curView)
                {
                    if (!origSet.Contains((c.X, c.Y)))
                    {
                        var atOrig = originalNpcs.FirstOrDefault(o => string.Equals(o.Name?.Trim(), c.Name?.Trim(), StringComparison.OrdinalIgnoreCase));
                        if (atOrig != null && (atOrig.X != c.X || atOrig.Y != c.Y))
                        {
                            sb.AppendLine($"MOVED:");
                            sb.AppendLine($"  ~ {c.Name}: ({atOrig.X}, {atOrig.Y}) → ({c.X}, {c.Y})");
                            sb.AppendLine();
                            any = true;
                        }
                        else
                        {
                            sb.AppendLine($"ADDED:");
                            sb.AppendLine($"  + {c.Name} at {c.X}, {c.Y}");
                            sb.AppendLine();
                            any = true;
                        }
                    }
                }

                foreach (var o in originalNpcs)
                {
                    if (!curSet.Contains((o.X, o.Y)) && !curView.Any(c => string.Equals(c.Name?.Trim(), o.Name?.Trim(), StringComparison.OrdinalIgnoreCase)))
                    {
                        sb.AppendLine($"REMOVED:");
                        sb.AppendLine($"  - {o.Name} at ({o.X}, {o.Y})");
                        sb.AppendLine();
                        any = true;
                    }
                }

                if (!any) sb.AppendLine("(No changes from original)");
            }
            else
                sb.AppendLine("(No original data — import town first to see changes)");

            sb.AppendLine();
            sb.AppendLine("==================================================================================");
            return sb.ToString();
        }

        private static List<TownNpc> CurrentToView(IReadOnlyList<NpcPlacable> currentNpcs)
        {
            return currentNpcs.Select(n => new TownNpc
            {
                Name = n.Label ?? n.ScriptName ?? "?",
                X = n.X,
                Y = n.Y,
                Type = TypeFromSprite(n.Sprite),
                Sprite = string.IsNullOrWhiteSpace(n.Sprite) ? "4_M_01" : n.Sprite
            }).ToList();
        }

        private static int TypeFromSprite(string? sprite)
        {
            if (string.IsNullOrWhiteSpace(sprite)) return 0;
            foreach (var kv in TowninfoImporter.TypeToSprite)
                if (kv.Value.Equals(sprite, StringComparison.OrdinalIgnoreCase)) return (int)kv.Key;
            return 0;
        }

        private static string EscapePipe(string? s) => string.IsNullOrEmpty(s) ? "" : s.Replace("|", "\\|");

        private static string SanitizeNpcName(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "NPC";
            s = s!.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > 60 ? s.Substring(0, 60) : s;
        }

        private static string GenerateDefaultScript(string name, int type)
        {
            var t = (TownNpcType)Math.Clamp(type, 0, 7);
            return t switch
            {
                TownNpcType.ToolDealer => $"mes \"[{name}]\";\nmes \"Welcome! I sell useful items for adventurers.\";\nclose;",
                TownNpcType.WeaponDealer => $"mes \"[{name}]\";\nmes \"Looking for weapons? You've come to the right place!\";\nclose;",
                TownNpcType.ArmorDealer => $"mes \"[{name}]\";\nmes \"I have the finest armor in town.\";\nclose;",
                TownNpcType.Smith => $"mes \"[{name}]\";\nmes \"Need something upgraded or refined?\";\nclose;",
                TownNpcType.Guide => $"mes \"[{name}]\";\nmes \"Welcome to this town! How may I help you?\";\nclose;",
                TownNpcType.Inn => $"mes \"[{name}]\";\nmes \"Would you like to rest here?\";\nclose;",
                TownNpcType.KafraEmployee => $"mes \"[{name}]\";\nmes \"Welcome to Kafra Services!\";\nmes \"How may I assist you today?\";\nclose;",
                TownNpcType.StylingShop => $"mes \"[{name}]\";\nmes \"Want to change your hairstyle?\";\nclose;",
                _ => $"mes \"[{name}]\";\nmes \"Hello!\";\nclose;"
            };
        }
    }
}
