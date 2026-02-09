using System.Collections.Generic;

namespace RoDbEditor.Services.Analysis;

public interface IValidator
{
    IEnumerable<DiagnosticRecord> Validate(WorkspaceIndex index);
}
