using System.Collections.Generic;
using System.Windows;
using GRF.Image;
using RoDbEditor.Config;
using RoDbEditor.Core;
using RoDbEditor.Services;

namespace RoDbEditor;

public partial class App : System.Windows.Application
{
    public static GrfService GrfService { get; private set; } = null!;
    public static RoDbEditorConfig Config { get; private set; } = null!;
    public static ItemDbService ItemDbService { get; private set; } = null!;
    public static IReadOnlyDictionary<int, string> ItemInfoDescriptions { get; set; } = new Dictionary<int, string>();

    public static void ReloadDataPath(string dataPath)
    {
        ItemDbService.LoadFromDataPath(dataPath);
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

        ItemDbService = new ItemDbService();
        if (!string.IsNullOrEmpty(Config.DataPath))
        {
            ItemDbService.LoadFromDataPath(Config.DataPath);
            var lubPath = System.IO.Path.Combine(Config.DataPath, "system", "iteminfo.lub");
            if (!System.IO.File.Exists(lubPath))
                lubPath = System.IO.Path.Combine(Config.DataPath, "data", "iteminfo.lub");
            ItemInfoDescriptions = ItemInfoLubParser.ParseDescriptions(lubPath);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        GrfService?.Dispose();
        base.OnExit(e);
    }
}
