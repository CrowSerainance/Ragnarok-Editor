using ROMapOverlayEditor.Tools;

namespace ROMapOverlayEditor
{
    public sealed class EditorState
    {
        // Singleton-style editor state
        public static EditorState Current { get; } = new();

        private EditorState() { }

        public EditorTool ActiveTool { get; set; } = EditorTool.Select;

        // Common flags
        public bool SnapToGrid { get; set; } = true;
        public bool ShowGrid { get; set; } = true;
        public bool ShowLabels { get; set; } = true;

        // Sensitivity
        public double RotateSensitivity { get; set; } = 1.0;
        public double PanSensitivity { get; set; } = 1.0;
        public double ZoomSensitivity { get; set; } = 1.0;

        // Modifier state (updated by input layer)
        public bool IsShiftDown { get; set; }
        public bool IsCtrlDown { get; set; }
        public bool IsAltDown { get; set; }
    }
}
