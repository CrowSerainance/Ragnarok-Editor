namespace RoDbEditor.Services.Analysis;

public enum DiagnosticSeverity { Error, Warning, Info }

public class DiagnosticRecord
{
    public DiagnosticSeverity Severity { get; set; }
    public string Code { get; set; }
    public string Message { get; set; }
    public string FilePath { get; set; }
    public int LineNumber { get; set; }
    public string? Snippet { get; set; }
}
