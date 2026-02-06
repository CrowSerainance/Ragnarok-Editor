using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GRF.Core;
using GRF.Core.GroupedGrf;
using RoDbEditor.Config;

namespace RoDbEditor.Core;

/// <summary>
/// Holds multi-GRF container and provides file lookup for the Asset Browser.
/// </summary>
public class GrfService : IDisposable
{
    private MultiGrfReader? _reader;
    private readonly List<string> _grfPaths = new();
    private bool _disposed;

    public MultiGrfReader? Reader => _reader;
    public IReadOnlyList<string> GrfPaths => _grfPaths;

    public bool IsLoaded => _reader != null && _grfPaths.Count > 0;

    public void LoadFromConfig(RoDbEditorConfig config)
    {
        Close();

        var paths = config.GrfPaths.Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p)).ToList();
        if (paths.Count == 0)
            return;

        _reader = new MultiGrfReader();
        var multiPaths = paths.Select(p => new MultiGrfPath(p)).ToList();
        _reader.Update(multiPaths);
        _grfPaths.AddRange(paths);
    }

    public void AddGrfPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!File.Exists(path)) return;

        _reader ??= new MultiGrfReader();

        // Use Update with the full list to properly initialize the GRF
        var newPath = new MultiGrfPath(path);
        var allPaths = _grfPaths.Select(p => new MultiGrfPath(p)).ToList();
        allPaths.Add(newPath);

        _reader.Update(allPaths);

        if (!_grfPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            _grfPaths.Add(path);
    }

    public byte[]? GetData(string relativePath)
    {
        if (_reader == null) return null;
        try
        {
            return _reader.GetData(relativePath);
        }
        catch
        {
            return null;
        }
    }

    public bool Exists(string relativePath)
    {
        try
        {
            return _reader?.Exists(relativePath) ?? false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets files from the GRF matching the directory and optional search pattern.
    /// </summary>
    /// <param name="directory">Directory path inside GRF (e.g., "data\\sprite")</param>
    /// <param name="searchPattern">Search pattern (e.g., "*.spr") - optional</param>
    /// <param name="searchOption">Search option for recursive or top-level only</param>
    /// <returns>List of file paths matching the criteria</returns>
    public IEnumerable<string> GetFiles(string directory, string? searchPattern = null, SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        if (_reader?.FileTable == null)
            return Array.Empty<string>();

        try
        {
            // Use the proper GetFiles signature from the reference implementation
            var files = _reader.FileTable.GetFiles(directory, searchPattern ?? "", searchOption, true);
            return files ?? new List<string>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GrfService.GetFiles error: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Gets all entries in a directory (for browsing).
    /// </summary>
    public IEnumerable<string> FilesInDirectory(string directory)
    {
        if (_reader == null)
            return Array.Empty<string>();

        try
        {
            return _reader.FilesInDirectory(directory);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private void Close()
    {
        try
        {
            _reader?.Close();
        }
        catch { }

        _reader = null;
        _grfPaths.Clear();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            Close();
        }

        _disposed = true;
    }
}
