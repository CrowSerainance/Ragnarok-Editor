namespace ROMapOverlayEditor.Tools
{
    public enum EditorTool
    {
        None = 0,

        // Navigation (camera always works regardless of tool)
        OrbitCamera,
        PanCamera,
        ZoomCamera,

        // Selection
        Select,

        // 2D Overlay
        PlaceNpc,
        PlaceWarp,
        MoveObject,
        DeleteObject,

        // 3D / Terrain
        PaintGat_Walkable,
        PaintGat_NotWalkable,
        PaintGat_Water,

        // Utility
        Measure,
    }
}
