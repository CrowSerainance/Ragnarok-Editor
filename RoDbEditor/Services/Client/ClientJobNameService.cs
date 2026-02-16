using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RoDbEditor.Services.Client;

public sealed class ClientJobNameService
{
    private readonly Dictionary<string, string> _byKey = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Entries => _byKey;

    public void Clear() => _byKey.Clear();

    public void LoadFromClientSystem(string clientSystemPath)
    {
        Clear();
        if (string.IsNullOrWhiteSpace(clientSystemPath) || !Directory.Exists(clientSystemPath))
            return;

        var path = ClientFileLocator.TryFindSystemFile(clientSystemPath,
            "jobname.lub", "jobname.lua", "Jobname.lub", "Jobname.lua");

        if (path == null) return;

        var text = File.ReadAllText(path, Encoding.Default);

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("[jobtbl.", StringComparison.OrdinalIgnoreCase)) continue;

            var close = line.IndexOf(']', StringComparison.Ordinal);
            if (close < 0) continue;

            var key = line.Substring(1, close - 1);
            var q1 = line.IndexOf('"');
            var q2 = line.LastIndexOf('"');
            if (q1 < 0 || q2 <= q1) continue;

            var val = line.Substring(q1 + 1, q2 - q1 - 1);
            _byKey[key] = val;
        }
    }

    public string AppendToPatchSystem(string clientPatchRoot, string spriteName)
    {
        if (string.IsNullOrWhiteSpace(clientPatchRoot)) throw new ArgumentException(nameof(clientPatchRoot));
        if (string.IsNullOrWhiteSpace(spriteName)) throw new ArgumentException(nameof(spriteName));

        var sys = ClientFileLocator.EnsureSystemOut(clientPatchRoot);
        var outPath = Path.Combine(sys, "jobname_rodbeditor.lua");

        if (!File.Exists(outPath))
        {
            File.WriteAllText(outPath,
@"-- RoDbEditor: jobname overlay
dofile(""System/jobname.lub"")
", Encoding.Default);
        }

        var safe = spriteName.ToUpperInvariant();
        var line = $"[jobtbl.{safe}] = \"{safe}\", -- RoDbEditor\n";

        File.AppendAllText(outPath, line, Encoding.Default);
        _byKey["jobtbl." + safe] = safe;

        return outPath;
    }
}
