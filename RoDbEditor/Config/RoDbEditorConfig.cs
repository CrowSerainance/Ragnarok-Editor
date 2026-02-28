using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RoDbEditor.Config;

/// <summary>
/// How RoDbEditor is allowed to write client-side files.
/// </summary>
public enum ClientWriteMode
{
    /// <summary>
    /// Prefer safe patch outputs: treat the client installation as read-mostly and
    /// write itemInfo overlays, npcidentity/jobname overlays, and assets under ClientPatchRoot
    /// or a dedicated custom GRF.
    /// </summary>
    PatchOnly = 0,

    /// <summary>
    /// Allow direct edits to the live client install under ClientRootPath
    /// (e.g. System/itemInfo_C.lua and icons under data/texture).
    /// This is convenient for single-user setups but less safe for shared clients.
    /// </summary>
    LiveClient = 1
}

/// <summary>
/// A single workspace profile (server + client + patch paths, GRF list, and write mode).
/// </summary>
public class WorkspaceProfile
{
    /// <summary>Display name for the profile (shown in the FILE ASSIGNMENT UI).</summary>
    public string Name { get; set; } = "Default";

    /// <summary>
    /// rAthena root folder. Base DB is read from db/re or db/pre-re; only db/import is written.
    /// </summary>
    public string? DataPath { get; set; }

    /// <summary>
    /// Base client installation root. Used as a read source for System/ scripts and GRF files.
    /// In PatchOnly mode this should generally be treated as read-only.
    /// </summary>
    public string? ClientRootPath { get; set; }

    /// <summary>
    /// Patch output root. RoDbEditor writes overlay System/ and data/ files here for distribution
    /// via a patcher or GRF builder when running in PatchOnly mode.
    /// </summary>
    public string? ClientPatchRoot { get; set; }

    /// <summary>
    /// Full path override for the target custom GRF that RoDbEditor may modify (sprites, accessory tables).
    /// If null, ClientRootPath + TargetGrfFileName is used.
    /// </summary>
    public string? TargetGrfPath { get; set; }

    /// <summary>
    /// File name of the writable custom GRF (default: custom.grf) when TargetGrfPath is not set.
    /// </summary>
    public string TargetGrfFileName { get; set; } = "custom.grf";

    /// <summary>
    /// Ordered list of GRF files used as read sources for sprites and client data.
    /// The order should mirror DATA.ini (index 0 = highest priority).
    /// </summary>
    public List<string> GrfPaths { get; } = new();

    /// <summary>
    /// How RoDbEditor is allowed to write client-side data for this profile
    /// (PatchOnly vs LiveClient). Defaults to PatchOnly for safety.
    /// </summary>
    public ClientWriteMode ClientWriteMode { get; set; } = ClientWriteMode.PatchOnly;

    /// <summary>Minimum custom item ID (default 50000). Used by IdRegistryService.</summary>
    public int ItemIdMin { get; set; } = 50000;
    /// <summary>Maximum custom item ID (default 59999).</summary>
    public int ItemIdMax { get; set; } = 59999;
    /// <summary>Minimum custom headgear view ID (default 32000).</summary>
    public int ViewIdMin { get; set; } = 32000;
    /// <summary>Maximum custom view ID (default 32999).</summary>
    public int ViewIdMax { get; set; } = 32999;
    /// <summary>Minimum custom mob ID (default 30000).</summary>
    public int MobIdMin { get; set; } = 30000;
    /// <summary>Maximum custom mob ID (default 30999).</summary>
    public int MobIdMax { get; set; } = 30999;

    public WorkspaceProfile Clone()
    {
        var p = new WorkspaceProfile
        {
            Name = Name,
            DataPath = DataPath,
            ClientRootPath = ClientRootPath,
            ClientPatchRoot = ClientPatchRoot,
            TargetGrfPath = TargetGrfPath,
            TargetGrfFileName = TargetGrfFileName,
            ClientWriteMode = ClientWriteMode,
            ItemIdMin = ItemIdMin,
            ItemIdMax = ItemIdMax,
            ViewIdMin = ViewIdMin,
            ViewIdMax = ViewIdMax,
            MobIdMin = MobIdMin,
            MobIdMax = MobIdMax
        };
        foreach (var path in GrfPaths)
            p.GrfPaths.Add(path);
        return p;
    }
}

/// <summary>
/// Loads and holds RoDbEditor configuration:
/// - DataPath: rAthena root; only db/import is written.
/// - ClientRootPath: base client install; primarily read, optionally written in LiveClient mode.
/// - ClientPatchRoot: patch output root; preferred write target in PatchOnly mode.
/// - TargetGrfPath/TargetGrfFileName: writable custom GRF used for sprites and accessory tables.
/// - GrfPaths: ordered GRF list used for reads, mirroring DATA.ini priority where possible.
/// - ClientWriteMode: whether writes go to the live client or patch outputs.
///
/// Config file: %AppData%\RoDbEditor\RoDbEditor.ini only (per-user, so the app folder stays portable).
/// </summary>
public class RoDbEditorConfig
{
    public const string ConfigFileName = "RoDbEditor.ini";
    public const string SectionGrf = "GRF";
    public const string SectionProfiles = "Profiles";
    public const string ProfileSectionPrefix = "Profile:";

    /// <summary>Workspace profiles; at least one is always present after Load.</summary>
    public List<WorkspaceProfile> Profiles { get; } = new();
    /// <summary>Name of the currently active profile.</summary>
    public string? ActiveProfileName { get; set; }

    /// <summary>
    /// Legacy single-profile fields; kept in sync with active profile for existing call sites.
    /// These mirror the active WorkspaceProfile and should not diverge from it.
    /// </summary>
    public List<string> GrfPaths { get; } = new();
    /// <summary>
    /// rAthena root for the active profile. Only db/import is written; base DB remains read-only.
    /// </summary>
    public string? DataPath { get; set; }

    /// <summary>Target GRF filename for asset assignment (default: custom.grf).</summary>
    public string TargetGrfFileName { get; set; } = "custom.grf";

    /// <summary>Full path override for target GRF. When set, used instead of ClientRoot + TargetGrfFileName.</summary>
    public string? TargetGrfPath { get; set; }

    /// <summary>
    /// Ragnarok client root folder (contains System/ and GRF files).
    /// In PatchOnly mode this is treated as read-mostly; in LiveClient mode some writers
    /// may update files directly under this path for convenience.
    /// </summary>
    public string? ClientRootPath { get; set; }

    /// <summary>
    /// Patch output root where RoDbEditor writes System/ and data/ overlay files when generating
    /// patch content (e.g. itemInfo_rodbeditor.lua, npcidentity/jobname overlays).
    /// </summary>
    public string? ClientPatchRoot { get; set; }

    /// <summary>
    /// How RoDbEditor is allowed to write client-side data for the active profile.
    /// Defaults to PatchOnly and is synchronized with the active WorkspaceProfile.
    /// </summary>
    public ClientWriteMode ClientWriteMode { get; set; } = ClientWriteMode.PatchOnly;

    /// <summary>Minimum custom item ID (default 50000). Mirrored from active profile.</summary>
    public int ItemIdMin { get; set; } = 50000;
    public int ItemIdMax { get; set; } = 59999;
    public int ViewIdMin { get; set; } = 32000;
    public int ViewIdMax { get; set; } = 32999;
    public int MobIdMin { get; set; } = 30000;
    public int MobIdMax { get; set; } = 30999;

    /// <summary>
    /// True after the first-run setup wizard has been completed.
    /// When false and paths are empty, App shows SetupWizard instead of MainWindow.
    /// </summary>
    public bool HasCompletedWizard { get; set; }

    public static RoDbEditorConfig Load()
    {
        var config = new RoDbEditorConfig();
        var configPath = FindConfigPath();
        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
        {
            config.EnsureDefaultProfileFromLegacyFields();
            return config;
        }

        try
        {
            var lines = File.ReadAllLines(configPath);
            string? currentSection = null;
            WorkspaceProfile? buildingProfile = null;
            var profileOrder = new List<string>(); // preserve order of profile names

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith(";") || line.StartsWith("#"))
                    continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    if (buildingProfile != null)
                    {
                        config.Profiles.Add(buildingProfile);
                        buildingProfile = null;
                    }
                    currentSection = line[1..^1].Trim();
                    if (currentSection.StartsWith(ProfileSectionPrefix, System.StringComparison.OrdinalIgnoreCase))
                    {
                        var name = currentSection.Substring(ProfileSectionPrefix.Length).Trim();
                        if (!string.IsNullOrEmpty(name))
                        {
                            buildingProfile = new WorkspaceProfile { Name = name };
                            if (!profileOrder.Contains(name, System.StringComparer.OrdinalIgnoreCase))
                                profileOrder.Add(name);
                        }
                    }
                    continue;
                }
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();

                if (string.Equals(currentSection, SectionProfiles, System.StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(key, "ActiveProfileName", System.StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
                        config.ActiveProfileName = value;
                    if (string.Equals(key, "HasCompletedWizard", System.StringComparison.OrdinalIgnoreCase))
                        config.HasCompletedWizard = value.Equals("true", System.StringComparison.OrdinalIgnoreCase) || value == "1";
                    continue;
                }

                if (buildingProfile != null && currentSection != null && currentSection.StartsWith(ProfileSectionPrefix, System.StringComparison.OrdinalIgnoreCase))
                {
                    ApplyProfileKey(buildingProfile, key, value);
                    continue;
                }

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
                    if (string.Equals(key, "TargetGrfFileName", System.StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
                        config.TargetGrfFileName = value;
                    if (string.Equals(key, "TargetGrfPath", System.StringComparison.OrdinalIgnoreCase))
                        config.TargetGrfPath = value;
                    if (string.Equals(key, "ClientRootPath", System.StringComparison.OrdinalIgnoreCase))
                        config.ClientRootPath = value;
                    if (string.Equals(key, "ClientPatchRoot", System.StringComparison.OrdinalIgnoreCase))
                        config.ClientPatchRoot = value;
                    if (string.Equals(key, "ClientWriteMode", System.StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
                    {
                        if (System.Enum.TryParse<ClientWriteMode>(value, ignoreCase: true, out var mode))
                            config.ClientWriteMode = mode;
                    }
                    if (string.Equals(key, "ItemIdMin", System.StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var iMin))
                        config.ItemIdMin = iMin;
                    if (string.Equals(key, "ItemIdMax", System.StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var iMax))
                        config.ItemIdMax = iMax;
                    if (string.Equals(key, "ViewIdMin", System.StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var vMin))
                        config.ViewIdMin = vMin;
                    if (string.Equals(key, "ViewIdMax", System.StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var vMax))
                        config.ViewIdMax = vMax;
                    if (string.Equals(key, "MobIdMin", System.StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var mMin))
                        config.MobIdMin = mMin;
                    if (string.Equals(key, "MobIdMax", System.StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var mMax))
                        config.MobIdMax = mMax;
                }
            }

            if (buildingProfile != null)
                config.Profiles.Add(buildingProfile);

            if (config.Profiles.Count > 0)
            {
                if (string.IsNullOrEmpty(config.ActiveProfileName) && profileOrder.Count > 0)
                    config.ActiveProfileName = profileOrder[0];
                config.ApplyActiveProfileToLegacyFields();
            }
            else
                config.EnsureDefaultProfileFromLegacyFields();
        }
        catch
        {
            config.EnsureDefaultProfileFromLegacyFields();
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
            var active = config.GetActiveProfile();
            if (active != null)
            {
                active.GrfPaths.Clear();
                foreach (var path in config.GrfPaths)
                    active.GrfPaths.Add(path);
            }
        }

        return config;
    }

    private static void ApplyProfileKey(WorkspaceProfile profile, string key, string value)
    {
        if (string.Equals(key, "DataPath", System.StringComparison.OrdinalIgnoreCase))
            profile.DataPath = value;
        else if (string.Equals(key, "ClientRootPath", System.StringComparison.OrdinalIgnoreCase))
            profile.ClientRootPath = value;
        else if (string.Equals(key, "ClientPatchRoot", System.StringComparison.OrdinalIgnoreCase))
            profile.ClientPatchRoot = value;
        else if (string.Equals(key, "TargetGrfPath", System.StringComparison.OrdinalIgnoreCase))
            profile.TargetGrfPath = value;
        else if (string.Equals(key, "TargetGrfFileName", System.StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
            profile.TargetGrfFileName = value;
        else if (string.Equals(key, "ClientWriteMode", System.StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
        {
            if (System.Enum.TryParse<ClientWriteMode>(value, ignoreCase: true, out var mode))
                profile.ClientWriteMode = mode;
        }
        else if ((string.Equals(key, "Path", System.StringComparison.OrdinalIgnoreCase) || string.Equals(key, "GrfPath", System.StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrEmpty(value))
            profile.GrfPaths.Add(value);
        else if (string.Equals(key, "ItemIdMin", System.StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var itemMin))
            profile.ItemIdMin = itemMin;
        else if (string.Equals(key, "ItemIdMax", System.StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var itemMax))
            profile.ItemIdMax = itemMax;
        else if (string.Equals(key, "ViewIdMin", System.StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var viewMin))
            profile.ViewIdMin = viewMin;
        else if (string.Equals(key, "ViewIdMax", System.StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var viewMax))
            profile.ViewIdMax = viewMax;
        else if (string.Equals(key, "MobIdMin", System.StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var mobMin))
            profile.MobIdMin = mobMin;
        else if (string.Equals(key, "MobIdMax", System.StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var mobMax))
            profile.MobIdMax = mobMax;
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

        // [Profiles] section: active profile name and wizard state
        sb.AppendLine("[" + SectionProfiles + "]");
        sb.AppendLine("ActiveProfileName=" + (ActiveProfileName ?? GetActiveProfile()?.Name ?? "Default"));
        sb.AppendLine("HasCompletedWizard=" + (HasCompletedWizard ? "true" : "false"));
        sb.AppendLine();

        // [Profile:Name] sections: one per profile
        foreach (var profile in Profiles)
        {
            var name = string.IsNullOrEmpty(profile.Name) ? "Default" : profile.Name;
            sb.AppendLine("[" + ProfileSectionPrefix + name + "]");
            if (!string.IsNullOrEmpty(profile.DataPath))
                sb.AppendLine("DataPath=" + profile.DataPath);
            if (!string.IsNullOrEmpty(profile.ClientRootPath))
                sb.AppendLine("ClientRootPath=" + profile.ClientRootPath);
            if (!string.IsNullOrEmpty(profile.ClientPatchRoot))
                sb.AppendLine("ClientPatchRoot=" + profile.ClientPatchRoot);
            if (!string.IsNullOrEmpty(profile.TargetGrfPath))
                sb.AppendLine("TargetGrfPath=" + profile.TargetGrfPath);
            sb.AppendLine("TargetGrfFileName=" + (profile.TargetGrfFileName ?? "custom.grf"));
            sb.AppendLine("ClientWriteMode=" + profile.ClientWriteMode);
            sb.AppendLine("ItemIdMin=" + profile.ItemIdMin);
            sb.AppendLine("ItemIdMax=" + profile.ItemIdMax);
            sb.AppendLine("ViewIdMin=" + profile.ViewIdMin);
            sb.AppendLine("ViewIdMax=" + profile.ViewIdMax);
            sb.AppendLine("MobIdMin=" + profile.MobIdMin);
            sb.AppendLine("MobIdMax=" + profile.MobIdMax);
            foreach (var path in profile.GrfPaths)
            {
                if (!string.IsNullOrEmpty(path))
                    sb.AppendLine("Path=" + path);
            }
            sb.AppendLine();
        }

        // Legacy [GRF] section: mirror active profile for backward compatibility
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
        sb.AppendLine("ClientWriteMode=" + ClientWriteMode);
        sb.AppendLine("ItemIdMin=" + ItemIdMin);
        sb.AppendLine("ItemIdMax=" + ItemIdMax);
        sb.AppendLine("ViewIdMin=" + ViewIdMin);
        sb.AppendLine("ViewIdMax=" + ViewIdMax);
        sb.AppendLine("MobIdMin=" + MobIdMin);
        sb.AppendLine("MobIdMax=" + MobIdMax);
        foreach (var path in GrfPaths)
        {
            if (!string.IsNullOrEmpty(path))
                sb.AppendLine("Path=" + path);
        }
        File.WriteAllText(configPath, sb.ToString());
    }

    /// <summary>Returns the currently active profile, or null if none.</summary>
    public WorkspaceProfile? GetActiveProfile()
    {
        if (string.IsNullOrEmpty(ActiveProfileName))
            return Profiles.FirstOrDefault();
        return Profiles.FirstOrDefault(p => string.Equals(p.Name, ActiveProfileName, System.StringComparison.OrdinalIgnoreCase))
            ?? Profiles.FirstOrDefault();
    }

    /// <summary>If no profiles exist, creates a default profile from legacy [GRF] fields.</summary>
    public void EnsureDefaultProfileFromLegacyFields()
    {
        if (Profiles.Count > 0)
            return;
        var legacy = new WorkspaceProfile
        {
            Name = "Default",
            DataPath = DataPath,
            ClientRootPath = ClientRootPath,
            ClientPatchRoot = ClientPatchRoot,
            TargetGrfPath = TargetGrfPath,
            TargetGrfFileName = TargetGrfFileName ?? "custom.grf",
            ClientWriteMode = ClientWriteMode,
            ItemIdMin = ItemIdMin,
            ItemIdMax = ItemIdMax,
            ViewIdMin = ViewIdMin,
            ViewIdMax = ViewIdMax,
            MobIdMin = MobIdMin,
            MobIdMax = MobIdMax
        };
        foreach (var path in GrfPaths)
            legacy.GrfPaths.Add(path);
        Profiles.Add(legacy);
        if (string.IsNullOrEmpty(ActiveProfileName))
            ActiveProfileName = legacy.Name;
    }

    /// <summary>Copies active profile values into legacy fields (DataPath, ClientRootPath, etc.) for existing call sites.</summary>
    public void ApplyActiveProfileToLegacyFields()
    {
        var active = GetActiveProfile();
        if (active == null)
            return;
        DataPath = active.DataPath;
        ClientRootPath = active.ClientRootPath;
        ClientPatchRoot = active.ClientPatchRoot;
        TargetGrfPath = active.TargetGrfPath;
        TargetGrfFileName = active.TargetGrfFileName ?? "custom.grf";
        ClientWriteMode = active.ClientWriteMode;
        ItemIdMin = active.ItemIdMin;
        ItemIdMax = active.ItemIdMax;
        ViewIdMin = active.ViewIdMin;
        ViewIdMax = active.ViewIdMax;
        MobIdMin = active.MobIdMin;
        MobIdMax = active.MobIdMax;
        GrfPaths.Clear();
        foreach (var path in active.GrfPaths)
            GrfPaths.Add(path);
    }

    private static string? FindConfigPath()
    {
        // Use AppData only so the app folder can be copied without machine-specific paths.
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "RoDbEditor", ConfigFileName);
    }
}
