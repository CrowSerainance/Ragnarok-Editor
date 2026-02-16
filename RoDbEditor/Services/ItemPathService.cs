using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoDbEditor.Core;
using RoDbEditor.Models;

namespace RoDbEditor.Services;

/// <summary>
/// Maps iteminfo entries to related GRF paths (icons, sprites) and back.
/// Routes item names from iteminfo onto related files inside the GRF.
/// </summary>
public class ItemPathService
{
    private readonly ItemDbService _itemDb;
    private readonly GrfService _grfService;
    private readonly SpriteLookupService _spriteLookup;

    public ItemPathService(ItemDbService itemDb, GrfService grfService, SpriteLookupService spriteLookup)
    {
        _itemDb = itemDb ?? throw new ArgumentNullException(nameof(itemDb));
        _grfService = grfService ?? throw new ArgumentNullException(nameof(grfService));
        _spriteLookup = spriteLookup ?? throw new ArgumentNullException(nameof(spriteLookup));
    }

    /// <summary>
    /// Gets GRF paths that exist for this item (icon, sprite) with labels.
    /// </summary>
    public List<(string Path, string Label)> GetRelatedPaths(ItemEntry item)
    {
        var result = new List<(string, string)>();
        if (item == null || !_grfService.IsLoaded) return result;

        var resourceName = GetItemResourceName(item);

        var iconPaths = new[]
        {
            $@"data\texture\effect\{item.Id}.bmp",
            $@"data\texture\effect\{item.AegisName}.bmp",
            $@"data\texture\effect\{resourceName}.bmp",
            $@"data\texture\effect\item\{item.Id}.bmp",
            $@"data\texture\effect\collection\{item.Id}.bmp",
            $@"data\texture\effect\collection\{item.AegisName}.bmp",
            $@"data\texture\effect\collection\{resourceName}.bmp",
        };

        foreach (var p in iconPaths)
        {
            if (_grfService.Exists(p))
                result.Add((p, $"{item.DisplayName} (icon)"));
        }

        try
        {
            var uiPath = $@"data\texture\유저인터페이스\item\{item.Id}.bmp";
            if (_grfService.Exists(uiPath))
                result.Add((uiPath, $"{item.DisplayName} (icon)"));
            var uiPathByName = $@"data\texture\유저인터페이스\item\{resourceName}.bmp";
            if (!string.IsNullOrEmpty(resourceName) && _grfService.Exists(uiPathByName))
                result.Add((uiPathByName, $"{item.DisplayName} (icon)"));
        }
        catch { /* encoding fallback */ }

        var (_, sprPath) = _spriteLookup.FindMonsterSprite(resourceName);
        if (!string.IsNullOrEmpty(sprPath) && _grfService.Exists(sprPath))
        {
            var actPath = Path.ChangeExtension(sprPath, ".act");
            result.Add((sprPath, $"{item.DisplayName} (sprite)"));
            if (_grfService.Exists(actPath))
                result.Add((actPath, $"{item.DisplayName} (animation)"));
        }

        return result;
    }

    /// <summary>
    /// Tries to find an ItemEntry that matches the given GRF path (by filename).
    /// </summary>
    public ItemEntry? TryGetItemForPath(string grfPath)
    {
        if (string.IsNullOrEmpty(grfPath) || _itemDb?.Items == null || _itemDb.Items.Count == 0)
            return null;

        var fileName = Path.GetFileNameWithoutExtension(grfPath);
        if (string.IsNullOrEmpty(fileName)) return null;

        var lower = fileName.ToLowerInvariant();

        foreach (var item in _itemDb.Items)
        {
            if (item.AegisName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                return item;
            if (item.AegisName.ToLowerInvariant().Replace("_", "") == lower.Replace("_", ""))
                return item;
            if (item.Id.ToString() == fileName)
                return item;
        }

        return null;
    }

    /// <summary>
    /// Resolve the client resource name for an item (icon/sprite base name).
    /// Uses ClientItemInfoService when available, else item.ResourceName ?? item.AegisName.
    /// </summary>
    private static string GetItemResourceName(ItemEntry item)
    {
        if (item == null) return "";
        if (RoDbEditor.App.ClientItemInfoService != null &&
            RoDbEditor.App.ClientItemInfoService.TryGet(item.Id, out var entry) && entry != null)
        {
            var r = entry.IdentifiedResourceName ?? entry.UnidentifiedResourceName;
            if (!string.IsNullOrWhiteSpace(r)) return r;
        }
        return string.IsNullOrWhiteSpace(item.ResourceName) ? (item.AegisName ?? "") : item.ResourceName;
    }

    /// <summary>
    /// Returns true if the path is likely item-related (effect icon or item sprite).
    /// </summary>
    public bool IsItemRelatedPath(string grfPath)
    {
        if (string.IsNullOrEmpty(grfPath)) return false;
        var normalized = grfPath.Replace('/', '\\').ToLowerInvariant();
        return normalized.Contains(@"texture\effect") ||
               (normalized.Contains(@"sprite\") && (normalized.EndsWith(".spr") || normalized.EndsWith(".act")));
    }
}
