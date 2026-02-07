using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RoDbEditor.Core;
using Utilities.Services;

namespace RoDbEditor.Services;

/// <summary>
/// Service to find sprite files (ACT/SPR) by monster, NPC, or item name.
/// Uses direct path probing with multiple encoding variants to reliably find sprites.
/// </summary>
public class SpriteLookupService
{
    private readonly GrfService _grfService;
    private readonly FileSystemSpriteSource? _fileSystemSource;
    private Dictionary<string, string>? _spriteCache;
    private HashSet<string>? _allSpriteFolders;

    // Monster sprite folder paths to probe (in priority order)
    // Korean folder "몬스터" may appear in different encodings in the GRF
    private static readonly string[] MonsterFolderVariants = new[]
    {
        @"data\sprite\몬스터",           // Korean UTF-8 (display)
        @"data\sprite\¸ó½ºÅÍ",           // Korean CP949 as Latin-1
        @"data\sprite\monster",          // English alternative
        @"data\sprite\mob",              // Another English alternative
        @"data\sprite\npc",              // Some mobs are in NPC folder
    };

    // NPC sprite folder paths
    private static readonly string[] NpcFolderVariants = new[]
    {
        @"data\sprite\npc",
        @"data\sprite\NPC",
        @"data\sprite\Npc",
    };

    public SpriteLookupService(GrfService grfService, FileSystemSpriteSource? fileSystemSource = null)
    {
        _grfService = grfService;
        _fileSystemSource = fileSystemSource;
    }

    public (string? actPath, string? sprPath) FindMonsterSprite(string aegisName)
    {
        if (string.IsNullOrEmpty(aegisName)) return (null, null);

        System.Diagnostics.Debug.WriteLine($"[SpriteLookupService] FindMonsterSprite: {aegisName}");

        // Try extracted filesystem assets first (faster, custom sprites take priority)
        if (_fileSystemSource != null)
        {
            var fsResult = _fileSystemSource.FindMonsterSprite(aegisName);
            if (fsResult.actPath != null || fsResult.sprPath != null)
            {
                System.Diagnostics.Debug.WriteLine($"[SpriteLookupService] Found in filesystem: {fsResult.actPath}");
                return fsResult;
            }
        }

        // Try GRF cache (if we've successfully enumerated)
        BuildCacheIfNeeded();
        var result = FindSpriteInCache(aegisName);
        if (result.actPath != null)
        {
            System.Diagnostics.Debug.WriteLine($"[SpriteLookupService] Found in cache: {result.actPath}");
            return result;
        }

        // Direct probing with multiple encoding variants
        result = ProbeForSprite(aegisName, MonsterFolderVariants);
        if (result.actPath != null)
        {
            System.Diagnostics.Debug.WriteLine($"[SpriteLookupService] Found by probing: {result.actPath}");
            return result;
        }

        System.Diagnostics.Debug.WriteLine($"[SpriteLookupService] Not found: {aegisName}");
        return (null, null);
    }

    public (string? actPath, string? sprPath) FindNpcSprite(string spriteName)
    {
        if (string.IsNullOrEmpty(spriteName)) return (null, null);

        System.Diagnostics.Debug.WriteLine($"[SpriteLookupService] FindNpcSprite: {spriteName}");

        // Try extracted filesystem assets first
        if (_fileSystemSource != null)
        {
            var fsResult = _fileSystemSource.FindNpcSprite(spriteName);
            if (fsResult.actPath != null || fsResult.sprPath != null)
            {
                System.Diagnostics.Debug.WriteLine($"[SpriteLookupService] Found NPC in filesystem: {fsResult.actPath}");
                return fsResult;
            }
        }

        // Try GRF cache
        BuildCacheIfNeeded();
        var result = FindSpriteInCache(spriteName);
        if (result.actPath != null) return result;

        // Direct probing
        result = ProbeForSprite(spriteName, NpcFolderVariants);
        if (result.actPath != null) return result;

        return (null, null);
    }

    /// <summary>
    /// Probe for a sprite by trying multiple folder variants and encoding conversions.
    /// </summary>
    private (string? actPath, string? sprPath) ProbeForSprite(string name, string[] folderVariants)
    {
        // Generate name variants (case, underscores, etc.)
        var nameVariants = GenerateNameVariants(name);

        foreach (var folder in folderVariants)
        {
            foreach (var nameVar in nameVariants)
            {
                // Try the path as-is
                var actPath = $@"{folder}\{nameVar}.act";
                var sprPath = $@"{folder}\{nameVar}.spr";

                var actData = _grfService.GetData(actPath);
                if (actData != null && actData.Length > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[SpriteLookupService] Found (direct): {actPath}");
                    return (actPath, sprPath);
                }

                // Try with EncodingService conversion to Korean
                try
                {
                    var actPathKr = EncodingService.ConvertStringToKorean(actPath);
                    if (actPathKr != actPath)
                    {
                        actData = _grfService.GetData(actPathKr);
                        if (actData != null && actData.Length > 0)
                        {
                            var sprPathKr = EncodingService.ConvertStringToKorean(sprPath);
                            System.Diagnostics.Debug.WriteLine($"[SpriteLookupService] Found (Korean): {actPathKr}");
                            return (actPathKr, sprPathKr);
                        }
                    }
                }
                catch { }

                // Try with ANSI conversion
                try
                {
                    var actPathAnsi = EncodingService.ConvertStringToAnsi(actPath);
                    if (actPathAnsi != actPath)
                    {
                        actData = _grfService.GetData(actPathAnsi);
                        if (actData != null && actData.Length > 0)
                        {
                            var sprPathAnsi = EncodingService.ConvertStringToAnsi(sprPath);
                            System.Diagnostics.Debug.WriteLine($"[SpriteLookupService] Found (ANSI): {actPathAnsi}");
                            return (actPathAnsi, sprPathAnsi);
                        }
                    }
                }
                catch { }
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Generate name variants for fuzzy matching (case, underscores, etc.)
    /// </summary>
    internal static List<string> GenerateNameVariants(string name)
    {
        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            name,
            name.ToUpperInvariant(),
            name.ToLowerInvariant(),
        };

        // Common transformations
        if (name.Contains("_"))
        {
            variants.Add(name.Replace("_", ""));
            // For names like "PORING_" try "poring"
            variants.Add(name.TrimEnd('_'));
            variants.Add(name.TrimEnd('_').ToLowerInvariant());
        }

        if (name.Contains("-"))
        {
            variants.Add(name.Replace("-", "_"));
        }

        // Try without trailing numbers (e.g., "poring2" -> "poring")
        var trimmed = name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        if (trimmed.Length > 0 && trimmed != name)
        {
            variants.Add(trimmed);
            variants.Add(trimmed.ToLowerInvariant());
        }

        return variants.ToList();
    }

    private (string? actPath, string? sprPath) FindSpriteInCache(string name)
    {
        if (_spriteCache == null || _spriteCache.Count == 0)
            return (null, null);

        var lower = name.ToLowerInvariant();

        // Try exact match
        if (_spriteCache.TryGetValue(lower, out var basePath))
            return (basePath + ".act", basePath + ".spr");

        // Try common variations
        var variations = GenerateNameVariants(name);
        foreach (var v in variations)
        {
            if (_spriteCache.TryGetValue(v.ToLowerInvariant(), out basePath))
                return (basePath + ".act", basePath + ".spr");
        }

        // Try partial match (if name contains the key or vice versa)
        foreach (var kvp in _spriteCache)
        {
            if (kvp.Key.Contains(lower) || lower.Contains(kvp.Key))
            {
                return (kvp.Value + ".act", kvp.Value + ".spr");
            }
        }

        return (null, null);
    }

    public (byte[]? actData, byte[]? sprData) GetSpriteData(string? actPath, string? sprPath)
    {
        // If paths are absolute (from filesystem source), read directly
        if (_fileSystemSource != null &&
            !string.IsNullOrEmpty(sprPath) && Path.IsPathRooted(sprPath))
        {
            var (fActData, fSprData) = _fileSystemSource.GetSpriteData(actPath, sprPath);
            System.Diagnostics.Debug.WriteLine($"[SpriteLookupService] GetData (filesystem) ACT: {fActData?.Length ?? 0}, SPR: {fSprData?.Length ?? 0} bytes");
            return (fActData, fSprData);
        }

        // Otherwise, use GRF (existing logic)
        byte[]? actData = null;
        byte[]? sprData = null;

        if (!string.IsNullOrEmpty(actPath))
        {
            actData = _grfService.GetData(actPath);
            System.Diagnostics.Debug.WriteLine($"[SpriteLookupService] GetData ACT '{actPath}': {actData?.Length ?? 0} bytes");
        }

        if (!string.IsNullOrEmpty(sprPath))
        {
            sprData = _grfService.GetData(sprPath);
            System.Diagnostics.Debug.WriteLine($"[SpriteLookupService] GetData SPR '{sprPath}': {sprData?.Length ?? 0} bytes");
        }

        return (actData, sprData);
    }

    private void BuildCacheIfNeeded()
    {
        if (_spriteCache != null)
            return;

        _spriteCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _allSpriteFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!_grfService.IsLoaded || _grfService.Reader == null)
        {
            System.Diagnostics.Debug.WriteLine("[SpriteLookupService] GRF not loaded, skipping cache build");
            return;
        }

        System.Diagnostics.Debug.WriteLine("[SpriteLookupService] Building sprite cache...");

        try
        {
            // Try to directly enumerate from GRF containers (bypassing path prefix issues)
            if (_grfService.Reader.Containers != null)
            {
                foreach (var containerKvp in _grfService.Reader.Containers)
                {
                    var grf = containerKvp.Value;
                    if (grf?.FileTable?.Entries == null) continue;

                    System.Diagnostics.Debug.WriteLine($"[SpriteLookupService] Scanning container: {Path.GetFileName(containerKvp.Key)} ({grf.FileTable.Entries.Count} entries)");

                    foreach (var entry in grf.FileTable.Entries)
                    {
                        var path = entry.RelativePath;
                        if (string.IsNullOrEmpty(path)) continue;

                        // Check if it's a .spr file in a sprite folder
                        if (!path.EndsWith(".spr", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!path.Contains("sprite", StringComparison.OrdinalIgnoreCase)) continue;

                        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                        var basePath = path.Substring(0, path.Length - 4); // remove .spr
                        _spriteCache.TryAdd(name, basePath);

                        // Track folder paths
                        var dir = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(dir))
                            _allSpriteFolders.Add(dir);
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"[SpriteLookupService] Cached {_spriteCache.Count} sprites from {_allSpriteFolders?.Count ?? 0} folders");

            // Log sample sprite names for debugging
            if (_spriteCache.Count > 0)
            {
                foreach (var kvp in _spriteCache.Take(10))
                {
                    System.Diagnostics.Debug.WriteLine($"[SpriteLookupService] Sample: {kvp.Key} -> {kvp.Value}");
                }
            }

            // Log sample folder paths
            if (_allSpriteFolders?.Count > 0)
            {
                foreach (var folder in _allSpriteFolders.Take(5))
                {
                    System.Diagnostics.Debug.WriteLine($"[SpriteLookupService] Folder: {folder}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SpriteLookupService] Failed to build sprite cache: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Get all available sprite folders (for debugging/diagnostics).
    /// </summary>
    public IEnumerable<string> GetAllSpriteFolders()
    {
        BuildCacheIfNeeded();
        return _allSpriteFolders ?? Enumerable.Empty<string>();
    }

    /// <summary>
    /// Get count of cached sprites.
    /// </summary>
    public int CachedSpriteCount
    {
        get
        {
            BuildCacheIfNeeded();
            return (_spriteCache?.Count ?? 0) + (_fileSystemSource?.CachedCount ?? 0);
        }
    }

    public void ClearCache()
    {
        _spriteCache = null;
        _allSpriteFolders = null;
        _fileSystemSource?.ClearCache();
    }
}
