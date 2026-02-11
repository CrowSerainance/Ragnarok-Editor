using System.Collections.Generic;

namespace RoDbEditor.Services.Analysis;

public class BrokenReferenceValidator : IValidator
{
    public IEnumerable<DiagnosticRecord> Validate(WorkspaceIndex index)
    {
        var resolver = new SymbolResolver(index);

        foreach (var reference in index.References)
        {
            // Ignore empty/sentinel IDs (0 or negative) to avoid false positives.
            if (reference.To.Id.HasValue && reference.To.Id.Value <= 0)
                continue;

            var result = resolver.Resolve(reference.To);
            
            if (result.Status == ValidateStatus.NotFound)
            {
                yield return new DiagnosticRecord
                {
                    Severity = DiagnosticSeverity.Error,
                    Code = "ROV002",
                    Message = $"Broken reference: {reference.RefKind} '{reference.To.Id?.ToString() ?? reference.To.Name}' not found.",
                    FilePath = reference.SourceFilePath,
                    LineNumber = reference.LineNumber
                };
            }
            else if (result.Status == ValidateStatus.Ambiguous)
            {
                yield return new DiagnosticRecord
                {
                    Severity = DiagnosticSeverity.Warning,
                    Code = "ROV003",
                    Message = $"Ambiguous reference: {reference.RefKind} '{reference.To.Name}' matches {result.Matches.Count} targets.",
                    FilePath = reference.SourceFilePath,
                    LineNumber = reference.LineNumber
                };
            }
        }
    }
}
