using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Xml;
using GRF.Image;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using RoDbEditor.Config;
using RoDbEditor.Core;
using RoDbEditor.Services;

namespace RoDbEditor;

public partial class App : System.Windows.Application
{
    public static GrfService GrfService { get; private set; } = null!;
    public static SpriteLookupService SpriteLookupService { get; private set; } = null!;
    public static RoDbEditorConfig Config { get; private set; } = null!;
    public static ItemDbService ItemDbService { get; private set; } = null!;
    public static MobDbService MobDbService { get; private set; } = null!;
    public static SpawnParser SpawnParser { get; private set; } = null!;
    public static NpcIndexService NpcIndexService { get; private set; } = null!;
    public static IReadOnlyDictionary<int, string> ItemInfoDescriptions { get; set; } = new Dictionary<int, string>();
    public static IHighlightingDefinition? RagnarokScriptHighlighting { get; private set; }

    public static void ReloadDataPath(string dataPath)
    {
        ItemDbService.LoadFromDataPath(dataPath);
        MobDbService.LoadFromDataPath(dataPath);
        SpawnParser.LoadFromDataPath(dataPath);
        NpcIndexService.LoadFromDataPath(dataPath);
        var lubPath = System.IO.Path.Combine(dataPath, "system", "iteminfo.lub");
        if (!System.IO.File.Exists(lubPath))
            lubPath = System.IO.Path.Combine(dataPath, "data", "iteminfo.lub");
        ItemInfoDescriptions = ItemInfoLubParser.ParseDescriptions(lubPath);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Register GrfImage -> BitmapSource converter so GRF.ImageProvider and .Cast<BitmapSource>() work
        ImageConverterManager.AddConverter(new GrfImageToWpfConverter());

        Config = RoDbEditorConfig.Load();
        GrfService = new GrfService();
        GrfService.LoadFromConfig(Config);
        SpriteLookupService = new SpriteLookupService(GrfService);

        ItemDbService = new ItemDbService();
        MobDbService = new MobDbService();
        SpawnParser = new SpawnParser();
        NpcIndexService = new NpcIndexService();
        LoadRagnarokScriptHighlighting();
        if (!string.IsNullOrEmpty(Config.DataPath))
        {
            ItemDbService.LoadFromDataPath(Config.DataPath);
            MobDbService.LoadFromDataPath(Config.DataPath);
            SpawnParser.LoadFromDataPath(Config.DataPath);
            NpcIndexService.LoadFromDataPath(Config.DataPath);
            var lubPath = System.IO.Path.Combine(Config.DataPath, "system", "iteminfo.lub");
            if (!System.IO.File.Exists(lubPath))
                lubPath = System.IO.Path.Combine(Config.DataPath, "data", "iteminfo.lub");
            ItemInfoDescriptions = ItemInfoLubParser.ParseDescriptions(lubPath);
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
            // Prevent external DTD resolutions for security
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
            // If loading fails, ensure we don't keep a partially-initialized value.
            RagnarokScriptHighlighting = null;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        GrfService?.Dispose();
        base.OnExit(e);
    }
}
