using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RoDbEditor.Models;

namespace RoDbEditor.Services;

public class SpriteAssignmentService
{
    private readonly GrfWriterService _grfWriter;

    public SpriteAssignmentService(GrfWriterService grfWriter)
    {
        _grfWriter = grfWriter ?? throw new ArgumentNullException(nameof(grfWriter));
    }

    public SpriteAssignmentResult ExecuteAssignment(SpriteAssignmentRequest request)
    {
        var result = new SpriteAssignmentResult();
        if (request == null)
        {
            result.Errors.Add("Assignment request is null.");
            return result;
        }

        if (string.IsNullOrWhiteSpace(request.ClientRootPath) || !Directory.Exists(request.ClientRootPath))
        {
            result.Errors.Add("Client root path is not set or does not exist.");
            return result;
        }

        if (string.IsNullOrWhiteSpace(request.TargetKey))
        {
            result.Errors.Add("Target key is empty.");
            return result;
        }

        if (string.IsNullOrWhiteSpace(request.SourceSprPath) || !File.Exists(request.SourceSprPath))
        {
            result.Errors.Add("Source SPR file was not found.");
            return result;
        }

        var safeBase = MakeSafeFileBase(request.TargetKey);
        var grfPath = !string.IsNullOrWhiteSpace(request.TargetGrfPath)
            ? request.TargetGrfPath
            : Path.Combine(request.ClientRootPath, "custom.grf");

        var filesToAdd = new List<(string GrfInternalPath, string SourceDiskPath)>();

        // Sprite/act files
        if (request.EntityType == SpriteAssignmentEntityType.Item && request.IsHeadgear)
        {
            AddSprActPair(filesToAdd, request, @"data\sprite\아이템", safeBase);
            AddSprActPair(filesToAdd, request, @"data\sprite\악세사리\남", "남_" + safeBase);
            AddSprActPair(filesToAdd, request, @"data\sprite\악세사리\여", "여_" + safeBase);
        }
        else
        {
            var spriteFolder = request.EntityType switch
            {
                SpriteAssignmentEntityType.Npc => @"data\sprite\npc",
                SpriteAssignmentEntityType.Monster => @"data\sprite\몬스터",
                _ => @"data\sprite\아이템"
            };
            AddSprActPair(filesToAdd, request, spriteFolder, safeBase);
        }

        if (!string.IsNullOrWhiteSpace(request.SourceActPath) && !File.Exists(request.SourceActPath) && result.Warnings.Count == 0)
        {
            result.Warnings.Add("ACT file is missing. Installing SPR only.");
        }

        // Related files (.wav, .bmp, .pal)
        foreach (var source in request.RelatedPaths ?? new List<string>())
        {
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                continue;

            var ext = Path.GetExtension(source).ToLowerInvariant();
            string? grfInternalPath = null;
            if (ext == ".wav")
            {
                grfInternalPath = Path.Combine("data", "wav", safeBase + ".wav").Replace('/', '\\');
            }
            else if (ext is ".bmp" or ".png" or ".tga" or ".jpg" or ".jpeg")
            {
                // For Item: add to 유저인터페이스 (User Interface) per RO doc:
                //   - item: inventory icon
                //   - collection: right-click detail view
                // Also add to effect\item for clients that use that path.
                if (request.EntityType == SpriteAssignmentEntityType.Item)
                {
                    var extStr = ext;
                    var baseWithExt = safeBase + extStr;
                    // 유저인터페이스\item = inventory icon
                    filesToAdd.Add((Path.Combine("data", "texture", "유저인터페이스", "item", baseWithExt).Replace('/', '\\'), source));
                    // 유저인터페이스\collection = collection (right-click view)
                    filesToAdd.Add((Path.Combine("data", "texture", "유저인터페이스", "collection", baseWithExt).Replace('/', '\\'), source));
                    // effect\item = some clients use this
                    grfInternalPath = Path.Combine("data", "texture", "effect", "item", baseWithExt).Replace('/', '\\');
                }
                else
                {
                    grfInternalPath = Path.Combine("data", "texture", "effect", safeBase + ext).Replace('/', '\\');
                }
            }
            else if (ext == ".pal")
            {
                grfInternalPath = Path.Combine("data", "texture", "palette", safeBase + ".pal").Replace('/', '\\');
            }

            if (grfInternalPath != null)
                filesToAdd.Add((grfInternalPath, source));
        }

        if (filesToAdd.Count == 0)
        {
            result.Errors.Add("No files to add.");
            return result;
        }

        var addResult = _grfWriter.AddFilesToGrf(grfPath, filesToAdd);

        result.CopiedFiles.AddRange(addResult.AddedPaths);
        result.Errors.AddRange(addResult.Errors);
        result.Success = addResult.Success && result.Errors.Count == 0;

        result.ManifestPath = WriteManifest(request, grfPath, result.CopiedFiles, result.Warnings);
        return result;
    }

    private static void AddSprActPair(
        List<(string GrfInternalPath, string SourceDiskPath)> list,
        SpriteAssignmentRequest request,
        string grfFolder,
        string fileBase)
    {
        var sprPath = Path.Combine(grfFolder, fileBase + ".spr").Replace('/', '\\');
        list.Add((sprPath, request.SourceSprPath));

        if (!string.IsNullOrWhiteSpace(request.SourceActPath) && File.Exists(request.SourceActPath))
        {
            var actPath = Path.Combine(grfFolder, fileBase + ".act").Replace('/', '\\');
            list.Add((actPath, request.SourceActPath));
        }
    }

    private static string WriteManifest(
        SpriteAssignmentRequest request,
        string grfPath,
        IReadOnlyList<string> addedPaths,
        IReadOnlyList<string> warnings)
    {
        var manifestPath = Path.Combine(request.ClientRootPath, "asset-assignment-manifest.txt");
        var sb = new StringBuilder();
        sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]");
        sb.AppendLine($"TargetGRF: {grfPath}");
        sb.AppendLine($"EntityType: {request.EntityType}");
        sb.AppendLine($"TargetKey: {request.TargetKey}");
        sb.AppendLine($"SourceACT: {request.SourceActPath}");
        sb.AppendLine($"SourceSPR: {request.SourceSprPath}");
        foreach (var path in addedPaths)
            sb.AppendLine("Added to GRF: " + path);
        foreach (var warning in warnings)
            sb.AppendLine("Warning: " + warning);
        sb.AppendLine();
        File.AppendAllText(manifestPath, sb.ToString());
        return manifestPath;
    }

    private static string MakeSafeFileBase(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = raw.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var value = new string(chars);
        if (string.IsNullOrWhiteSpace(value))
            value = "custom_sprite";
        return value;
    }
}
