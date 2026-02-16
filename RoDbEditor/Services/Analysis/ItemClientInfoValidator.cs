using System.Collections.Generic;
using RoDbEditor;
using RoDbEditor.Core;
using RoDbEditor.Models;

namespace RoDbEditor.Services.Analysis;

/// <summary>
/// For custom items (Id >= 50000): requires ClientItemInfoService entry and icon by resource name.
/// </summary>
public class ItemClientInfoValidator : IValidator
{
    private readonly GrfService _grf;
    private readonly FileSystemSpriteSource? _files;

    public ItemClientInfoValidator(GrfService grf, FileSystemSpriteSource? files)
    {
        _grf = grf;
        _files = files;
    }

    public IEnumerable<DiagnosticRecord> Validate(WorkspaceIndex index)
    {
        var itemDb = App.ItemDbService;
        var clientInfo = App.ClientItemInfoService;
        if (itemDb == null) yield break;

        foreach (var kv in index.ById)
        {
            if (kv.Key.Item1 != EntityKind.Item || kv.Key.Item2 < 50000) continue;
            var id = kv.Key.Item2;

            var item = itemDb.GetById(id);
            if (item == null) continue;

            var aegisName = item.AegisName ?? "";

            if (clientInfo == null || !clientInfo.TryGet(id, out var entry) || entry == null)
            {
                yield return new DiagnosticRecord
                {
                    Severity = DiagnosticSeverity.Error,
                    Code = "ROV_CLIENT_ITEM",
                    Message = $"Custom item {id} ({aegisName}) has no ClientItemInfo entry. Load client System or add via Write itemInfo_rodbeditor.lua.",
                    FilePath = "",
                    LineNumber = 0
                };
                continue;
            }

            var resourceName = entry.IdentifiedResourceName ?? entry.UnidentifiedResourceName ?? aegisName;
            if (string.IsNullOrWhiteSpace(resourceName)) resourceName = aegisName;

            bool iconFound = false;
            if (_files != null && _files.FindItemIcon(id, resourceName) != null)
                iconFound = true;
            if (!iconFound && _grf != null)
            {
                var paths = new[]
                {
                    $"data\\texture\\effect\\{resourceName}.bmp",
                    $"data\\texture\\effect\\{id}.bmp",
                    $@"data\texture\유저인터페이스\item\{resourceName}.bmp",
                    $@"data\texture\유저인터페이스\item\{id}.bmp",
                };
                foreach (var p in paths)
                {
                    if (_grf.Exists(p)) { iconFound = true; break; }
                }
            }

            if (!iconFound)
            {
                yield return new DiagnosticRecord
                {
                    Severity = DiagnosticSeverity.Error,
                    Code = "ROV_CLIENT_ICON",
                    Message = $"Custom item {id} ({aegisName}): icon not found for resource name '{resourceName}'.",
                    FilePath = "",
                    LineNumber = 0
                };
            }

            if (!string.IsNullOrEmpty(resourceName) && !string.Equals(resourceName, aegisName, System.StringComparison.OrdinalIgnoreCase))
            {
                yield return new DiagnosticRecord
                {
                    Severity = DiagnosticSeverity.Info,
                    Code = "ROV_CLIENT_RESOURCE",
                    Message = $"Item {id}: client resource name '{resourceName}' differs from AegisName '{aegisName}'.",
                    FilePath = "",
                    LineNumber = 0
                };
            }
        }
    }
}
