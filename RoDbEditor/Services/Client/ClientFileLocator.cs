using System;
using System.IO;
using System.Linq;

namespace RoDbEditor.Services.Client;

public static class ClientFileLocator
{
    public static string? TryFindSystemFile(string systemRoot, params string[] candidates)
    {
        if (string.IsNullOrWhiteSpace(systemRoot) || !Directory.Exists(systemRoot))
            return null;

        var files = Directory.GetFiles(systemRoot, "*.*", SearchOption.TopDirectoryOnly);
        foreach (var c in candidates)
        {
            var hit = files.FirstOrDefault(f => string.Equals(Path.GetFileName(f), c, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;
        }

        return null;
    }

    public static string EnsureDir(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    public static string EnsureSystemOut(string patchRoot)
    {
        var sys = Path.Combine(patchRoot, "System");
        EnsureDir(sys);
        return sys;
    }

    public static string EnsureDataOut(string patchRoot)
    {
        var data = Path.Combine(patchRoot, "data");
        EnsureDir(data);
        return data;
    }
}
