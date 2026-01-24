using System.Numerics;

namespace ROMapOverlayEditor.ThreeD
{
    public sealed class BrowEditViewConfig
    {
        public float FovDegrees { get; set; } = 45f;
        public float CameraMouseSpeed { get; set; } = 1.0f;
        public Vector3 BackgroundColor { get; set; } = new Vector3(0.10f, 0.10f, 0.15f);

        // View toggles
        public bool ViewTextures { get; set; } = true;
        public bool ViewLighting { get; set; } = true;
        public bool ViewGatOverlay { get; set; } = false;       // OPTION: off by default
        public bool ViewWireframe { get; set; } = false;
        public bool ViewObjects { get; set; } = true;

        // Overlay alpha
        public float GatOverlayAlpha { get; set; } = 0.45f;
    }
}
