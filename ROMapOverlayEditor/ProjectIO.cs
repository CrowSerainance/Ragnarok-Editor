using System.IO;
using System.Text.Json;

namespace ROMapOverlayEditor;

public static class ProjectIO
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        TypeInfoResolver = ProjectJsonContext.Default
    };

    public static void Save(string filePath, ProjectData data)
    {
        File.WriteAllText(filePath, ToJson(data));
    }

    /// <summary>Serialize ProjectData to JSON for undo snapshots.</summary>
    public static string ToJson(ProjectData data)
    {
        return JsonSerializer.Serialize(data, Options);
    }

    /// <summary>Deserialize ProjectData from JSON (undo/redo).</summary>
    public static ProjectData FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseRoot(doc.RootElement);
    }

    public static ProjectData Load(string filePath)
    {
        return FromJson(File.ReadAllText(filePath));
    }

    private static ProjectData ParseRoot(JsonElement root)
    {
        var data = new ProjectData
        {
            Version = root.TryGetProperty("Version", out var v) ? v.GetInt32() : 1,
            ProjectName = root.GetProperty("ProjectName").GetString() ?? "Project",
            BaseFolderPath = root.TryGetProperty("BaseFolderPath", out var bf) ? bf.GetString() : null,
            MapName = root.GetProperty("MapName").GetString() ?? "prontera",
            BackgroundImagePath = root.TryGetProperty("BackgroundImagePath", out var bg) ? bg.GetString() : null,
            GrfFilePath = root.TryGetProperty("GrfFilePath", out var gf) ? gf.GetString() : null,
            GrfInternalPath = root.TryGetProperty("GrfInternalPath", out var gi) ? gi.GetString() : null,
            LuaDataFolderPath = root.TryGetProperty("LuaDataFolderPath", out var lf) ? lf.GetString() : null,
            LastSelectedTown = root.TryGetProperty("LastSelectedTown", out var ls) ? ls.GetString() : null,
            PixelsPerTile = root.TryGetProperty("PixelsPerTile", out var ppt) ? ppt.GetDouble() : 8.0,
            OriginBottomLeft = root.TryGetProperty("OriginBottomLeft", out var obl) && obl.GetBoolean(),
            ExportScriptsPath = root.TryGetProperty("ExportScriptsPath", out var es) ? es.GetString() : null,
            ExportDbPatchPath = root.TryGetProperty("ExportDbPatchPath", out var ed) ? ed.GetString() : null,
            MobDbSourcePath = root.TryGetProperty("MobDbSourcePath", out var md) ? md.GetString() : null,
            MobSkillDbSourcePath = root.TryGetProperty("MobSkillDbSourcePath", out var ms) ? ms.GetString() : null
        };

        if (root.TryGetProperty("Items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var it in items.EnumerateArray())
            {
                var kindStr = it.GetProperty("Kind").GetString() ?? "Npc";
                if (!Enum.TryParse<PlacableKind>(kindStr, out var kind))
                    kind = PlacableKind.Npc;

                Placable obj = kind switch
                {
                    PlacableKind.Warp => new WarpPlacable(),
                    PlacableKind.Spawn => new SpawnPlacable(),
                    _ => new NpcPlacable()
                };

                obj.Id = it.TryGetProperty("Id", out var idEl) && Guid.TryParse(idEl.GetString(), out var gid) ? gid : Guid.NewGuid();
                obj.Kind = kind;
                obj.Label = it.TryGetProperty("Label", out var lab) ? lab.GetString() ?? "" : "";
                obj.MapName = it.TryGetProperty("MapName", out var mn) ? mn.GetString() ?? "" : "";
                obj.X = it.TryGetProperty("X", out var x) ? x.GetInt32() : 0;
                obj.Y = it.TryGetProperty("Y", out var y) ? y.GetInt32() : 0;
                obj.Dir = it.TryGetProperty("Dir", out var d) ? d.GetInt32() : 0;

                if (obj is NpcPlacable npc)
                {
                    npc.Sprite = it.TryGetProperty("Sprite", out var sp) ? sp.GetString() ?? npc.Sprite : npc.Sprite;
                    npc.ScriptName = it.TryGetProperty("ScriptName", out var sn) ? sn.GetString() ?? npc.ScriptName : npc.ScriptName;
                    npc.ScriptBody = it.TryGetProperty("ScriptBody", out var sb) ? sb.GetString() ?? npc.ScriptBody : npc.ScriptBody;
                }
                else if (obj is WarpPlacable wp)
                {
                    wp.WarpName = it.TryGetProperty("WarpName", out var wn) ? wn.GetString() ?? wp.WarpName : wp.WarpName;
                    wp.W = it.TryGetProperty("W", out var w) ? w.GetInt32() : wp.W;
                    wp.H = it.TryGetProperty("H", out var h) ? h.GetInt32() : wp.H;
                    wp.DestMap = it.TryGetProperty("DestMap", out var dm) ? dm.GetString() ?? wp.DestMap : wp.DestMap;
                    wp.DestX = it.TryGetProperty("DestX", out var dx) ? dx.GetInt32() : wp.DestX;
                    wp.DestY = it.TryGetProperty("DestY", out var dy) ? dy.GetInt32() : wp.DestY;
                }
                else if (obj is SpawnPlacable sp)
                {
                    sp.MobId = it.TryGetProperty("MobId", out var mi) ? mi.GetInt32() : 1002;
                    sp.Count = it.TryGetProperty("Count", out var cnt) ? cnt.GetInt32() : 1;
                    sp.RespawnMin = it.TryGetProperty("RespawnMin", out var rmin) ? rmin.GetInt32() : 0;
                    sp.RespawnMax = it.TryGetProperty("RespawnMax", out var rmax) ? rmax.GetInt32() : 0;
                    sp.AreaW = it.TryGetProperty("AreaW", out var aw) ? aw.GetInt32() : 0;
                    sp.AreaH = it.TryGetProperty("AreaH", out var ah) ? ah.GetInt32() : 0;
                    sp.IsMvp = it.TryGetProperty("IsMvp", out var mvp) && mvp.GetBoolean();
                }

                data.Items.Add(obj);
            }
        }

        return data;
    }
}
