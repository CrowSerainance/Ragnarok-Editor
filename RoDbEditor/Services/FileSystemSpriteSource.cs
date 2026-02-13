using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RoDbEditor.Services;

/// <summary>
/// Scans pre-extracted RO asset folders for sprite files (.spr/.act pairs).
/// Supports multiple server variant subfolders under a root path.
/// Expected structure: {root}/{ServerName}/CUSTOM_CONTENT/data/sprite/{category}/{sprite}.spr
/// </summary>
public class FileSystemSpriteSource
{
    private readonly string _rootPath;
    private Dictionary<string, string>? _spriteCache; // lowercase name -> full base path (no extension)
    private Dictionary<string, string>? _textureCache; // lowercase name -> full path (with extension)

    public FileSystemSpriteSource(string rootPath)
    {
        _rootPath = rootPath;
    }

    public string RootPath => _rootPath;

    public int CachedCount
    {
        get
        {
            BuildCacheIfNeeded();
            return _spriteCache?.Count ?? 0;
        }
    }

    public void BuildCache()
    {
        _spriteCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _textureCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(_rootPath))
            return;

        System.Diagnostics.Debug.WriteLine($"[FileSystemSpriteSource] Building cache from: {_rootPath}");

        foreach (var dataPath in EnumerateDataRoots(_rootPath))
        {
            // Index sprites
            var spritePath = Path.Combine(dataPath, "sprite");
            if (Directory.Exists(spritePath))
            {
                try
                {
                    foreach (var sprFile in Directory.EnumerateFiles(spritePath, "*.spr", SearchOption.AllDirectories))
                    {
                        var name = Path.GetFileNameWithoutExtension(sprFile);
                        var basePath = sprFile[..^4]; // strip .spr extension
                        _spriteCache.TryAdd(name, basePath);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FileSystemSpriteSource] Error scanning sprites: {ex.Message}");
                }
            }

            // Index textures (item icons, collection images, card BMPs, UI elements)
            var texturePath = Path.Combine(dataPath, "texture");
            if (Directory.Exists(texturePath))
            {
                try
                {
                    foreach (var bmpFile in Directory.EnumerateFiles(texturePath, "*.bmp", SearchOption.AllDirectories))
                    {
                        var name = Path.GetFileNameWithoutExtension(bmpFile);
                        _textureCache.TryAdd(name, bmpFile);
                    }
                    // Also index PNG and TGA
                    foreach (var pngFile in Directory.EnumerateFiles(texturePath, "*.png", SearchOption.AllDirectories))
                    {
                        var name = Path.GetFileNameWithoutExtension(pngFile);
                        _textureCache.TryAdd(name, pngFile);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FileSystemSpriteSource] Error scanning textures: {ex.Message}");
                }
            }
        }

        System.Diagnostics.Debug.WriteLine($"[FileSystemSpriteSource] Cached {_spriteCache.Count} sprites, {_textureCache.Count} textures");
    }

    private static IEnumerable<string> EnumerateDataRoots(string rootPath)
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Case 1: root itself points to a data folder.
        if (Directory.Exists(Path.Combine(rootPath, "sprite")) ||
            Directory.Exists(Path.Combine(rootPath, "texture")))
        {
            yielded.Add(rootPath);
            yield return rootPath;
        }

        // Case 2: root is a server folder containing CUSTOM_CONTENT\data.
        var directServerData = Path.Combine(rootPath, "CUSTOM_CONTENT", "data");
        if (Directory.Exists(directServerData) && yielded.Add(directServerData))
        {
            yield return directServerData;
        }

        // Case 3: root contains multiple server variants:
        // {root}\{server}\CUSTOM_CONTENT\data
        foreach (var serverDir in Directory.GetDirectories(rootPath))
        {
            var dataPath = Path.Combine(serverDir, "CUSTOM_CONTENT", "data");
            if (Directory.Exists(dataPath) && yielded.Add(dataPath))
            {
                yield return dataPath;
            }
        }

        // Case 4: generic fallback for deeper nested structures:
        // find any ...\CUSTOM_CONTENT\data subtree under root.
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

    public void ClearCache()
    {
        _spriteCache = null;
        _textureCache = null;
    }

    private void BuildCacheIfNeeded()
    {
        if (_spriteCache == null)
            BuildCache();
    }

    public (string? actPath, string? sprPath) FindMonsterSprite(string aegisName)
    {
        if (string.IsNullOrEmpty(aegisName)) return (null, null);
        BuildCacheIfNeeded();
        return FindInCache(aegisName);
    }

    public (string? actPath, string? sprPath) FindNpcSprite(string spriteName)
    {
        if (string.IsNullOrEmpty(spriteName)) return (null, null);
        BuildCacheIfNeeded();
        return FindInCache(spriteName);
    }

    private (string? actPath, string? sprPath) FindInCache(string name)
    {
        if (_spriteCache == null || _spriteCache.Count == 0)
            return (null, null);

        var variants = SpriteLookupService.GenerateNameVariants(name);
        foreach (var v in variants)
        {
            if (_spriteCache.TryGetValue(v, out var basePath))
            {
                var sprPath = basePath + ".spr";
                var actPath = basePath + ".act";
                if (File.Exists(sprPath))
                    return (File.Exists(actPath) ? actPath : null, sprPath);
            }
        }

        return (null, null);
    }

    public (byte[]? actData, byte[]? sprData) GetSpriteData(string? actPath, string? sprPath)
    {
        byte[]? actData = null;
        byte[]? sprData = null;

        try
        {
            if (!string.IsNullOrEmpty(actPath) && File.Exists(actPath))
                actData = File.ReadAllBytes(actPath);
        }
        catch { }

        try
        {
            if (!string.IsNullOrEmpty(sprPath) && File.Exists(sprPath))
                sprData = File.ReadAllBytes(sprPath);
        }
        catch { }

        return (actData, sprData);
    }

    /// <summary>
    /// Find an item icon texture by item ID or AegisName.
    /// Searches extracted texture folders for BMP/PNG files.
    /// </summary>
    public string? FindItemIcon(int itemId, string? aegisName = null)
    {
        BuildCacheIfNeeded();
        if (_textureCache == null || _textureCache.Count == 0)
            return null;

        // Try by numeric ID first (most common naming convention)
        if (_textureCache.TryGetValue(itemId.ToString(), out var path))
            return path;

        // Try by AegisName
        if (!string.IsNullOrEmpty(aegisName) && _textureCache.TryGetValue(aegisName, out path))
            return path;

        return null;
    }

    /// <summary>
    /// Read a texture file as byte array.
    /// </summary>
    public byte[]? GetTextureData(string filePath)
    {
        try
        {
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                return File.ReadAllBytes(filePath);
        }
        catch { }
        return null;
    }
}
