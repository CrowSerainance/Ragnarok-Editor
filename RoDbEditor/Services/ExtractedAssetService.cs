using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RoDbEditor.Models;

namespace RoDbEditor.Services;

/// <summary>
/// Builds extracted asset groups from filesystem assets:
/// group key = folder + base filename, pairing all sibling extensions.
/// </summary>
public class ExtractedAssetService
{
    public sealed class SaveResult
    {
        public int CopiedCount { get; init; }
        public string DestinationRoot { get; init; } = "";
        public string ManifestPath { get; init; } = "";
        public List<string> WrittenFiles { get; init; } = new();
    }

    private readonly Func<string?> _extractedRootProvider;
    private List<ExtractedAssetEntry>? _all;
    private bool _cacheBuilt;

    private static readonly HashSet<string> RelevantExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".spr", ".act", ".pal",
        ".bmp", ".png", ".tga", ".jpg", ".jpeg", ".gif",
        ".str", ".wav", ".txt", ".lua", ".lub",
    };

    public ExtractedAssetService(Func<string?> extractedRootProvider)
    {
        _extractedRootProvider = extractedRootProvider;
    }

    public IReadOnlyList<ExtractedAssetEntry> GetByCategory(string _unusedCategory)
    {
        BuildCacheIfNeeded();
        return _all ?? (IReadOnlyList<ExtractedAssetEntry>)Array.Empty<ExtractedAssetEntry>();
    }

    public IReadOnlyList<ExtractedAssetEntry> Search(string _unusedCategory, string? filter)
    {
        var all = GetByCategory("");
        if (string.IsNullOrWhiteSpace(filter))
            return all;

        return all.Where(e =>
                e.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                e.FolderPath.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                e.ExtensionsSummary.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public int TotalCount
    {
        get
        {
            BuildCacheIfNeeded();
            return _all?.Count ?? 0;
        }
    }

    public void ClearCache()
    {
        _all = null;
        _cacheBuilt = false;
    }

    public IReadOnlyList<string> ResolveFilesForPairing(ExtractedAssetEntry entry, string pairingMode)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));

        var all = (entry.SourcePaths ?? new List<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (all.Count == 0)
            return all;

        var key = (pairingMode ?? "").Trim().ToUpperInvariant();
        if (key.Contains("ACT_SPR_ONLY"))
        {
            return all.Where(p =>
            {
                var ext = Path.GetExtension(p);
                return ext.Equals(".spr", StringComparison.OrdinalIgnoreCase) ||
                       ext.Equals(".act", StringComparison.OrdinalIgnoreCase);
            }).ToList();
        }

        if (key.Contains("PAL_ONLY"))
        {
            return all.Where(p => Path.GetExtension(p).Equals(".pal", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return all;
    }

    public SaveResult SavePairedFilesPreserveLayout(
        ExtractedAssetEntry entry,
        string destinationRoot,
        string pairingMode)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));
        if (string.IsNullOrWhiteSpace(destinationRoot)) throw new ArgumentException("Destination root is required.");

        Directory.CreateDirectory(destinationRoot);

        var copied = 0;
        var written = new List<string>();
        var selectedFiles = ResolveFilesForPairing(entry, pairingMode);

        foreach (var source in selectedFiles)
        {
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                continue;

            var fileName = Path.GetFileName(source);
            var destinationFolder = ResolveDestinationFolder(entry, source, pairingMode);

            var destinationPath = Path.Combine(destinationRoot, destinationFolder, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? destinationRoot);
            File.Copy(source, destinationPath, overwrite: true);
            copied++;
            written.Add(destinationPath);
        }

        var manifestPath = WriteManifest(destinationRoot, entry, pairingMode, written);
        return new SaveResult
        {
            CopiedCount = copied,
            DestinationRoot = destinationRoot,
            ManifestPath = manifestPath,
            WrittenFiles = written
        };
    }

    public (int copiedCount, string destinationFolder) SavePairedFiles(
        ExtractedAssetEntry entry,
        string destinationRoot,
        string _unusedDestinationCategory)
    {
        var result = SavePairedFilesPreserveLayout(entry, destinationRoot, "ALL_RELATED");
        return (result.CopiedCount, result.DestinationRoot);
    }

    private void BuildCacheIfNeeded()
    {
        if (_cacheBuilt) return;
        _cacheBuilt = true;
        _all = new List<ExtractedAssetEntry>();

        var root = _extractedRootProvider();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return;

        var groups = new Dictionary<string, AssetGroup>(StringComparer.OrdinalIgnoreCase);

        foreach (var dataRoot in EnumerateDataRoots(root))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dataRoot, "*.*", SearchOption.AllDirectories);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file);
                if (!RelevantExtensions.Contains(ext))
                    continue;

                var folder = Path.GetDirectoryName(file) ?? "";
                var baseName = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(baseName))
                    continue;

                var key = folder.ToLowerInvariant() + "|" + baseName.ToLowerInvariant();
                if (!groups.TryGetValue(key, out var group))
                {
                    group = new AssetGroup
                    {
                        BaseName = baseName,
                        FolderPath = folder,
                        DataRoot = dataRoot,
                    };
                    groups[key] = group;
                }

                group.Files.Add(file);
            }
        }

        foreach (var group in groups.Values)
        {
            group.Files.Sort(StringComparer.OrdinalIgnoreCase);
            var relFolder = MakeDisplayFolder(group.FolderPath, root);

            var actPath = group.Files.FirstOrDefault(f => f.EndsWith(".act", StringComparison.OrdinalIgnoreCase));
            var sprPath = group.Files.FirstOrDefault(f => f.EndsWith(".spr", StringComparison.OrdinalIgnoreCase));
            var previewPath = ChoosePreviewPath(group.Files, sprPath);
            var extSummary = string.Join(", ",
                group.Files.Select(f => Path.GetExtension(f).ToLowerInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase));

            var suffix = relFolder.Length > 48 ? relFolder[^48..] : relFolder;
            _all.Add(new ExtractedAssetEntry
            {
                BaseName = group.BaseName,
                DisplayName = $"{group.BaseName} [{extSummary}] ({suffix})",
                ActPath = actPath,
                SprPath = sprPath,
                PreviewPath = previewPath,
                FolderPath = relFolder,
                DataRelativeFolder = MakeDataRelativeFolder(group.FolderPath, group.DataRoot),
                VariantName = InferVariantName(group.DataRoot, root),
                SourcePaths = group.Files.ToList(),
                ExtensionsSummary = extSummary,
                SuggestedCategory = GuessCategory(relFolder),
            });
        }

        _all.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> EnumerateDataRoots(string rootPath)
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(Path.Combine(rootPath, "sprite")) ||
            Directory.Exists(Path.Combine(rootPath, "texture")))
        {
            yielded.Add(rootPath);
            yield return rootPath;
        }

        var directServerData = Path.Combine(rootPath, "CUSTOM_CONTENT", "data");
        if (Directory.Exists(directServerData) && yielded.Add(directServerData))
            yield return directServerData;

        foreach (var serverDir in Directory.GetDirectories(rootPath))
        {
            var dataPath = Path.Combine(serverDir, "CUSTOM_CONTENT", "data");
            if (Directory.Exists(dataPath) && yielded.Add(dataPath))
                yield return dataPath;
        }

        IEnumerable<string> dataDirs;
        try
        {
            dataDirs = Directory.EnumerateDirectories(rootPath, "data", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        foreach (var dataDir in dataDirs)
        {
            var parent = Directory.GetParent(dataDir);
            if (!string.Equals(parent?.Name, "CUSTOM_CONTENT", StringComparison.OrdinalIgnoreCase))
                continue;

            if (yielded.Add(dataDir))
                yield return dataDir;
        }
    }

    private static string ChoosePreviewPath(IReadOnlyList<string> files, string? sprPath)
    {
        if (!string.IsNullOrWhiteSpace(sprPath))
            return sprPath;

        foreach (var ext in new[] { ".png", ".bmp", ".tga", ".jpg", ".jpeg", ".gif" })
        {
            var hit = files.FirstOrDefault(f => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(hit))
                return hit;
        }

        return files.FirstOrDefault() ?? "";
    }

    private static string MakeDisplayFolder(string fullFolderPath, string root)
    {
        try
        {
            var rel = Path.GetRelativePath(root, fullFolderPath);
            return rel.Replace(Path.DirectorySeparatorChar, '\\');
        }
        catch
        {
            return fullFolderPath;
        }
    }

    private static string MakeDataRelativeFolder(string fullFolderPath, string dataRoot)
    {
        try
        {
            var rel = Path.GetRelativePath(dataRoot, fullFolderPath);
            rel = rel.Replace(Path.DirectorySeparatorChar, '\\');
            if (string.IsNullOrWhiteSpace(rel) || rel == ".")
                return "data";
            return @"data\" + rel.TrimStart('\\');
        }
        catch
        {
            return InferDataFolderFromPath(fullFolderPath) ?? @"data\custom";
        }
    }

    private static string InferVariantName(string dataRoot, string extractedRoot)
    {
        try
        {
            var rel = Path.GetRelativePath(extractedRoot, dataRoot)
                .Replace(Path.DirectorySeparatorChar, '\\');
            var segments = rel.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 3 &&
                string.Equals(segments[1], "CUSTOM_CONTENT", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(segments[2], "data", StringComparison.OrdinalIgnoreCase))
            {
                return segments[0];
            }
        }
        catch
        {
            // ignored
        }

        return "UNKNOWN";
    }

    private static string? InferDataFolderFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var normalized = path.Replace('/', '\\');
        var marker = "\\data\\";
        var idx = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        var folder = Path.GetDirectoryName(normalized[(idx + 1)..]);
        if (string.IsNullOrWhiteSpace(folder))
            return "data";

        return folder.Replace('/', '\\');
    }

    private static string WriteManifest(
        string destinationRoot,
        ExtractedAssetEntry entry,
        string pairingMode,
        IReadOnlyList<string> writtenFiles)
    {
        var manifestPath = Path.Combine(destinationRoot, "asset-merge-manifest.txt");
        var sb = new StringBuilder();
        sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]");
        sb.AppendLine($"Asset: {entry.BaseName}");
        sb.AppendLine($"Variant: {entry.VariantName}");
        sb.AppendLine($"PairingMode: {pairingMode}");
        sb.AppendLine($"SourceFolder: {entry.FolderPath}");
        sb.AppendLine($"DataFolder: {entry.DataRelativeFolder}");
        foreach (var file in writtenFiles)
            sb.AppendLine("Copied: " + file);
        sb.AppendLine();

        File.AppendAllText(manifestPath, sb.ToString());
        return manifestPath;
    }

    private static string ResolveDestinationFolder(ExtractedAssetEntry entry, string sourcePath, string pairingMode)
    {
        var key = (pairingMode ?? "").Trim().ToUpperInvariant();
        var ext = (Path.GetExtension(sourcePath) ?? "").ToLowerInvariant();

        if (key.Contains("SORT_FILE_TYPE"))
        {
            if (ext == ".act" || ext == ".spr")
                return @"data\sorted\act_spr";
            if (ext == ".pal")
                return @"data\sorted\pal";

            var name = ext.TrimStart('.');
            if (string.IsNullOrWhiteSpace(name))
                name = "unknown";
            return $@"data\sorted\{name}";
        }

        if (key.Contains("ACT_SPR_ONLY"))
            return @"data\sorted\act_spr";

        if (key.Contains("PAL_ONLY"))
            return @"data\sorted\pal";

        var destinationFolder = entry.DataRelativeFolder;
        if (string.IsNullOrWhiteSpace(destinationFolder))
            destinationFolder = InferDataFolderFromPath(sourcePath);

        if (string.IsNullOrWhiteSpace(destinationFolder))
            destinationFolder = @"data\custom";

        return destinationFolder;
    }

    private static string GuessCategory(string path)
    {
        var normalized = path.Replace('/', '\\');
        if (normalized.Contains(@"\sprite\npc\", StringComparison.OrdinalIgnoreCase)) return "NPCs";
        if (normalized.Contains(@"\sprite\monster\", StringComparison.OrdinalIgnoreCase)) return "MONSTERS";
        if (normalized.Contains(@"\sprite\mob\", StringComparison.OrdinalIgnoreCase)) return "MONSTERS";
        if (normalized.Contains(@"\sprite\item\", StringComparison.OrdinalIgnoreCase)) return "ITEMS";
        if (normalized.Contains(@"\texture\effect\", StringComparison.OrdinalIgnoreCase)) return "EFFECTS";
        if (normalized.Contains(@"\texture\userinterface\", StringComparison.OrdinalIgnoreCase)) return "UI";
        return "CUSTOM";
    }

    private sealed class AssetGroup
    {
        public string BaseName { get; init; } = "";
        public string FolderPath { get; init; } = "";
        public string DataRoot { get; init; } = "";
        public List<string> Files { get; } = new();
    }
}
