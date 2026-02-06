using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RoDbEditor.Models;

namespace RoDbEditor.Services;

public class SpawnParser
{
    private readonly List<SpawnEntry> _spawns = new();
    private string? _dataPath;

    public IReadOnlyList<SpawnEntry> Spawns => _spawns;

    public void LoadFromDataPath(string? dataPath)
    {
        _dataPath = dataPath;
        _spawns.Clear();
        if (string.IsNullOrWhiteSpace(dataPath)) return;

        var dir = Path.Combine(dataPath, "npc", "re", "mobs");
        if (!Directory.Exists(dir))
        {
            dir = Path.Combine(dataPath, "npc", "mobs");
            if (!Directory.Exists(dir)) return;
        }

        // rAthena: map_name,x,y[,x2,y2] TAB monster TAB name TAB mob_id,amount[,delay,variance,event]
        foreach (var file in Directory.EnumerateFiles(dir, "*.txt", SearchOption.AllDirectories))
        {
            try
            {
                var lines = File.ReadAllLines(file);
                foreach (var line in lines)
                {
                    var parts = line.Split('\t');
                    if (parts.Length < 4) continue;
                    if (!string.Equals(parts[1].Trim(), "monster", StringComparison.OrdinalIgnoreCase)) continue;
                    var mapPart = parts[0].Trim();
                    var mobIdPart = parts[3].Trim(); // mob_id,amount[,delay,...]
                    var mapCoords = mapPart.Split(',');
                    if (mapCoords.Length < 3) continue;
                    if (!int.TryParse(mapCoords[1].Trim(), out var x) || !int.TryParse(mapCoords[2].Trim(), out var y)) continue;
                    var mobIdAmount = mobIdPart.Split(',');
                    if (mobIdAmount.Length < 1) continue;
                    if (!int.TryParse(mobIdAmount[0].Trim(), out var mobId)) continue;
                    _spawns.Add(new SpawnEntry { Map = mapCoords[0].Trim(), X = x, Y = y, MobId = mobId });
                }
            }
            catch { }
        }
    }

    public IEnumerable<SpawnEntry> GetSpawnsForMob(int mobId)
    {
        return _spawns.Where(s => s.MobId == mobId);
    }
}
