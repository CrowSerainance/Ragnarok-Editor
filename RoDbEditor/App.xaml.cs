using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Xml;
using GRF.Image;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using RoDbEditor.Config;
using RoDbEditor.Core;
using RoDbEditor.Services;
using RoDbEditor.Services.Blueprint;
using RoDbEditor.Services.Client;
using Utilities.Parsers.Lua;

namespace RoDbEditor;

public partial class App : System.Windows.Application
{
    public static GrfService GrfService { get; private set; } = null!;
    public static SpriteLookupService SpriteLookupService { get; set; } = null!;
    public static FileSystemSpriteSource? FileSystemSpriteSource { get; set; }
    public static RoDbEditorConfig Config { get; private set; } = null!;
    public static ItemDbService ItemDbService { get; private set; } = null!;
    public static MobDbService MobDbService { get; private set; } = null!;
    public static SpawnParser SpawnParser { get; private set; } = null!;
    public static NpcIndexService NpcIndexService { get; private set; } = null!;
    public static ItemPathService ItemPathService { get; private set; } = null!;
    public static MapIndexService MapIndexService { get; private set; } = null!;
    public static AttrFixTableService AttrFixTableService { get; private set; } = null!;
    public static SkillDbService SkillDbService { get; private set; } = null!;
    public static MobSkillDbService MobSkillDbService { get; private set; } = null!;
    public static SkillDbMiniService SkillDbMiniService { get; private set; } = null!;
    public static MobSkillPanelService MobSkillPanelService { get; private set; } = null!;
    public static MobSkillWriteService? MobSkillWriteService { get; private set; }
    public static GrfWriterService GrfWriterService { get; private set; } = null!;
    public static SpriteAssignmentService SpriteAssignmentService { get; private set; } = null!;
    public static ItemInfoLuaWriter? ItemInfoLuaWriter { get; set; }
    public static AccessoryIdWriter? AccessoryIdWriter { get; set; }
    public static ClientAssetWriter? ClientAssetWriter { get; set; }
    public static MobInfoLuaWriter? MobInfoLuaWriter { get; set; }
    public static NpcScriptWriter? NpcScriptWriter { get; private set; }
    public static EntityDesignationService EntityDesignationService { get; private set; } = null!;
    public static BlueprintExportService BlueprintExportService { get; private set; } = null!;
    public static ClientItemInfoService ClientItemInfoService { get; private set; } = null!;
    public static ClientItemInfoWriter ClientItemInfoWriter { get; private set; } = null!;
    public static MobAvailService MobAvailService { get; private set; } = null!;
    public static ClientNpcIdentityService ClientNpcIdentityService { get; private set; } = null!;
    public static ClientJobNameService ClientJobNameService { get; private set; } = null!;
    public static IdRegistryService IdRegistryService { get; private set; } = null!;
    public static IReadOnlyDictionary<int, string> ItemInfoDescriptions { get; set; } = new Dictionary<int, string>();
    public static IHighlightingDefinition? RagnarokScriptHighlighting { get; private set; }

    /// <summary>
    /// Applies a workspace profile at runtime: syncs config, saves, and rebuilds all dependent services (GRF, sprites, writers, server/client data).
    /// Call this when switching workspace without restart.
    /// </summary>
    public static void ApplyWorkspaceProfile(WorkspaceProfile profile)
    {
        if (profile == null) return;

        var existing = Config.Profiles.FirstOrDefault(p => string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.DataPath = profile.DataPath;
            existing.ClientRootPath = profile.ClientRootPath;
            existing.ClientPatchRoot = profile.ClientPatchRoot;
            existing.TargetGrfPath = profile.TargetGrfPath;
            existing.TargetGrfFileName = profile.TargetGrfFileName ?? "custom.grf";
            existing.GrfPaths.Clear();
            foreach (var path in profile.GrfPaths)
                existing.GrfPaths.Add(path);
        }
        else
        {
            Config.Profiles.Add(profile.Clone());
        }

        Config.ActiveProfileName = profile.Name;
        Config.ApplyActiveProfileToLegacyFields();

        if (Config.GrfPaths.Count == 0 && !string.IsNullOrEmpty(Config.ClientRootPath) && Directory.Exists(Config.ClientRootPath))
        {
            var grfOrder = new[] { "custom.grf", "en.grf", "official_data.grf", "data.grf" };
            foreach (var grf in grfOrder)
            {
                var full = Path.Combine(Config.ClientRootPath, grf);
                if (File.Exists(full))
                    Config.GrfPaths.Add(full);
            }
            var active = Config.GetActiveProfile();
            if (active != null)
            {
                active.GrfPaths.Clear();
                foreach (var path in Config.GrfPaths)
                    active.GrfPaths.Add(path);
            }
        }

        Config.Save();

        GrfService.LoadFromConfig(Config);

        FileSystemSpriteSource = null;
        if (!string.IsNullOrEmpty(Config.ClientRootPath))
        {
            var dataSprite = Path.Combine(Config.ClientRootPath, "data", "sprite");
            var dataFolder = Path.Combine(Config.ClientRootPath, "data");
            if (Directory.Exists(dataSprite))
                FileSystemSpriteSource = new FileSystemSpriteSource(dataSprite);
            else if (Directory.Exists(dataFolder))
                FileSystemSpriteSource = new FileSystemSpriteSource(dataFolder);
        }

        SpriteLookupService = new SpriteLookupService(GrfService, FileSystemSpriteSource);
        SpriteLookupService.ClearCache();

        ItemPathService = new ItemPathService(ItemDbService, GrfService, SpriteLookupService);

        var clientRoot = !string.IsNullOrEmpty(Config.ClientRootPath) && Directory.Exists(Config.ClientRootPath)
            ? Config.ClientRootPath
            : null;
        if (!string.IsNullOrEmpty(clientRoot))
        {
            // ClientRootPath is always used as a read source for System/ and GRFs.
            // Whether we also write directly to itemInfo_C.lua and icons here depends
            // on the configured ClientWriteMode; PatchOnly prefers patch outputs.
            if (Config.ClientWriteMode == ClientWriteMode.LiveClient)
            {
                ItemInfoLuaWriter = new ItemInfoLuaWriter(clientRoot);
                ClientAssetWriter = new ClientAssetWriter(clientRoot);
            }
            else
            {
                ItemInfoLuaWriter = null;
                ClientAssetWriter = null;
            }

            AccessoryIdWriter = new AccessoryIdWriter(clientRoot, GrfService, GrfWriterService);
            MobInfoLuaWriter = new MobInfoLuaWriter(clientRoot);
        }
        else
        {
            ItemInfoLuaWriter = null;
            AccessoryIdWriter = null;
            ClientAssetWriter = null;
            MobInfoLuaWriter = null;
        }
        NpcScriptWriter = new NpcScriptWriter(Config.DataPath ?? clientRoot ?? string.Empty);

        if (!string.IsNullOrEmpty(Config.ClientRootPath))
        {
            var clientSys = Path.Combine(Config.ClientRootPath, "System");
            if (Directory.Exists(clientSys))
            {
                ClientItemInfoService.LoadFromClientSystem(clientSys);
                ClientNpcIdentityService.LoadFromClientSystem(clientSys);
                ClientJobNameService.LoadFromClientSystem(clientSys);
            }
        }

        if (!string.IsNullOrEmpty(Config.DataPath))
            ReloadDataPath(Config.DataPath);

        RefreshIdRegistry();

        if (GrfService.IsLoaded)
            ReloadFromGrf();
    }

    /// <summary>Refresh IdRegistryService from db/import and optionally client accessory tables.</summary>
    public static void RefreshIdRegistry()
    {
        IdRegistryService.ScanServerDbImport(Config.DataPath);
        IdRegistryService.ScanClientAccessoryTables(
            path => GrfService?.IsLoaded == true ? GrfService.GetData(path) : null,
            Config.ClientRootPath);
    }

    /// <summary>
    /// Reload item/mob data from GRF. Called when GRF is loaded.
    /// Used as primary source when no DataPath, or as fallback when DataPath has no item_db/mob_db.
    /// </summary>
    public static void ReloadFromGrf()
    {
        if (GrfService == null || !GrfService.IsLoaded)
            return;

        // Load items from GRF only when we have none (no DataPath item_db, or DataPath = client folder)
        if (ItemDbService.Items.Count == 0)
        {
            LoadItemsFromGrf();
        }

        if (MobDbService.Mobs.Count == 0)
        {
            LoadMobsFromGrf();
        }
    }

    private static void LoadItemsFromGrf()
    {
        if (GrfService == null || !GrfService.IsLoaded) return;

        // Try to load iteminfo.lub from GRF
        var iteminfoPath = @"data\luafiles514\lua files\datainfo\iteminfo.lub";
        var iteminfoData = GrfService.GetData(iteminfoPath);
        
        if (iteminfoData == null || iteminfoData.Length == 0)
        {
            // Try alternate paths
            var altPaths = new[]
            {
                @"data\luafiles514\lua files\datainfo\iteminfo.lua",
                @"data\iteminfo.lub",
                @"data\iteminfo.lua"
            };
            foreach (var alt in altPaths)
            {
                iteminfoData = GrfService.GetData(alt);
                if (iteminfoData != null && iteminfoData.Length > 0)
                    break;
            }
        }

        if (iteminfoData != null && iteminfoData.Length > 0)
        {
            if (LuaParser.IsLub(iteminfoData))
                System.Diagnostics.Debug.WriteLine("[App] iteminfo.lub is compiled; decompiling via GRF LUB reader");
            ItemDbService.LoadFromGrfData(iteminfoData);
            ItemInfoDescriptions = ItemInfoLubParser.ParseDescriptionsFromData(iteminfoData);
            System.Diagnostics.Debug.WriteLine($"[App] Loaded {ItemDbService.Items.Count} items from GRF");
        }
    }

    private static void LoadMobsFromGrf()
    {
        if (GrfService == null || !GrfService.IsLoaded) return;

        var mobinfoPath = @"data\luafiles514\lua files\datainfo\mobinfo.lub";
        var mobinfoData = GrfService.GetData(mobinfoPath);
        if (mobinfoData == null || mobinfoData.Length == 0)
        {
            var mobAltPaths = new[]
            {
                @"data\luafiles514\lua files\datainfo\mobinfo.lua",
                @"data\lua files\datainfo\mobinfo.lub",
                @"data\lua files\datainfo\mobinfo.lua",
                @"data\mobinfo.lub",
                @"data\mobinfo.lua"
            };
            foreach (var alt in mobAltPaths)
            {
                mobinfoData = GrfService.GetData(alt);
                if (mobinfoData != null && mobinfoData.Length > 0)
                    break;
            }
        }
        if (mobinfoData != null && mobinfoData.Length > 0)
        {
            if (LuaParser.IsLub(mobinfoData))
                System.Diagnostics.Debug.WriteLine("[App] mobinfo.lub is compiled; decompiling via GRF LUB reader");
            MobDbService.LoadFromGrfData(mobinfoData);
            System.Diagnostics.Debug.WriteLine($"[App] Loaded {MobDbService.Mobs.Count} mobs from GRF");
        }
    }

    /// <summary>
    /// Reload server data from rAthena DataPath (db, npc, spawns).
    /// Items/mobs come from item_db.yml and mob_db.yml when present.
    /// </summary>
    public static void ReloadDataPath(string dataPath)
    {
        ItemDbService.LoadFromDataPath(dataPath);
        MobDbService.LoadFromDataPath(dataPath);
        SpawnParser.LoadFromDataPath(dataPath);
        NpcIndexService.LoadFromDataPath(dataPath);
        MapIndexService.LoadFromDataPath(dataPath);
        AttrFixTableService.LoadFromDataPath(dataPath);
        SkillDbService.LoadFromDataPath(dataPath);
        MobSkillDbService.LoadFromDataPath(dataPath);
        SkillDbMiniService.LoadFromDataPath(dataPath);
        MobSkillWriteService = new MobSkillWriteService(dataPath);
        EntityDesignationService = new EntityDesignationService(NpcIndexService, MobDbService);
        MobAvailService.LoadFromDataPath(dataPath);

        var lubPath = Path.Combine(dataPath, "system", "iteminfo.lub");
        if (!File.Exists(lubPath))
            lubPath = Path.Combine(dataPath, "data", "iteminfo.lub");
        ItemInfoDescriptions = ItemInfoLubParser.ParseDescriptions(lubPath);
    }

    public static async System.Threading.Tasks.Task ReloadDataPathAsync(string dataPath)
    {
        var newItemDb = new ItemDbService();
        var newMobDb = new MobDbService();
        var newNpcIndex = new NpcIndexService();
        var newSpawn = new SpawnParser();
        
        var newAttrFix = new AttrFixTableService();
        var newMapIndex = new MapIndexService();
        var newSkillDb = new SkillDbService();
        var newMobSkillDb = new MobSkillDbService();
        var newSkillDbMini = new SkillDbMiniService();
        await System.Threading.Tasks.Task.Run(() => {
            newItemDb.LoadFromDataPath(dataPath);
            newMobDb.LoadFromDataPath(dataPath);
            newNpcIndex.LoadFromDataPath(dataPath);
            newSpawn.LoadFromDataPath(dataPath);
            newMapIndex.LoadFromDataPath(dataPath);
            newAttrFix.LoadFromDataPath(dataPath);
            newSkillDb.LoadFromDataPath(dataPath);
            newMobSkillDb.LoadFromDataPath(dataPath);
            newSkillDbMini.LoadFromDataPath(dataPath);
        });

        ItemDbService = newItemDb;
        MobDbService = newMobDb;
        NpcIndexService = newNpcIndex;
        SpawnParser = newSpawn;
        MapIndexService = newMapIndex;
        AttrFixTableService = newAttrFix;
        SkillDbService = newSkillDb;
        MobSkillDbService = newMobSkillDb;
        SkillDbMiniService = newSkillDbMini;
        MobSkillPanelService = new MobSkillPanelService(MobSkillDbService, SkillDbMiniService, MobDbService);
        MobSkillWriteService = new MobSkillWriteService(dataPath);

        // Re-init dependent services
        ItemPathService = new ItemPathService(newItemDb, GrfService, SpriteLookupService);
        EntityDesignationService = new EntityDesignationService(newNpcIndex, newMobDb);
        MobAvailService.LoadFromDataPath(dataPath);

        // Reload GRF items if needed (fallback)
        if (GrfService != null && GrfService.IsLoaded)
             ReloadFromGrf();
             
         // Reload iteminfo
        var lubPath = Path.Combine(dataPath, "system", "iteminfo.lub");
        if (!File.Exists(lubPath))
            lubPath = Path.Combine(dataPath, "data", "iteminfo.lub");
        
        // Parsing iteminfo is fast enough? Or move to Task.Run?
        // It uses ItemInfoLubParser.
        await System.Threading.Tasks.Task.Run(() => {
             ItemInfoDescriptions = ItemInfoLubParser.ParseDescriptions(lubPath);
        });
    }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // Register GrfImage -> BitmapSource converter
        ImageConverterManager.AddConverter(new GrfImageToWpfConverter());

        Config = RoDbEditorConfig.Load();

        GrfService = new GrfService();
        GrfService.LoadFromConfig(Config);

        // Create filesystem sprite source from client data folder for sprite preview in Items/Monsters tabs
        if (!string.IsNullOrEmpty(Config.ClientRootPath))
        {
            var dataSprite = Path.Combine(Config.ClientRootPath, "data", "sprite");
            var dataFolder = Path.Combine(Config.ClientRootPath, "data");
            if (Directory.Exists(dataSprite))
                FileSystemSpriteSource = new FileSystemSpriteSource(dataSprite);
            else if (Directory.Exists(dataFolder))
                FileSystemSpriteSource = new FileSystemSpriteSource(dataFolder);
        }

        SpriteLookupService = new SpriteLookupService(GrfService, FileSystemSpriteSource);

        ItemDbService = new ItemDbService();
        MobDbService = new MobDbService();
        SpawnParser = new SpawnParser();
        NpcIndexService = new NpcIndexService();
        ItemPathService = new ItemPathService(ItemDbService, GrfService, SpriteLookupService);
        MapIndexService = new MapIndexService();
        AttrFixTableService = new AttrFixTableService();
        SkillDbService = new SkillDbService();
        MobSkillDbService = new MobSkillDbService();
        SkillDbMiniService = new SkillDbMiniService();
        MobSkillPanelService = new MobSkillPanelService(MobSkillDbService, SkillDbMiniService, MobDbService);
        GrfWriterService = new GrfWriterService();
        SpriteAssignmentService = new SpriteAssignmentService(GrfWriterService);
        var clientRoot = !string.IsNullOrEmpty(Config.ClientRootPath) && Directory.Exists(Config.ClientRootPath)
            ? Config.ClientRootPath
            : null;
        if (!string.IsNullOrEmpty(clientRoot))
        {
            // ClientRootPath is always used as a read source for System/ and GRFs.
            // Whether we also write directly to itemInfo_C.lua and icons here depends
            // on the configured ClientWriteMode; PatchOnly prefers patch outputs.
            if (Config.ClientWriteMode == ClientWriteMode.LiveClient)
            {
                ItemInfoLuaWriter = new ItemInfoLuaWriter(clientRoot);
                ClientAssetWriter = new ClientAssetWriter(clientRoot);
            }
            else
            {
                ItemInfoLuaWriter = null;
                ClientAssetWriter = null;
            }

            AccessoryIdWriter = new AccessoryIdWriter(clientRoot, GrfService, GrfWriterService);
            MobInfoLuaWriter = new MobInfoLuaWriter(clientRoot);
        }
        NpcScriptWriter = new NpcScriptWriter(Config.DataPath ?? clientRoot ?? string.Empty);
        EntityDesignationService = new EntityDesignationService(NpcIndexService, MobDbService);
        IdRegistryService = new IdRegistryService();
        BlueprintExportService = new BlueprintExportService();
        ClientItemInfoService = new ClientItemInfoService();
        ClientItemInfoWriter = new ClientItemInfoWriter();
        MobAvailService = new MobAvailService();
        ClientNpcIdentityService = new ClientNpcIdentityService();
        ClientJobNameService = new ClientJobNameService();

        // Wire up condition text resolvers
        MobSkillConditionText.SkillNameResolver = id => SkillDbMiniService.ResolveDisplayName(id);
        
        LoadRagnarokScriptHighlighting();

        // If data path is set, load server data (npc, spawns, items/mobs from YAML if present)
        if (!string.IsNullOrEmpty(Config.DataPath))
        {
            ReloadDataPath(Config.DataPath);
        }

        RefreshIdRegistry();

        if (!string.IsNullOrEmpty(Config.ClientRootPath))
        {
            var clientSys = Path.Combine(Config.ClientRootPath, "System");
            if (Directory.Exists(clientSys))
            {
                ClientItemInfoService.LoadFromClientSystem(clientSys);
                ClientNpcIdentityService.LoadFromClientSystem(clientSys);
                ClientJobNameService.LoadFromClientSystem(clientSys);
            }
        }

        // When GRF is loaded, load items/mobs from GRF. This runs either as primary source
        // (no DataPath) or as fallback when DataPath has no item_db/mob_db (e.g. client\data).
        if (GrfService.IsLoaded)
        {
            ReloadFromGrf();
        }

        var needWizard = !Config.HasCompletedWizard ||
            (string.IsNullOrEmpty(Config.DataPath) && string.IsNullOrEmpty(Config.ClientRootPath));

        if (needWizard)
        {
            var wiz = new SetupWizard();
            wiz.ShowDialog();
            if (wiz.CompletedSuccessfully && Config.GetActiveProfile() is { } profile)
            {
                ApplyWorkspaceProfile(profile);
                var main = new MainWindow();
                MainWindow = main;
                main.Show();
            }
            else
            {
                Shutdown();
                return;
            }
        }
        else
        {
            var main = new MainWindow();
            MainWindow = main;
            main.Show();
        }
    }

    private static void LoadRagnarokScriptHighlighting()
    {
        var asm = typeof(App).Assembly;
        var name = asm.GetName().Name + ".Resources.RagnarokScript.xshd";
        using var stream = asm.GetManifestResourceStream(name);
        if (stream == null) return;

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        using var reader = XmlReader.Create(stream, settings);
        try
        {
            RagnarokScriptHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }
        catch
        {
            RagnarokScriptHighlighting = null;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        GrfService?.Dispose();
        base.OnExit(e);
    }
}
