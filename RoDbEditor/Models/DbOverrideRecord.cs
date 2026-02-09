using RoDbEditor.Services.Analysis;

namespace RoDbEditor.Models;

public record DbOverrideRecord(
    EntityKind Kind,
    int Id,
    string? OldSourceFile,
    string? NewSourceFile,
    string? OldName,
    string? NewName,
    string Reason
);
