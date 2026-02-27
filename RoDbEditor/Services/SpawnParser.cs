using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RoDbEditor.Models;

namespace RoDbEditor.Services;

/// <summary>
/// Read-only index of monster spawns from rAthena NPC scripts.
///
/// Responsibilities:
/// - Scan NPC script folders under DataPath (npc, npc/re, npc/pre-re, npc/custom, etc.).
/// - Parse classic tab-based monster spawn lines and script-based monster/boss_monster/areamonster calls.
/// - Expose aggregated SpawnEntry rows per mob for MONSTER DETAILS \"On Maps\" display and diagnostics.
/// - Does not modify any NPC scripts; this is analysis-only.
/// </summary>
public class SpawnParser
{
    private readonly List<SpawnEntry> _spawns = new();
    private string? _dataPath;
    private static readonly Regex ScriptMonsterRegex = new(
        @"\bmonster\s+""(?<map>[^""]+)""\s*,\s*(?<x>\d+)\s*,\s*(?<y>\d+)(?:\s*,\s*\d+\s*,\s*\d+)?\s*,\s*""[^""]*""\s*,\s*(?<mob>\d+)\s*,\s*(?<amount>\d+)(?:\s*,\s*(?<delay>\d+))?(?:\s*,\s*(?<variance>\d+))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ScriptBossMonsterRegex = new(
        @"\bboss_monster\s+""(?<map>[^""]+)""\s*,\s*(?<x>\d+)\s*,\s*(?<y>\d+)(?:\s*,\s*\d+\s*,\s*\d+)?\s*,\s*""[^""]*""\s*,\s*(?<mob>\d+)\s*,\s*(?<amount>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ScriptAreaMonsterRegex = new(
        @"\bareamonster\s+""(?<map>[^""]+)""\s*,\s*(?<x1>\d+)\s*,\s*(?<y1>\d+)\s*,\s*(?<x2>\d+)\s*,\s*(?<y2>\d+)\s*,\s*""[^""]*""\s*,\s*(?<mob>\d+)\s*,\s*(?<amount>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IReadOnlyList<SpawnEntry> Spawns => _spawns;

    public void LoadFromDataPath(string? dataPath)
    {
        _dataPath = dataPath;
        _spawns.Clear();
        if (string.IsNullOrWhiteSpace(dataPath)) return;

        var candidateDirs = new[]
        {
            Path.Combine(dataPath, "npc"),
            Path.Combine(dataPath, "npc", "re"),
            Path.Combine(dataPath, "npc", "pre-re"),
            Path.Combine(dataPath, "npc", "custom"),
            Path.Combine(dataPath, "npc", "mobs"),
            Path.Combine(dataPath, "npc", "re", "mobs"),
            Path.Combine(dataPath, "npc", "re", "instances"),
            Path.Combine(dataPath, "npc", "re", "quests"),
        }
        .Where(Directory.Exists)
        .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in candidateDirs)
            ScanDirectory(dir);
    }

    public IEnumerable<SpawnEntry> GetSpawnsForMob(int mobId)
    {
        return _spawns.Where(s => s.MobId == mobId);
    }

    // rAthena: map_name,x,y[,x2,y2] TAB monster TAB name TAB mob_id,amount[,delay,variance,event]
    private void ScanDirectory(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*.txt", SearchOption.AllDirectories))
        {
            try
            {
                var lines = File.ReadAllLines(file);
                foreach (var line in lines)
                {
                    if (TryParseTabMonsterLine(line, out var tabEntry))
                    {
                        _spawns.Add(tabEntry);
                        continue;
                    }

                    if (TryParseScriptMonsterLine(line, out var scriptEntry))
                    {
                        _spawns.Add(scriptEntry);
                        continue;
                    }

                    if (TryParseScriptBossMonsterLine(line, out var bossEntry))
                    {
                        _spawns.Add(bossEntry);
                        continue;
                    }

                    if (TryParseScriptAreaMonsterLine(line, out var areaEntry))
                    {
                        _spawns.Add(areaEntry);
                        continue;
                    }

                    if (TryParseScriptMonsterLine(line, out scriptEntry))
                        _spawns.Add(scriptEntry);
                }
            }
            catch { }
        }
    }

    private static bool TryParseTabMonsterLine(string line, out SpawnEntry entry)
    {
        entry = new SpawnEntry();
        var parts = line.Split('\t');
        if (parts.Length < 4) return false;
        if (!string.Equals(parts[1].Trim(), "monster", StringComparison.OrdinalIgnoreCase)) return false;

        var mapPart = parts[0].Trim();
        var mobIdPart = parts[3].Trim();
        var mapCoords = mapPart.Split(',');
        if (mapCoords.Length < 3) return false;
        if (!int.TryParse(mapCoords[1].Trim(), out var x) || !int.TryParse(mapCoords[2].Trim(), out var y)) return false;

        var mobIdAmount = mobIdPart.Split(',');
        if (mobIdAmount.Length < 1) return false;
        if (!int.TryParse(mobIdAmount[0].Trim(), out var mobId)) return false;

        var amount = mobIdAmount.Length >= 2 && int.TryParse(mobIdAmount[1].Trim(), out var a) ? a : 1;
        int? delayMs = mobIdAmount.Length >= 3 && int.TryParse(mobIdAmount[2].Trim(), out var d) ? d : null;
        int? varianceMs = mobIdAmount.Length >= 4 && int.TryParse(mobIdAmount[3].Trim(), out var v) ? v : null;

        entry = new SpawnEntry
        {
            Map = mapCoords[0].Trim(),
            X = x,
            Y = y,
            MobId = mobId,
            Amount = amount,
            DelayMs = delayMs,
            VarianceMs = varianceMs
        };
        return true;
    }

    private static bool TryParseScriptMonsterLine(string line, out SpawnEntry entry)
    {
        entry = new SpawnEntry();
        var match = ScriptMonsterRegex.Match(line);
        if (!match.Success) return false;

        if (!int.TryParse(match.Groups["x"].Value, out var x)) return false;
        if (!int.TryParse(match.Groups["y"].Value, out var y)) return false;
        if (!int.TryParse(match.Groups["mob"].Value, out var mobId)) return false;
        if (!int.TryParse(match.Groups["amount"].Value, out var amount)) amount = 1;

        int? delayMs = null;
        int? varianceMs = null;
        if (int.TryParse(match.Groups["delay"].Value, out var d)) delayMs = d;
        if (int.TryParse(match.Groups["variance"].Value, out var v)) varianceMs = v;

        entry = new SpawnEntry
        {
            Map = match.Groups["map"].Value.Trim(),
            X = x,
            Y = y,
            MobId = mobId,
            Amount = amount,
            DelayMs = delayMs,
            VarianceMs = varianceMs
        };
        return true;
    }

    private static bool TryParseScriptBossMonsterLine(string line, out SpawnEntry entry)
    {
        entry = new SpawnEntry();
        var match = ScriptBossMonsterRegex.Match(line);
        if (!match.Success) return false;

        if (!int.TryParse(match.Groups["x"].Value, out var x)) return false;
        if (!int.TryParse(match.Groups["y"].Value, out var y)) return false;
        if (!int.TryParse(match.Groups["mob"].Value, out var mobId)) return false;
        if (!int.TryParse(match.Groups["amount"].Value, out var amount)) amount = 1;

        entry = new SpawnEntry
        {
            Map = match.Groups["map"].Value.Trim(),
            X = x,
            Y = y,
            MobId = mobId,
            Amount = amount
        };
        return true;
    }

    private static bool TryParseScriptAreaMonsterLine(string line, out SpawnEntry entry)
    {
        entry = new SpawnEntry();
        var match = ScriptAreaMonsterRegex.Match(line);
        if (!match.Success) return false;

        if (!int.TryParse(match.Groups["x1"].Value, out var x1)) return false;
        if (!int.TryParse(match.Groups["y1"].Value, out var y1)) return false;
        if (!int.TryParse(match.Groups["x2"].Value, out var x2)) return false;
        if (!int.TryParse(match.Groups["y2"].Value, out var y2)) return false;
        if (!int.TryParse(match.Groups["mob"].Value, out var mobId)) return false;
        if (!int.TryParse(match.Groups["amount"].Value, out var amount)) amount = 1;

        // Use rectangle center as representative spawn location.
        var cx = (x1 + x2) / 2;
        var cy = (y1 + y2) / 2;

        entry = new SpawnEntry
        {
            Map = match.Groups["map"].Value.Trim(),
            X = cx,
            Y = cy,
            MobId = mobId,
            Amount = amount
        };
        return true;
    }
}
