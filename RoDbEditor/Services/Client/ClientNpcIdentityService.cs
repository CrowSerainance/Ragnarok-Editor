using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RoDbEditor.Services.Client;

public sealed class ClientNpcIdentityService
{
    private readonly Dictionary<string, int> _byName = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, int> Entries => _byName;

    public void Clear() => _byName.Clear();

    public void LoadFromClientSystem(string clientSystemPath)
    {
        Clear();
        if (string.IsNullOrWhiteSpace(clientSystemPath) || !Directory.Exists(clientSystemPath))
            return;

        var path = ClientFileLocator.TryFindSystemFile(clientSystemPath,
            "npcidentity.lub", "npcidentity.lua", "NPCidentity.lub", "NPCidentity.lua");

        if (path == null) return;

        var text = File.ReadAllText(path, Encoding.Default);

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("[\"")) continue;

            var q = line.IndexOf("\"]", StringComparison.Ordinal);
            if (q < 0) continue;

            var name = line.Substring(2, q - 2);
            var eq = line.IndexOf('=', q);
            if (eq < 0) continue;

            var rhs = new string(line.Substring(eq + 1).Trim().TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(rhs, out var id))
                _byName[name] = id;
        }
    }

    public bool TryGet(string spriteName, out int id) => _byName.TryGetValue(spriteName, out id);

    public string AppendToPatchSystem(string clientPatchRoot, string spriteName, int npcId)
    {
        if (string.IsNullOrWhiteSpace(clientPatchRoot)) throw new ArgumentException(nameof(clientPatchRoot));
        if (string.IsNullOrWhiteSpace(spriteName)) throw new ArgumentException(nameof(spriteName));

        var sys = ClientFileLocator.EnsureSystemOut(clientPatchRoot);
        var outPath = Path.Combine(sys, "npcidentity_rodbeditor.lua");

        if (!File.Exists(outPath))
        {
            File.WriteAllText(outPath,
@"-- RoDbEditor: npcidentity overlay
dofile(""System/npcidentity.lub"")
", Encoding.Default);
        }

        var safeName = spriteName.ToUpperInvariant();
        var line = $"[\"{safeName}\"] = {npcId}, -- RoDbEditor\n";

        File.AppendAllText(outPath, line, Encoding.Default);
        _byName[safeName] = npcId;

        return outPath;
    }
}
