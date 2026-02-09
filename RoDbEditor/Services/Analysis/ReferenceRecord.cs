namespace RoDbEditor.Services.Analysis;

public class ReferenceRecord
{
    public SymbolKey From { get; set; }
    public SymbolKey To { get; set; }
    public string RefKind { get; set; }
    public string Snippet { get; set; }
    public string SourceFilePath { get; set; }
    public int LineNumber { get; set; }
}
