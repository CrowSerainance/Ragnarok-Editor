using System.Collections.Generic;
using System.IO;

namespace RoDbEditor.Config;

/// <summary>
/// Loads and holds RoDbEditor configuration (GRF paths, etc.).
/// Config file: RoDbEditor.ini in app directory or %AppData%\RoDbEditor\RoDbEditor.ini
/// </summary>
public class RoDbEditorConfig
{
    public const string ConfigFileName = "RoDbEditor.ini";
    public const string SectionGrf = "GRF";

    // User-specific default rAthena data path.
    // This makes F:\MMORPG\RAGNAROK ONLINE\rathena-master the priority
    // source for db (mob, item, skills, spawns, etc.) regardless of any
    // DataPath value present in RoDbEditor.ini.
    private const string DefaultDataPath = @"F:\MMORPG\RAGNAROK ONLINE\rathena-master";

    /// <summary>
    /// Default client folder for GRF files (matches DATA.ini load order).
    /// When no GRF paths are configured, we auto-add these if they exist.
    /// </summary>
    private static readonly string DefaultClientPath = @"F:\MMORPG\RAGNAROK ONLINE\client";

    public List<string> GrfPaths { get; } = new();
    public string? DataPath { get; set; }
    public string? ExtractedAssetsPath { get; set; }

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
                    if (string.Equals(key, "DataPath", System.StringComparison.OrdinalIgnoreCase))
                        config.DataPath = value;
                    if (string.Equals(key, "ExtractedAssetsPath", System.StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(value) && Directory.Exists(value))
                            config.ExtractedAssetsPath = value;
                    }
                    if (string.Equals(key, "TargetGrfFileName", System.StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
                        config.TargetGrfFileName = value;
                    if (string.Equals(key, "TargetGrfPath", System.StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
                        config.TargetGrfPath = value;
                    if (string.Equals(key, "ClientRootPath", System.StringComparison.OrdinalIgnoreCase))
                        config.ClientRootPath = value;
                    if (string.Equals(key, "ClientPatchRoot", System.StringComparison.OrdinalIgnoreCase))
                        config.ClientPatchRoot = value;
                }

                // After reading the INI, force the configured DataPath to the
                // new rAthena folder when it exists so it is always used as
                // the primary server database source.
                if (Directory.Exists(DefaultDataPath))
                {
                    config.DataPath = DefaultDataPath;
                }
            }
        }
        catch
        {
            // Keep default empty config
        }

        // When no GRF paths configured, auto-add client GRFs (same order as DATA.ini)
        if (config.GrfPaths.Count == 0 && Directory.Exists(DefaultClientPath))
        {
            var grfOrder = new[] { "custom.grf", "en.grf", "official_data.grf", "data.grf" };
            foreach (var grf in grfOrder)
            {
                var full = Path.Combine(DefaultClientPath, grf);
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
        if (!string.IsNullOrEmpty(ExtractedAssetsPath))
            sb.AppendLine("ExtractedAssetsPath=" + ExtractedAssetsPath);
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
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidate = Path.Combine(appDir, ConfigFileName);
        if (File.Exists(candidate))
            return candidate;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        candidate = Path.Combine(appData, "RoDbEditor", ConfigFileName);
        return candidate;
    }
}
