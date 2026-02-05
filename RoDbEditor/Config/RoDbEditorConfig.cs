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

    public List<string> GrfPaths { get; } = new();
    public string? DataPath { get; set; }

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
                }
            }
        }
        catch
        {
            // Keep default empty config
        }

        return config;
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
