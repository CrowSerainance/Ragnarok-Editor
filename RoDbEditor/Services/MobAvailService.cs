using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RoDbEditor.Models;

namespace RoDbEditor.Services;

public sealed class MobAvailService
{
    private readonly Dictionary<string, MobAvailEntry> _byMob = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, MobAvailEntry> Entries => _byMob;

    public void Clear() => _byMob.Clear();

    public void LoadFromDataPath(string dataPath)
    {
        Clear();
        if (string.IsNullOrWhiteSpace(dataPath) || !Directory.Exists(dataPath))
            return;

        var import = Path.Combine(dataPath, "db", "import", "mob_avail.yml");
        if (!File.Exists(import)) return;

        var lines = File.ReadAllLines(import, Encoding.UTF8);

        MobAvailEntry? current = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("#") || line.Length == 0) continue;

            if (line.StartsWith("- Mob:", StringComparison.OrdinalIgnoreCase))
            {
                current = new MobAvailEntry();
                current.Mob = line.Substring(6).Trim();
                continue;
            }

            if (current == null) continue;

            if (line.StartsWith("Sprite:", StringComparison.OrdinalIgnoreCase))
            {
                current.Sprite = line.Substring(7).Trim();
                if (!string.IsNullOrWhiteSpace(current.Mob))
                    _byMob[current.Mob] = current;
            }
            else if (line.StartsWith("Sex:", StringComparison.OrdinalIgnoreCase))
                current.Sex = line.Substring(4).Trim();
            else if (line.StartsWith("HairStyle:", StringComparison.OrdinalIgnoreCase) && int.TryParse(line.Substring(10).Trim(), out var hs))
                current.HairStyle = hs;
            else if (line.StartsWith("HairColor:", StringComparison.OrdinalIgnoreCase) && int.TryParse(line.Substring(10).Trim(), out var hc))
                current.HairColor = hc;
            else if (line.StartsWith("ClothColor:", StringComparison.OrdinalIgnoreCase) && int.TryParse(line.Substring(11).Trim(), out var cc))
                current.ClothColor = cc;
        }
    }

    public MobAvailEntry? Get(string mobAegis)
        => _byMob.TryGetValue(mobAegis, out var e) ? e : null;

    public string AppendOrReplaceImportEntry(string dataPath, MobAvailEntry entry)
    {
        if (string.IsNullOrWhiteSpace(dataPath)) throw new ArgumentException(nameof(dataPath));
        if (string.IsNullOrWhiteSpace(entry.Mob)) throw new ArgumentException("entry.Mob empty");
        if (string.IsNullOrWhiteSpace(entry.Sprite)) throw new ArgumentException("entry.Sprite empty");

        entry.Mob = entry.Mob.ToUpperInvariant();
        entry.Sprite = entry.Sprite.ToUpperInvariant();

        var importDir = Path.Combine(dataPath, "db", "import");
        Directory.CreateDirectory(importDir);
        var importPath = Path.Combine(importDir, "mob_avail.yml");

        if (!File.Exists(importPath))
        {
            File.WriteAllText(importPath,
@"Header:
  Type: MOB_AVAIL_DB
  Version: 1

Body:
", Encoding.UTF8);
        }

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"  # RoDbEditor: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"  - Mob: {entry.Mob}");
        sb.AppendLine($"    Sprite: {entry.Sprite}");
        if (!string.IsNullOrWhiteSpace(entry.Sex)) sb.AppendLine($"    Sex: {entry.Sex}");
        if (entry.HairStyle.HasValue) sb.AppendLine($"    HairStyle: {entry.HairStyle.Value}");
        if (entry.HairColor.HasValue) sb.AppendLine($"    HairColor: {entry.HairColor.Value}");
        if (entry.ClothColor.HasValue) sb.AppendLine($"    ClothColor: {entry.ClothColor.Value}");

        File.AppendAllText(importPath, sb.ToString(), Encoding.UTF8);

        _byMob[entry.Mob] = entry;

        return importPath;
    }
}
