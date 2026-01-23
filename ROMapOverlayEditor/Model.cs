using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ROMapOverlayEditor;

public enum PlacableKind
{
    Npc,
    Warp,
    Spawn
}

public abstract class Placable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; set; } = Guid.NewGuid();
    
    private PlacableKind _kind;
    public PlacableKind Kind { get => _kind; set => SetField(ref _kind, value); }
    
    private string _label = "";
    public string Label { get => _label; set => SetField(ref _label, value); }
    
    private string _mapName = "";
    public string MapName { get => _mapName; set => SetField(ref _mapName, value); }
    
    private int _x;
    public int X { get => _x; set => SetField(ref _x, value); }
    
    private int _y;
    public int Y { get => _y; set => SetField(ref _y, value); }
    
    private int _dir = 0;
    public int Dir { get => _dir; set => SetField(ref _dir, value); }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed class NpcPlacable : Placable
{
    private string _sprite = "4_M_01";
    public string Sprite { get => _sprite; set => SetField(ref _sprite, value); }
    
    private string _scriptName = "MyNpc";
    public string ScriptName { get => _scriptName; set => SetField(ref _scriptName, value); }
    
    private string _scriptBody = "mes \"Hello!\";\nclose;";
    public string ScriptBody { get => _scriptBody; set => SetField(ref _scriptBody, value); }

    public NpcPlacable()
    {
        Kind = PlacableKind.Npc;
    }
}

public sealed class WarpPlacable : Placable
{
    private int _w = 1;
    public int W { get => _w; set => SetField(ref _w, value); }
    
    private int _h = 1;
    public int H { get => _h; set => SetField(ref _h, value); }
    
    private string _destMap = "prontera";
    public string DestMap { get => _destMap; set => SetField(ref _destMap, value); }
    
    private int _destX = 150;
    public int DestX { get => _destX; set => SetField(ref _destX, value); }
    
    private int _destY = 150;
    public int DestY { get => _destY; set => SetField(ref _destY, value); }
    
    private string _warpName = "warp_1";
    public string WarpName { get => _warpName; set => SetField(ref _warpName, value); }

    public WarpPlacable()
    {
        Kind = PlacableKind.Warp;
    }
}

public sealed class ProjectData : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Schema version for project.romap.json.</summary>
    public int Version { get; set; } = 1;

    private string _projectName = "New Project";
    public string ProjectName { get => _projectName; set => SetField(ref _projectName, value); }

    /// <summary>Root folder for this project (project.romap.json lives here).</summary>
    public string? BaseFolderPath { get; set; }

    private string _mapName = "prontera";
    public string MapName { get => _mapName; set => SetField(ref _mapName, value); }

    public string? BackgroundImagePath { get; set; } = null;

    /// <summary>When set, map is loaded from this GRF using GrfInternalPath.</summary>
    public string? GrfFilePath { get; set; }

    /// <summary>Internal path inside the GRF (e.g. data\texture\effect\map\prontera.bmp).</summary>
    public string? GrfInternalPath { get; set; }

    /// <summary>Optional folder containing Towninfo.lua/lub when not in GRF or when GRF has bytecode.</summary>
    public string? LuaDataFolderPath { get; set; }

    /// <summary>Last selected town name for restoring selection when re-opening.</summary>
    public string? LastSelectedTown { get; set; }

    /// <summary>
    /// Map width in game tiles. Used for coordinate conversion.
    /// Set to 0 to auto-estimate from image dimensions.
    /// Common values: 400 (towns), 512 (fields), 300 (dungeons)
    /// </summary>
    public int MapTileWidth
    {
        get => _mapTileWidth;
        set { _mapTileWidth = value; OnPropertyChanged(); }
    }
    private int _mapTileWidth = 0;

    /// <summary>
    /// Map height in game tiles. Used for coordinate conversion.
    /// Set to 0 to auto-estimate from image dimensions.
    /// </summary>
    public int MapTileHeight
    {
        get => _mapTileHeight;
        set { _mapTileHeight = value; OnPropertyChanged(); }
    }
    private int _mapTileHeight = 0;

    // Pixels per tile; origin and Y-inversion in code
    private double _pixelsPerTile = 8.0;
    public double PixelsPerTile { get => _pixelsPerTile; set => SetField(ref _pixelsPerTile, value); }

    private bool _originBottomLeft = true;
    public bool OriginBottomLeft { get => _originBottomLeft; set => SetField(ref _originBottomLeft, value); }

    public List<Placable> Items { get; set; } = new();

    // --- Export paths (relative to BaseFolderPath or absolute) ---
    /// <summary>e.g. "scripts" or "scripts/npcs_custom.txt". Default scripts/.</summary>
    public string? ExportScriptsPath { get; set; }

    /// <summary>e.g. "db". For mob_db_patches.yml etc. Default db/.</summary>
    public string? ExportDbPatchPath { get; set; }

    // --- DB source paths (optional; for Mob Editor) ---
    public string? MobDbSourcePath { get; set; }
    public string? MobSkillDbSourcePath { get; set; }

    /// <summary>
    /// Path to extracted client data folder (contains .gat, .rsw files).
    /// Example: F:\MMORPG\RAGNAROK ONLINE\client\data
    /// </summary>
    public string? ClientDataPath { get; set; }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

/// <summary>Tracks where a Placable was imported from, for diff/changelog generation.</summary>
public class ImportSource
{
    public string SourceFile { get; set; } = "";
    public int OriginalX { get; set; }
    public int OriginalY { get; set; }
    public string OriginalSprite { get; set; } = "";
    public DateTime ImportedAt { get; set; } = DateTime.Now;
    public bool IsModified { get; set; }
}

public sealed class SpawnPlacable : Placable
{
    private int _mobId = 1002;
    public int MobId { get => _mobId; set => SetField(ref _mobId, value); }
    
    private int _count = 1;
    public int Count { get => _count; set => SetField(ref _count, value); }
    
    private int _respawnMin = 0;
    public int RespawnMin { get => _respawnMin; set => SetField(ref _respawnMin, value); }
    
    private int _respawnMax = 0;
    public int RespawnMax { get => _respawnMax; set => SetField(ref _respawnMax, value); }
    
    private int _areaW = 0;
    public int AreaW { get => _areaW; set => SetField(ref _areaW, value); }
    
    private int _areaH = 0;
    public int AreaH { get => _areaH; set => SetField(ref _areaH, value); }
    
    private bool _isMvp = false;
    public bool IsMvp { get => _isMvp; set => SetField(ref _isMvp, value); }

    public SpawnPlacable()
    {
        Kind = PlacableKind.Spawn;
    }
}

// Database Models (Stubbed)
public class MobEntry
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int Hp { get; set; }
    public string Race { get; set; } = "Formless";
    public string Element { get; set; } = "Neutral";
    public string Size { get; set; } = "Small";
    public int BaseExp { get; set; }
    public int JobExp { get; set; }
    public bool IsMvp { get; set; }
}

// Needed for polymorphic JSON
[JsonSerializable(typeof(ProjectData))]
[JsonSerializable(typeof(NpcPlacable))]
[JsonSerializable(typeof(WarpPlacable))]
[JsonSerializable(typeof(SpawnPlacable))]
internal partial class ProjectJsonContext : JsonSerializerContext { }
