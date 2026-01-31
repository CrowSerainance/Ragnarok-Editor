using System.Collections.Generic;

namespace ROMapOverlayEditor.GrfTown
{
    public sealed class TownEntry
    {
        public string Name { get; set; } = "";
        public string SourcePath { get; set; } = ""; // e.g. data\\System\\Towninfo.lub
        public List<TownNpc> Npcs { get; set; } = new();
    }

    public sealed class TownNpc
    {
        public string Name { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public int Type { get; set; } // TYPE field in Towninfo
        public string Sprite { get; set; } = ""; // if present
    }

    public sealed class TownLoadResult
    {
        public bool Ok { get; }
        public string Message { get; }
        public TownEntry? Town { get; }

        private TownLoadResult(bool ok, string msg, TownEntry? town)
        {
            Ok = ok;
            Message = msg;
            Town = town;
        }

        public static TownLoadResult Success(TownEntry town) => new(true, "", town);
        public static TownLoadResult Fail(string msg) => new(false, msg, null);
    }
}
