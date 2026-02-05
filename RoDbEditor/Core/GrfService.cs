using System;
using System.Collections.Generic;
using System.Linq;
using GRF.Core;
using GRF.Core.GroupedGrf;
using RoDbEditor.Config;

namespace RoDbEditor.Core;

/// <summary>
/// Holds multi-GRF container and provides file lookup for the Asset Browser.
/// </summary>
public class GrfService
{
    private MultiGrfReader? _reader;
    private readonly List<string> _grfPaths = new();

    public MultiGrfReader? Reader => _reader;
    public IReadOnlyList<string> GrfPaths => _grfPaths;

    public bool IsLoaded => _reader != null;

    public void LoadFromConfig(RoDbEditorConfig config)
    {
        _reader?.Close();
        _reader = new MultiGrfReader();
        _grfPaths.Clear();

        var paths = config.GrfPaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (paths.Count == 0)
            return;

        var multiPaths = paths.Select(p => new MultiGrfPath(p)).ToList();
        _reader.Update(multiPaths);
        _grfPaths.AddRange(paths);
    }

    public void AddGrfPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _reader ??= new MultiGrfReader();
        _reader.Add(path);
        if (!_grfPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            _grfPaths.Add(path);
    }

    public byte[]? GetData(string relativePath)
    {
        if (_reader == null) return null;
        return _reader.GetData(relativePath);
    }

    public bool Exists(string relativePath)
    {
        return _reader?.Exists(relativePath) ?? false;
    }

    public IEnumerable<string> GetFiles(string directory, string? filter = null)
    {
        if (_reader?.FileTable == null) return Array.Empty<string>();
        try
        {
            var files = _reader.FileTable.GetFiles(directory, filter ?? "", System.IO.SearchOption.TopDirectoryOnly, true);
            return files;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public void Dispose()
    {
        _reader?.Close();
        _reader = null;
        _grfPaths.Clear();
    }
}
