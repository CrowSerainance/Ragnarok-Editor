using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GRF.Core.GroupedGrf;
using Utilities.Services;

namespace RoDbEditor.Core;

/// <summary>
/// Holds multi-GRF container and provides file lookup for the UI.
/// IMPORTANT:
/// MultiGrfReader.FileTable is a MultiFileTable which is great for resolving a *known* path,
/// but it is NOT reliable for directory enumeration via GetFiles().
/// So we enumerate by walking each loaded GRF container's FileTable + any folder overlays.
/// </summary>
public sealed class GrfService : IDisposable
{
    private readonly List<string> _grfPaths = new();
    private MultiGrfReader? _reader;

    // Simple cache for GetFiles() results
    private readonly object _cacheLock = new();
    private readonly Dictionary<CacheKey, List<string>> _filesCache = new();

    public IReadOnlyList<string> GrfPaths => _grfPaths;
    public MultiGrfReader? Reader => _reader;
    public bool IsLoaded => _reader != null && _grfPaths.Count > 0;

    public GrfService()
    {
        try
        {
            // Force EUC-KR (949) for correct Korean filename reading in GRFs
            var eucKr = Encoding.GetEncoding(949);
            EncodingService.DisplayEncoding = eucKr;
        }
        catch
        {
            // 949 might not be available on all systems, but is standard for RO
        }
    }

    public void Dispose() => Close();

    public void Close()
    {
        _reader?.Close();
        _reader = null;
        _grfPaths.Clear();
        ClearFileCache();
    }

    public void LoadFromConfig(Config.RoDbEditorConfig config)
    {
        _grfPaths.Clear();

        if (config?.GrfPaths != null)
        {
            foreach (var p in config.GrfPaths)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                var full = SafeFullPath(p);

                // Accept BOTH GRF files and folder overlays (data folders, etc.)
                if (File.Exists(full) || Directory.Exists(full))
                    AddUniquePath(full);
            }
        }

        UpdateReader();
    }

    /// <summary>
    /// Adds a GRF path (or a folder overlay path). Returns true if it was newly added.
    /// </summary>
    public bool AddGrfPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var full = SafeFullPath(path);
        if (!File.Exists(full) && !Directory.Exists(full))
            return false;

        if (!AddUniquePath(full))
            return false;

        UpdateReader();
        return true;
    }

    private bool AddUniquePath(string fullPath)
    {
        if (_grfPaths.Any(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase)))
            return false;

        _grfPaths.Add(fullPath);
        return true;
    }

    private void UpdateReader()
    {
        ClearFileCache();

        if (_reader == null)
            _reader = new MultiGrfReader();

        var paths = _grfPaths
            .Select(p => new MultiGrfPath(p))
            .ToList();

        _reader.Update(paths);
    }

    private void ClearFileCache()
    {
        lock (_cacheLock)
            _filesCache.Clear();
    }

    private static string SafeFullPath(string p)
    {
        try { return Path.GetFullPath(p.Trim()); }
        catch { return p.Trim(); }
    }

    private static string NormalizeGrfPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        var p = path.Trim()
            .Replace('/', '\\')
            .TrimStart('\\');

        while (p.Contains("\\\\", StringComparison.Ordinal))
            p = p.Replace("\\\\", "\\", StringComparison.Ordinal);

        return p;
    }

    /// <summary>
    /// Reads a relative path from the multi-GRF set (or an absolute on-disk file if given).
    /// </summary>
    public byte[]? GetData(string relativePathOrDiskPath)
    {
        if (string.IsNullOrWhiteSpace(relativePathOrDiskPath))
            return null;

        // Allow direct disk reads for overlays/debug
        try
        {
            if (Path.IsPathRooted(relativePathOrDiskPath) && File.Exists(relativePathOrDiskPath))
                return File.ReadAllBytes(relativePathOrDiskPath);
        }
        catch { /* ignore */ }

        if (_reader == null) return null;

        var rel = NormalizeGrfPath(relativePathOrDiskPath);
        if (string.IsNullOrEmpty(rel)) return null;

        try
        {
            return _reader.GetData(rel);
        }
        catch
        {
            return null;
        }
    }

    public bool Exists(string relativePath)
    {
        if (_reader?.FileTable == null) return false;
        var rel = NormalizeGrfPath(relativePath);
        if (string.IsNullOrEmpty(rel)) return false;

        try
        {
            return _reader.FileTable[rel] != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Enumerate files under a directory with proper multi-source priority.
    /// NEVER use _reader.FileTable.GetFiles() — MultiFileTable.Files throws.
    /// We iterate each GRF container's FileTable directly.
    /// </summary>
    public IEnumerable<string> GetFiles(
        string directory,
        string? searchPattern = null,
        SearchOption searchOption = SearchOption.AllDirectories)
    {
        if (_reader == null) return Array.Empty<string>();

        var dir = NormalizeGrfPath(directory).TrimEnd('\\');
        var patKey = string.IsNullOrWhiteSpace(searchPattern) ? "*" : searchPattern.Trim();

        var cacheKey = new CacheKey(dir, patKey, searchOption);
        lock (_cacheLock)
        {
            if (_filesCache.TryGetValue(cacheKey, out var cached))
                return cached; // return cached list (already materialized)
        }

        // We want the *effective* view: earlier sources override later ones (same as MultiGrfReader indexer priority).
        // MultiFileTable does this by iterating Paths.Reverse() and overwriting. We'll do the same.
        var effective = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var resources = _reader.Paths; // in priority order (earlier = higher priority)
        foreach (var res in resources.Reverse())
        {
            var src = res.Path;
            if (string.IsNullOrWhiteSpace(src))
                continue;

            // Folder overlay
            if (Directory.Exists(src))
            {
                TryEnumerateFolderOverlay(effective, src, dir, patKey, searchOption);
                continue;
            }

            // GRF container — use per-container FileTable (not MultiFileTable)
            GRF.Core.GrfHolder? grf = null;
            if (_reader.Containers != null)
            {
                _reader.Containers.TryGetValue(src, out grf);
                // Fallback: find container by FileName when key lookup fails (path normalization/casing)
                if (grf == null && File.Exists(src))
                {
                    grf = _reader.Containers.Values.FirstOrDefault(c =>
                        string.Equals(c.FileName, src, StringComparison.OrdinalIgnoreCase));
                }
            }
            if (grf?.FileTable != null)
            {
                TryEnumerateGrf(effective, grf, dir, searchPattern, searchOption);
            }
        }

        var list = effective.Keys.ToList();

        lock (_cacheLock)
            _filesCache[cacheKey] = list;

        return list;
    }

    private static void TryEnumerateGrf(
        Dictionary<string, string> effective,
        GRF.Core.GrfHolder grf,
        string dir,
        string? searchPattern,
        SearchOption searchOption)
    {
        try
        {
            // GRF FileTable supports GetFiles reliably.
            var pat = string.IsNullOrWhiteSpace(searchPattern) ? null : searchPattern;
            var files = grf.FileTable.GetFiles(
                string.IsNullOrEmpty(dir) ? "" : dir,
                pat,
                searchOption,
                true);

            foreach (var f in files)
            {
                var norm = NormalizeGrfPath(f);
                if (!string.IsNullOrEmpty(norm))
                    effective[norm] = norm;
            }
        }
        catch
        {
            // swallow enumeration failures per container
        }
    }

    private static void TryEnumerateFolderOverlay(
        Dictionary<string, string> effective,
        string overlayPath,
        string dir,
        string patKey,
        SearchOption searchOption)
    {
        try
        {
            // Match GRFEditor's overlay logic:
            // if overlayPath points at "...\\data", root is its parent ("...\\")
            var trimmed = overlayPath.TrimEnd('\\', '/');
            var root = Path.GetDirectoryName(trimmed);
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return;

            // Attempt directory as-is; if missing, attempt ANSI conversion (GRFEditor does this in some paths)
            var targetDir = Path.Combine(root, dir);
            if (!Directory.Exists(targetDir))
            {
                var ansiDir = EncodingService.ConvertStringToAnsi(dir);
                targetDir = Path.Combine(root, ansiDir);
                if (!Directory.Exists(targetDir))
                    return;
            }

            var winPattern = (string.IsNullOrWhiteSpace(patKey) || patKey == "*") ? "*" : patKey;

            foreach (var file in Directory.GetFiles(targetDir, winPattern, searchOption))
            {
                // Convert to GRF-like relative path: "root\data\...\file"
                if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    continue;

                var rel = file.Substring(root.Length).TrimStart('\\', '/');
                rel = rel.Replace('/', '\\');

                var norm = NormalizeGrfPath(rel);
                if (!string.IsNullOrEmpty(norm))
                    effective[norm] = norm;
            }
        }
        catch
        {
            // ignore overlay enumeration failures
        }
    }

    public string BuildSanityReport()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"GRF/Overlay sources configured: {_grfPaths.Count}");
        sb.AppendLine($"GRF reader initialized: {(_reader != null)}");
        sb.AppendLine($"Loaded containers: {(_reader?.Containers?.Count ?? 0)}");
        sb.AppendLine();

        // Per-source summary (fast: no enumeration)
        if (_reader?.Containers != null)
        {
            foreach (var p in _grfPaths)
            {
                if (Directory.Exists(p))
                {
                    sb.AppendLine($"[DIR]  {p}");
                }
                else if (_reader.Containers.TryGetValue(p, out var grf) && grf?.FileTable?.Entries != null)
                {
                    sb.AppendLine($"[GRF]  {Path.GetFileName(p)}  | entries: {grf.FileTable.Entries.Count:n0}");
                }
                else
                {
                    sb.AppendLine($"[??]   {p}");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("Sanity probes (Exists):");

        // Common RO client paths (these are cheap checks)
        var probes = new[]
        {
            @"data\luafiles514\lua files\datainfo\iteminfo.lub",
            @"data\luafiles514\lua files\datainfo\iteminfo.lua",
            @"data\luafiles514\lua files\datainfo\mobinfo.lub",
            @"data\luafiles514\lua files\datainfo\mobinfo.lua",
            @"data\texture\유저인터페이스\item",
            @"data\sprite\npc",
            @"data\sprite\몬스터",
            @"data\*.rsw",
        };

        foreach (var probe in probes)
        {
            // If probe includes wildcard, we just check the directory/file existence in a simple way:
            if (probe.Contains('*'))
            {
                var baseDir = Path.GetDirectoryName(probe.Replace('*', 'x')) ?? "data";
                sb.AppendLine($"- {probe}  (hint dir: {baseDir})");
                continue;
            }

            sb.AppendLine($"- {probe} : {(Exists(probe) ? "FOUND" : "missing")}");
        }

        return sb.ToString();
    }

    private readonly record struct CacheKey(string Directory, string Pattern, SearchOption Option);
}