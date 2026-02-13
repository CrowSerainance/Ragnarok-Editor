using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RoDbEditor.Models;

namespace RoDbEditor.Services;

public class SpriteAssignmentService
{
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
        var dataRoot = Path.Combine(request.ClientRootPath, "data");
        var spriteFolder = request.EntityType switch
        {
            SpriteAssignmentEntityType.Npc => Path.Combine(dataRoot, "sprite", "npc"),
            SpriteAssignmentEntityType.Monster => Path.Combine(dataRoot, "sprite", "monster"),
            _ => Path.Combine(dataRoot, "sprite", "item")
        };
        Directory.CreateDirectory(spriteFolder);

        var destinationSpr = Path.Combine(spriteFolder, safeBase + ".spr");
        File.Copy(request.SourceSprPath, destinationSpr, overwrite: true);
        result.CopiedFiles.Add(destinationSpr);

        if (!string.IsNullOrWhiteSpace(request.SourceActPath) && File.Exists(request.SourceActPath))
        {
            var destinationAct = Path.Combine(spriteFolder, safeBase + ".act");
            File.Copy(request.SourceActPath, destinationAct, overwrite: true);
            result.CopiedFiles.Add(destinationAct);
        }
        else
        {
            result.Warnings.Add("ACT file is missing. Installed SPR only.");
        }

        foreach (var source in request.RelatedPaths ?? new List<string>())
        {
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                continue;

            var ext = Path.GetExtension(source).ToLowerInvariant();
            string? dest = null;
            if (ext == ".wav")
            {
                var wavFolder = Path.Combine(dataRoot, "wav");
                Directory.CreateDirectory(wavFolder);
                dest = Path.Combine(wavFolder, safeBase + ".wav");
            }
            else if (ext is ".bmp" or ".png" or ".tga" or ".jpg" or ".jpeg")
            {
                var fxFolder = Path.Combine(dataRoot, "texture", "effect");
                Directory.CreateDirectory(fxFolder);
                dest = Path.Combine(fxFolder, safeBase + ext);
            }
            else if (ext == ".pal")
            {
                var palFolder = Path.Combine(dataRoot, "texture", "palette");
                Directory.CreateDirectory(palFolder);
                dest = Path.Combine(palFolder, safeBase + ".pal");
            }

            if (dest == null)
                continue;

            File.Copy(source, dest, overwrite: true);
            result.CopiedFiles.Add(dest);
        }

        result.ManifestPath = WriteManifest(request, result.CopiedFiles, result.Warnings);
        result.Success = result.Errors.Count == 0;
        return result;
    }

    private static string WriteManifest(
        SpriteAssignmentRequest request,
        IReadOnlyList<string> copiedFiles,
        IReadOnlyList<string> warnings)
    {
        var manifestPath = Path.Combine(request.ClientRootPath, "asset-assignment-manifest.txt");
        var sb = new StringBuilder();
        sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]");
        sb.AppendLine($"EntityType: {request.EntityType}");
        sb.AppendLine($"TargetKey: {request.TargetKey}");
        sb.AppendLine($"SourceACT: {request.SourceActPath}");
        sb.AppendLine($"SourceSPR: {request.SourceSprPath}");
        foreach (var file in copiedFiles)
            sb.AppendLine("Copied: " + file);
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

