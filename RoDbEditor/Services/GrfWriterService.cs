using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GRF.Core;

namespace RoDbEditor.Services;

/// <summary>
/// Adds files to a GRF archive using GrfHolder. Used when assigning sprite assets
/// so files go into the GRF instead of the filesystem.
/// </summary>
public class GrfWriterService
{
    /// <summary>
    /// Result of adding files to a GRF.
    /// </summary>
    public sealed class AddResult
    {
        public bool Success { get; set; }
        public List<string> AddedPaths { get; } = new();
        public List<string> Errors { get; } = new();
    }

    /// <summary>
    /// Adds files to a GRF archive. Opens or creates the GRF, adds each file, then saves.
    /// </summary>
    /// <param name="grfPath">Full path to the .grf file (e.g. client\custom.grf).</param>
    /// <param name="files">Tuples of (grf internal path, source disk path).</param>
    /// <returns>AddResult with success flag, added paths, and any errors.</returns>
    public AddResult AddFilesToGrf(
        string grfPath,
        IEnumerable<(string GrfInternalPath, string SourceDiskPath)> files)
    {
        var result = new AddResult();
        var filesList = files?.ToList() ?? new List<(string, string)>();

        if (string.IsNullOrWhiteSpace(grfPath))
        {
            result.Errors.Add("GRF path is empty.");
            return result;
        }

        var validFiles = filesList
            .Where(f => !string.IsNullOrWhiteSpace(f.GrfInternalPath) && !string.IsNullOrWhiteSpace(f.SourceDiskPath))
            .Where(f => File.Exists(f.SourceDiskPath))
            .ToList();

        if (validFiles.Count == 0)
        {
            result.Errors.Add("No valid source files to add.");
            return result;
        }

        GrfHolder? grf = null;
        try
        {
            grf = new GrfHolder(grfPath, GrfLoadOptions.OpenOrNew);

            foreach (var (grfInternal, sourcePath) in validFiles)
            {
                try
                {
                    grf.Commands.AddFile(NormalizeGrfPath(grfInternal), sourcePath);
                    result.AddedPaths.Add(grfInternal);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"AddFile {grfInternal}: {ex.Message}");
                }
            }

            if (result.Errors.Count == 0 || result.AddedPaths.Count > 0)
            {
                grf.QuickSave();
                result.Success = result.Errors.Count == 0;
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"GRF open/save: {ex.Message}");
        }
        finally
        {
            grf?.Close();
        }

        return result;
    }

    /// <summary>
    /// Writes text content to a GRF file using a temp file intermediary.
    /// </summary>
    public AddResult AddContentToGrf(string grfPath, string grfInternalPath, string content, Encoding? encoding = null)
    {
        encoding ??= new UTF8Encoding(false);
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, content ?? string.Empty, encoding);
            return AddFilesToGrf(grfPath, new[] { (grfInternalPath, tempFile) });
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    private static string NormalizeGrfPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        return path.Trim().Replace('/', '\\').TrimStart('\\');
    }
}
