using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoDbEditor.Core;

namespace RoDbEditor.Services;

/// <summary>
/// Service to find sprite files (ACT/SPR) by monster or NPC AegisName.
/// </summary>
public class SpriteLookupService
{
    private readonly GrfService _grfService;
    private Dictionary<string, string>? _monsterSpriteCache;
    private Dictionary<string, string>? _npcSpriteCache;

    public SpriteLookupService(GrfService grfService)
    {
        _grfService = grfService;
    }

    /// <summary>
    /// Find monster sprite path by AegisName.
    /// </summary>
    public (string? actPath, string? sprPath) FindMonsterSprite(string aegisName)
    {
        if (string.IsNullOrEmpty(aegisName)) return (null, null);
        
        BuildCacheIfNeeded();
        
        var lower = aegisName.ToLowerInvariant();
        
        // Try exact match
        if (_monsterSpriteCache!.TryGetValue(lower, out var basePath))
        {
            return (basePath + ".act", basePath + ".spr");
        }

        // Try common variations
        var variations = new[]
        {
            lower,
            lower.Replace("_", ""),
            lower.Replace("-", "_"),
        };

        foreach (var variant in variations)
        {
            if (_monsterSpriteCache.TryGetValue(variant, out basePath))
            {
                return (basePath + ".act", basePath + ".spr");
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Find NPC sprite path by sprite name.
    /// </summary>
    public (string? actPath, string? sprPath) FindNpcSprite(string spriteName)
    {
        if (string.IsNullOrEmpty(spriteName)) return (null, null);
        
        BuildCacheIfNeeded();
        
        var lower = spriteName.ToLowerInvariant();
        
        if (_npcSpriteCache!.TryGetValue(lower, out var basePath))
        {
            return (basePath + ".act", basePath + ".spr");
        }

        return (null, null);
    }

    /// <summary>
    /// Get sprite data from GRF.
    /// </summary>
    public (byte[]? actData, byte[]? sprData) GetSpriteData(string? actPath, string? sprPath)
    {
        byte[]? actData = null;
        byte[]? sprData = null;

        if (!string.IsNullOrEmpty(actPath))
            actData = _grfService.GetData(actPath);
        
        if (!string.IsNullOrEmpty(sprPath))
            sprData = _grfService.GetData(sprPath);

        return (actData, sprData);
    }

    private void BuildCacheIfNeeded()
    {
        if (_monsterSpriteCache != null && _npcSpriteCache != null)
            return;

        _monsterSpriteCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _npcSpriteCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (_grfService.Reader?.FileTable == null)
            return;

        try
        {
            // Cache monster sprites (data\sprite\몬스터\)
            var monsterFiles = _grfService.GetFiles(@"data\sprite", "*.spr", SearchOption.AllDirectories);
            foreach (var file in monsterFiles)
            {
                var lower = file.ToLowerInvariant();
                if (lower.Contains("몬스터") || lower.Contains("monster"))
                {
                    var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    var basePath = file.Substring(0, file.Length - 4); // Remove .spr
                    _monsterSpriteCache.TryAdd(name, basePath);
                }
                else if (lower.Contains("npc"))
                {
                    var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    var basePath = file.Substring(0, file.Length - 4);
                    _npcSpriteCache.TryAdd(name, basePath);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to build sprite cache: {ex.Message}");
        }
    }

    public void ClearCache()
    {
        _monsterSpriteCache = null;
        _npcSpriteCache = null;
    }
}
