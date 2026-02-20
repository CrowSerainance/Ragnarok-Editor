using System.Collections.Generic;
using System.IO;

namespace RoDbEditor.Config;

/// <summary>
/// Loads and holds RoDbEditor configuration (GRF paths, etc.).
/// Config file: %AppData%\RoDbEditor\RoDbEditor.ini only (per-user, so the app folder stays portable).
/// </summary>
public class RoDbEditorConfig
{
    public const string ConfigFileName = "RoDbEditor.ini";
    public const string SectionGrf = "GRF";

    public List<string> GrfPaths { get; } = new();
    public string? DataPath { get; set; }

    /// <summary>Target GRF filename for asset assignment (default: custom.grf).</summary>
    public string TargetGrfFileName { get; set; } = "custom.grf";

    /// <summary>Full path override for target GRF. When set, used instead of ClientRoot + TargetGrfFileName.</summary>
    public string? TargetGrfPath { get; set; }

    /// <summary>Ragnarok client root folder (contains System/ and GRF files).</summary>
    public string? ClientRootPath { get; set; }

    /// <summary>Patch output root where RoDbEditor writes System/ and data/ overlay files.</summary>
    public string? ClientPatchRoot { get; set; }

    public static RoDbEditorConfig Load()
    {
        var config = new RoDbEditorConfig();
        var configPath = FindConfigPath();
        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
            return config;

        try
        {
            var lines = File.ReadAllLines(configPath);
            string? currentSection = null;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith(";") || line.StartsWith("#"))
                    continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    currentSection = line[1..^1].Trim();
                    continue;
                }
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();
                if (string.Equals(currentSection, SectionGrf, System.StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(key, "Path", System.StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(key, "GrfPath", System.StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(value) && (File.Exists(value) || Directory.Exists(value)))
                            config.GrfPaths.Add(value);
                    }
                    if (string.Equals(key, "DataPath", System.StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value) && Directory.Exists(value))
                        config.DataPath = value;
                    if (string.Equals(key, "TargetGrfFileName", System.StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
                        config.TargetGrfFileName = value;
                    if (string.Equals(key, "TargetGrfPath", System.StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
                        config.TargetGrfPath = value;
                    if (string.Equals(key, "ClientRootPath", System.StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value) && Directory.Exists(value))
                        config.ClientRootPath = value;
                    if (string.Equals(key, "ClientPatchRoot", System.StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value) && Directory.Exists(value))
                        config.ClientPatchRoot = value;
                }
            }
        }
        catch
        {
            // Keep default empty config
        }

        // When no GRF paths configured, auto-discover GRFs in ClientRootPath if set
        if (config.GrfPaths.Count == 0 && !string.IsNullOrEmpty(config.ClientRootPath) && Directory.Exists(config.ClientRootPath))
        {
            var grfOrder = new[] { "custom.grf", "en.grf", "official_data.grf", "data.grf" };
            foreach (var grf in grfOrder)
            {
                var full = Path.Combine(config.ClientRootPath, grf);
                if (File.Exists(full))
                    config.GrfPaths.Add(full);
            }
        }

        return config;
    }

    /// <summary>Persist config to INI file (creates directory if needed).</summary>
    public void Save()
    {
        var configPath = FindConfigPath();
        if (string.IsNullOrEmpty(configPath))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "RoDbEditor");
            Directory.CreateDirectory(dir);
            configPath = Path.Combine(dir, ConfigFileName);
        }
        var dirPath = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(dirPath))
            Directory.CreateDirectory(dirPath);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[" + SectionGrf + "]");
        if (!string.IsNullOrEmpty(DataPath))
            sb.AppendLine("DataPath=" + DataPath);
        sb.AppendLine("TargetGrfFileName=" + (TargetGrfFileName ?? "custom.grf"));
        if (!string.IsNullOrEmpty(TargetGrfPath))
            sb.AppendLine("TargetGrfPath=" + TargetGrfPath);
        if (!string.IsNullOrEmpty(ClientRootPath))
            sb.AppendLine("ClientRootPath=" + ClientRootPath);
        if (!string.IsNullOrEmpty(ClientPatchRoot))
            sb.AppendLine("ClientPatchRoot=" + ClientPatchRoot);
        foreach (var path in GrfPaths)
        {
            if (!string.IsNullOrEmpty(path))
                sb.AppendLine("Path=" + path);
        }
        File.WriteAllText(configPath, sb.ToString());
    }

    private static string? FindConfigPath()
    {
        // Use AppData only so the app folder can be copied without machine-specific paths.
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "RoDbEditor", ConfigFileName);
    }
}
