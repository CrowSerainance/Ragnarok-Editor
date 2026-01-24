using System;
using System.Windows.Input;
using ROMapOverlayEditor.Tools;
using ROMapOverlayEditor.ThreeD;

namespace ROMapOverlayEditor.Input
{
    public sealed class EditorInputRouter
    {
        private readonly BrowEditCameraController _camera;
        private readonly Action _applyCamera;
        private bool _dragging;
        private System.Windows.Point _last;

        public EditorInputRouter(BrowEditCameraController camera, Action applyCamera)
        {
            _camera = camera;
            _applyCamera = applyCamera ?? (() => { });
        }

        public void OnMouseDown(System.Windows.Point p)
        {
            _dragging = true;
            _last = p;
        }

        public void OnMouseUp()
        {
            _dragging = false;
        }

        public void OnMouseMove(System.Windows.Point p, MouseEventArgs e)
        {
            if (!_dragging) return;

            var dx = p.X - _last.X;
            var dy = p.Y - _last.Y;
            _last = p;

            var st = EditorState.Current;
            double slow = st.IsShiftDown ? 0.25 : 1.0;

            // CAMERA CONTROLS (ALWAYS ACTIVE, LIKE BROWEDIT)
            if (e.RightButton == MouseButtonState.Pressed)
            {
                _camera.Orbit(dx * slow, dy * slow, st.RotateSensitivity);
                _applyCamera();
                return;
            }

            if (e.MiddleButton == MouseButtonState.Pressed)
            {
                _camera.Pan(dx * slow, dy * slow, st.PanSensitivity);
                _applyCamera();
                return;
            }

            // TOOL CONTROLS (left drag) — tool-specific hover/drag handled by view
            switch (st.ActiveTool)
            {
                case EditorTool.Select:
                    break;
                case EditorTool.PlaceNpc:
                    break;
                case EditorTool.PaintGat_Walkable:
                case EditorTool.PaintGat_NotWalkable:
                case EditorTool.PaintGat_Water:
                    break;
            }
        }

        public void OnMouseWheel(int delta)
        {
            var st = EditorState.Current;
            _camera.Zoom(delta, st.ZoomSensitivity);
            _applyCamera();
        }
    }
}
