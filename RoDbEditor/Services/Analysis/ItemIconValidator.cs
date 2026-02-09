using System.Collections.Generic;
using RoDbEditor.Core;

namespace RoDbEditor.Services.Analysis;

public class ItemIconValidator : IValidator
{
    private readonly GrfService _grf;
    private readonly FileSystemSpriteSource? _files;

    public ItemIconValidator(GrfService grf, FileSystemSpriteSource? files)
    {
        _grf = grf;
        _files = files;
    }

    public IEnumerable<DiagnosticRecord> Validate(WorkspaceIndex index)
    {
        foreach (var kv in index.ById)
        {
            if (kv.Key.Item1 != EntityKind.Item) continue;
            var id = kv.Key.Item2;
            var key = kv.Value;
            var aegisName = key.Name; // SymbolKey.Name stores AegisName for items

            bool found = false;

            // 1. Check Filesystem
            if (_files != null)
            {
               if (_files.FindItemIcon(id, aegisName) != null) found = true;
            }

            // 2. Check GRF if not found
            if (!found && _grf != null)
            {
                 var paths = new[]
                {
                    $"data\\texture\\effect\\{id}.bmp",
                    $"data\\texture\\effect\\{aegisName}.bmp",
                    $"data\\texture\\effect\\item\\{id}.bmp",
                    $"data\\texture\\effect\\collection\\{id}.bmp",
                    $"data\\texture\\effect\\collection\\{aegisName}.bmp",
                    $@"data\texture\유저인터페이스\item\{id}.bmp",
                };
                foreach (var p in paths)
                {
                    if (_grf.Exists(p)) { found = true; break; }
                }
            }

            if (!found)
            {
                yield return new DiagnosticRecord
                {
                    Severity = DiagnosticSeverity.Warning,
                    Code = "ROV004",
                    Message = $"Missing icon for item {id} ({aegisName}).",
                    FilePath = "", // TODO: point to item_db line if possible
                    LineNumber = 0
                };
            }
        }
    }
}
