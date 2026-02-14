using System;
using System.Collections.Generic;
using System.Linq;

namespace RoDbEditor.Services;

public enum OperationKind { Added, Updated }

public enum OperationEntityKind { Item, Mob }

public class OperationRecord
{
    public DateTime Timestamp { get; set; }
    public OperationKind Kind { get; set; }
    public OperationEntityKind EntityKind { get; set; }
    public int Id { get; set; }
    public string AegisName { get; set; } = "";
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int BodyIndex { get; set; }
    /// <summary>Full file content before save (for Undo of Updated operations).</summary>
    public byte[]? PreviousYamlSnapshot { get; set; }

    public string DisplayAction => Kind == OperationKind.Added ? "Added" : "Updated";
    public string DisplayEntity => EntityKind == OperationEntityKind.Item
        ? $"Item {Id} ({AegisName})"
        : $"Mob {Id} ({AegisName})";
    public string DisplayFile => System.IO.Path.GetFileName(FilePath);
}

/// <summary>
/// Session-only audit log of saves to db/import. Supports Undo and Delete Latest.
/// </summary>
public class OperationsLogService
{
    private readonly List<OperationRecord> _records = new();

    public IReadOnlyList<OperationRecord> Records => _records;

    public void RecordAdded(OperationEntityKind entityKind, int id, string aegisName, string name, string filePath, int bodyIndex)
    {
        _records.Add(new OperationRecord
        {
            Timestamp = DateTime.Now,
            Kind = OperationKind.Added,
            EntityKind = entityKind,
            Id = id,
            AegisName = aegisName ?? "",
            Name = name ?? "",
            FilePath = filePath ?? "",
            BodyIndex = bodyIndex
        });
    }

    public void RecordUpdated(OperationEntityKind entityKind, int id, string aegisName, string name, string filePath, int bodyIndex, byte[]? previousYamlSnapshot)
    {
        _records.Add(new OperationRecord
        {
            Timestamp = DateTime.Now,
            Kind = OperationKind.Updated,
            EntityKind = entityKind,
            Id = id,
            AegisName = aegisName ?? "",
            Name = name ?? "",
            FilePath = filePath ?? "",
            BodyIndex = bodyIndex,
            PreviousYamlSnapshot = previousYamlSnapshot
        });
    }

    /// <summary>Pops and returns the last record for Undo. Returns null if empty.</summary>
    public OperationRecord? TryPopLast()
    {
        if (_records.Count == 0) return null;
        var last = _records[_records.Count - 1];
        _records.RemoveAt(_records.Count - 1);
        return last;
    }

    /// <summary>Gets the last Added operation for item_db (isItemContext=true) or mob_db (false).</summary>
    public OperationRecord? GetLastAddedForContext(bool isItemContext)
    {
        var suffix = isItemContext ? "item_db.yml" : "mob_db.yml";
        return _records
            .Where(r => r.Kind == OperationKind.Added && r.FilePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .LastOrDefault();
    }

    /// <summary>Removes a record by reference (e.g. after Delete selected).</summary>
    public void RemoveRecord(OperationRecord record)
    {
        _records.Remove(record);
    }

    public void Clear()
    {
        _records.Clear();
    }
}
