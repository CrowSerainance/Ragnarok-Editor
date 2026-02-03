using System;

namespace ROMapOverlayEditor.ThreeD
{
    public sealed class BrowEditCameraController
    {
        // Camera state
        public double Yaw { get; private set; } = 45;
        public double Pitch { get; private set; } = -45;
        public double Distance { get; set; } = 220;

        public double TargetX { get; set; }
        public double TargetY { get; set; }
        public double TargetZ { get; set; }


        /// <summary>
        /// Multiplier for overall camera movement speed.
        /// </summary>
        public float SpeedMultiplier { get; set; } = 1.0f;

        /// <summary>
        /// Sensitivity for camera rotation (orbit).
        /// </summary>
        public float RotateSensitivity { get; set; } = 1.0f;

        /// <summary>
        /// Sensitivity for camera panning.
        /// </summary>
        public float PanSensitivity { get; set; } = 1.0f;

        /// <summary>
        /// Sensitivity for camera zoom.
        /// </summary>
        public float ZoomSensitivity { get; set; } = 1.0f;

        public void ResetDefault()
        {
            Yaw = 45;
            Pitch = -45;
            Distance = 220;
            TargetX = TargetY = TargetZ = 0;
        }

        public void ResetEvenOut(double suggestedDistance, double tx = 0, double ty = 0, double tz = 0)
        {
            Distance = suggestedDistance;
            TargetX = tx;
            TargetY = ty;
            TargetZ = tz;
            Yaw = 45;
            Pitch = -45;
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

        // Helper methods for setting camera state
        public void SetTarget(double x, double y, double z)
        {
            TargetX = x;
            TargetY = y;
            TargetZ = z;
        }

        public void SetDistance(double distance)
        {
            Distance = distance;
        }

        public void SetYawPitch(double yawDeg, double pitchDeg)
        {
            Yaw = yawDeg;
            Pitch = pitchDeg;
        }

        public void SetState(float tx, float ty, float tz, float distance, float yawDeg, float pitchDeg)
        {
            // These assignments compile because they're inside the class that owns the setters.
            TargetX = tx;
            TargetY = ty;
            TargetZ = tz;
            Distance = distance;
            Yaw = yawDeg;
            Pitch = pitchDeg;
        }
    }
}
