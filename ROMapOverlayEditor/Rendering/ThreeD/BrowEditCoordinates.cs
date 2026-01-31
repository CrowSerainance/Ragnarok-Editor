// ============================================================================
// BrowEditCoordinates.cs - Coordinate System Utilities
// ============================================================================
// PURPOSE: Coordinate transformations matching BrowEdit3's conventions
// INTEGRATION: Drop into ROMapOverlayEditor/ThreeD/ folder
// NOTES: RO uses a unique coordinate system that differs from standard 3D engines
// ============================================================================

using System;
using System.Windows.Media.Media3D;

namespace ROMapOverlayEditor.ThreeD
{
    /// <summary>
    /// Synchronizes the RO Coordinate System with WPF/HelixToolkit.
    /// Logic strictly mirrors BrowEdit3's GndRenderer.cpp.
    /// </summary>
    public static class BrowEditCoordinates
    {
        public const float DefaultZoom = 10.0f;

        /// <summary>
        /// Converts a grid-space coordinate to a world-space point.
        /// RO uses an inverted Y-axis (height) and a flipped Z-axis for map alignment.
        /// </summary>
        /// <param name="gridX">Tile X index</param>
        /// <param name="altitude">GND height (h1-h4)</param>
        /// <param name="gridY">Tile Y index</param>
        /// <param name="mapHeight">Total height of the map in tiles</param>
        /// <param name="zoom">Scale factor (usually 10.0)</param>
        public static Point3D GridToWorld(int gridX, float altitude, int gridY, int mapHeight, float zoom = DefaultZoom)
        {
            // BrowEdit3: (10 * x, -height, 10 * mapHeight - 10 * y)
            // Note: We add offset +10 to Z for Top-Left vertex alignment vs Bottom-Left.
            double x = gridX * zoom;
            double y = -altitude;
            double z = (mapHeight - gridY) * zoom;
            return new Point3D(x, y, z);
        }

        /// <summary>
        /// Converts RSM object placement coordinates to WPF.
        /// RSM positions are relative to the map center in RO world units.
        /// </summary>
        public static Point3D RsmToWorld(Vector3D rsmPos, int mapWidth, int mapHeight)
        {
            // BrowEdit3: gnd->width * 5.0f + rswObject->position.x
            double x = (mapWidth * 5.0) + rsmPos.X;
            double y = -rsmPos.Y;
            double z = (mapHeight * 5.0) - rsmPos.Z; 
            return new Point3D(x, y, z);
        }

        /// <summary>
        /// Convert RSW rotation (Euler angles in degrees) to quaternion.
        /// RSW uses X=pitch, Y=yaw, Z=roll in degrees.
        /// </summary>
        /// <param name="rotationDeg">Rotation in degrees (X=pitch, Y=yaw, Z=roll)</param>
        /// <returns>Quaternion for WPF 3D transforms</returns>
        public static System.Numerics.Quaternion RswRotationToQuaternion(System.Numerics.Vector3 rotationDeg)
        {
            // Convert degrees to radians
            float pitchRad = rotationDeg.X * (MathF.PI / 180f);
            float yawRad = rotationDeg.Y * (MathF.PI / 180f);
            float rollRad = rotationDeg.Z * (MathF.PI / 180f);
            
            // Create quaternion from Euler angles (YXZ order, typical for RO)
            // This matches BrowEdit3's rotation convention
            var qPitch = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitX, pitchRad);
            var qYaw = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, yawRad);
            var qRoll = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, rollRad);
            
            // Combine: Yaw * Pitch * Roll (YXZ order)
            return qYaw * qPitch * qRoll;
        }
    }
}