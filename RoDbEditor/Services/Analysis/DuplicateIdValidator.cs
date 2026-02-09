using System.Collections.Generic;

namespace RoDbEditor.Services.Analysis;

public class DuplicateIdValidator : IValidator
{
    public IEnumerable<DiagnosticRecord> Validate(WorkspaceIndex index)
    {
        foreach (var overrideRecord in index.Overrides)
        {
            var severity = overrideRecord.Reason == "Import Override" ? DiagnosticSeverity.Info : DiagnosticSeverity.Error;
            var code = severity == DiagnosticSeverity.Info ? "ROV001I" : "ROV001";
            
            yield return new DiagnosticRecord
            {
                Severity = severity,
                Code = code,
                Message = $"{overrideRecord.Reason}: {overrideRecord.Kind} ID {overrideRecord.Id} ({overrideRecord.OldName} -> {overrideRecord.NewName}) overrides {overrideRecord.OldSourceFile} with {overrideRecord.NewSourceFile}",
                FilePath = overrideRecord.NewSourceFile ?? "",
                LineNumber = 0 // We don't have line numbers for DB loads yet
            };
        }
    }
}
