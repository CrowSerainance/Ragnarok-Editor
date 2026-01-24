using System;

namespace ROMapOverlayEditor.ThreeD
{
    public sealed class BrowEditCameraController
    {
        // Camera state
        public double Yaw { get; private set; } = 45;
        public double Pitch { get; private set; } = -45;
        public double Distance { get; private set; } = 220;

        public double TargetX { get; private set; }
        public double TargetY { get; private set; }
        public double TargetZ { get; private set; }

        public void ResetDefault()
        {
            Yaw = 45;
            Pitch = -45;
            Distance = 220;
            TargetX = TargetY = TargetZ = 0;
        }

        public void Orbit(double dx, double dy, double sens)
        {
            Yaw += dx * 0.25 * sens;
            Pitch -= dy * 0.25 * sens;
            Pitch = Math.Clamp(Pitch, -89, 89);
        }

        public void Pan(double dx, double dy, double sens)
        {
            double scale = 0.1 * sens * Math.Max(1, Distance / 200);
            TargetX -= dx * scale;
            TargetZ += dy * scale;
        }

        public void Zoom(double delta, double sens)
        {
            Distance -= delta * 0.05 * sens;
            Distance = Math.Max(5, Distance);
        }
    }
}
