using System.Collections.Generic;
using System.Linq;

namespace RoDbEditor.Services.Analysis;

public enum ValidateStatus
{
    Found,
    NotFound,
    Ambiguous
}

public class ResolveResult
{
    public ValidateStatus Status { get; set; }
    public List<SymbolKey> Matches { get; set; } = new();
}

public class SymbolResolver
{
    private readonly WorkspaceIndex _index;

    public SymbolResolver(WorkspaceIndex index)
    {
        _index = index;
    }

    public ResolveResult Resolve(SymbolKey key)
    {
        var result = new ResolveResult();

        // 1. If ID is present, lookup by ID
        if (key.Id.HasValue)
        {
            if (_index.ById.TryGetValue((key.Kind, key.Id.Value), out var match))
            {
                result.Status = ValidateStatus.Found;
                result.Matches.Add(match);
            }
            else
            {
                result.Status = ValidateStatus.NotFound;
            }
            return result;
        }

        // 2. If Name is present, lookup by Name (AegisName)
        if (!string.IsNullOrEmpty(key.Name))
        {
            if (_index.ByName.TryGetValue((key.Kind, key.Name), out var matches))
            {
                result.Matches.AddRange(matches);
                if (matches.Count == 1)
                    result.Status = ValidateStatus.Found;
                else
                    result.Status = ValidateStatus.Ambiguous; // Multiple matches for one name? (e.g. overrides not fully resolved, or same aegisname reused?)
            }
            else
            {
                result.Status = ValidateStatus.NotFound;
            }
            return result;
        }

        // 3. Neither ID nor Name? Invalid key.
        result.Status = ValidateStatus.NotFound;
        return result;
    }
}
