// =============================================================================
// BrowEditCoordinates.cs - Coordinate System Transforms for Ragnarok Online
// =============================================================================
// This file provides conversion functions between different coordinate systems
// used in Ragnarok Online map data and rendering. The transforms match BrowEdit3's
// behavior to ensure visual compatibility.
//
// COORDINATE SYSTEM OVERVIEW:
// ---------------------------
// Ragnarok Online uses a RIGHT-HANDED coordinate system with:
//   X = East/West (positive = East)
//   Y = Up/Down (INVERTED: negative = higher!)
//   Z = North/South (positive = South in grid terms)
//
// BrowEdit3 World Space Formula:
//   World X = gridX * tileScale (usually 10.0)
//   World Y = -heightValue (heights are negated!)
//   World Z = (mapHeight - gridY) * tileScale (Y-axis flipped)
//
// Reference: BrowEdit3's coordinate handling in various renderer files
// =============================================================================

using System;
using System.Numerics;
using System.Windows.Media.Media3D;

namespace ROMapOverlayEditor.ThreeD
{
    /// <summary>
    /// Static utility class providing coordinate transformations between
    /// grid space, world space, and RSW object space. Matches BrowEdit3's behavior.
    /// </summary>
    /// <remarks>
    /// This class is marked as 'partial' to allow extension in other files if needed.
    /// All methods are static since no instance state is required.
    /// </remarks>
    public static partial class BrowEditCoordinates
    {
        // =====================================================================
        // CONSTANTS
        // =====================================================================
        
        /// <summary>
        /// Default tile scale factor (10.0 world units per tile).
        /// This is the standard scale used in Ragnarok Online maps.
        /// A 100x100 tile map would be 1000x1000 world units.
        /// </summary>
        public const float DefaultZoom = 10.0f;

        // =====================================================================
        // GRID TO WORLD CONVERSION
        // =====================================================================

        /// <summary>
        /// Converts a grid-space coordinate (tile position + height) to a world-space point.
        /// This is the fundamental transform for placing terrain vertices.
        /// </summary>
        /// <param name="gridX">
        /// Tile X index (column) in the terrain grid.
        /// Range: 0 to mapWidth-1
        /// </param>
        /// <param name="altitude">
        /// GND height value at this corner (h1, h2, h3, or h4).
        /// Note: These are stored as positive values but represent depth below Y=0.
        /// </param>
        /// <param name="gridY">
        /// Tile Y index (row) in the terrain grid.
        /// Range: 0 to mapHeight-1
        /// Note: Grid Y increases downward (south), but world Z increases northward.
        /// </param>
        /// <param name="mapHeight">
        /// Total height of the map in tiles (number of rows).
        /// Used to flip the Y-axis for proper world orientation.
        /// </param>
        /// <param name="zoom">
        /// Scale factor for tile-to-world conversion. Default is 10.0.
        /// Larger values = more spread out terrain.
        /// </param>
        /// <returns>
        /// A Point3D in world space (WPF's 3D coordinate system).
        /// </returns>
        /// <remarks>
        /// The transformation formula matches BrowEdit3's GndRenderer.cpp:
        /// 
        /// BrowEdit3 C++ code:
        ///   glm::vec3 p1(10 * x,      -cube->h3, 10 * gnd->height - 10 * y);      // TL
        ///   glm::vec3 p2(10 * x + 10, -cube->h4, 10 * gnd->height - 10 * y);      // TR
        ///   glm::vec3 p3(10 * x,      -cube->h1, 10 * gnd->height - 10 * y + 10); // BL
        ///   glm::vec3 p4(10 * x + 10, -cube->h2, 10 * gnd->height - 10 * y + 10); // BR
        /// 
        /// Key points:
        /// 1. X is straightforward: gridX * scale
        /// 2. Y is NEGATED: heights become depths (negative = higher in world)
        /// 3. Z is FLIPPED: (mapHeight - gridY) inverts the row order
        /// </remarks>
        /// <example>
        /// // Convert corner (5, 10) with height 25.0 on a 256x256 map:
        /// var worldPos = BrowEditCoordinates.GridToWorld(5, 25.0f, 10, 256);
        /// // Result: X=50, Y=-25, Z=2460
        /// </example>
        public static Point3D GridToWorld(int gridX, float altitude, int gridY, int mapHeight, float zoom = DefaultZoom)
        {
            // Calculate world X: simply scale the grid column
            // Grid column 0 = world X 0, column 1 = world X 10, etc.
            double x = gridX * zoom;
            
            // Calculate world Y: NEGATE the height value
            // Ragnarok stores heights as positive numbers, but in world space
            // "higher" terrain should have smaller (more negative) Y values
            double y = -altitude;
            
            // Calculate world Z: flip the row order and scale
            // Grid row 0 should be at the "far" end of the map (high Z)
            // Grid row (mapHeight-1) should be at Z = zoom (near end)
            double z = (mapHeight - gridY) * zoom;
            
            return new Point3D(x, y, z);
        }

        // =====================================================================
        // RSW ROTATION CONVERSION (CRITICAL FOR RSM MODELS)
        // =====================================================================

        /// <summary>
        /// Converts RSW/RSM Euler rotation angles (in degrees) to a quaternion.
        /// This is REQUIRED for positioning 3D models (RSM) on the map.
        /// </summary>
        /// <param name="rotationDegrees">
        /// Euler angles in degrees stored as a Vector3:
        ///   X = Pitch (rotation around X-axis, tilting forward/backward)
        ///   Y = Yaw (rotation around Y-axis, turning left/right)
        ///   Z = Roll (rotation around Z-axis, tilting sideways)
        /// </param>
        /// <returns>
        /// A System.Numerics.Quaternion representing the combined rotation.
        /// Quaternions avoid gimbal lock and interpolate smoothly.
        /// </returns>
        /// <remarks>
        /// RSW/RSM Rotation Order:
        /// -----------------------
        /// BrowEdit3 applies rotations in this order (based on RsmRenderer.cpp):
        ///   1. Z-axis rotation (Roll) - applied first
        ///   2. X-axis rotation (Pitch) - applied second
        ///   3. Y-axis rotation (Yaw) - applied last
        /// 
        /// This matches the "ZXY" Euler convention, which is what
        /// Quaternion.CreateFromYawPitchRoll() produces internally.
        /// 
        /// The method name parameters are a bit confusing:
        ///   CreateFromYawPitchRoll(yaw, pitch, roll) applies in order: roll → pitch → yaw
        /// 
        /// Degree to Radian Conversion:
        /// ----------------------------
        /// Trigonometric functions in C# work with radians, not degrees.
        /// Formula: radians = degrees × (π / 180)
        /// Example: 90° = 90 × (π/180) = π/2 ≈ 1.5708 radians
        /// </remarks>
        /// <example>
        /// // Rotate a model 45° around Y (turn right) and 30° around X (tilt down):
        /// var rotation = new Vector3(30f, 45f, 0f);  // (pitch, yaw, roll)
        /// var quat = BrowEditCoordinates.RswRotationToQuaternion(rotation);
        /// </example>
        public static System.Numerics.Quaternion RswRotationToQuaternion(System.Numerics.Vector3 rotationDegrees)
        {
            // ================================================================
            // STEP 1: Convert degrees to radians
            // ================================================================
            // The trig functions in quaternion creation expect radians.
            // Conversion factor: π/180 ≈ 0.01745329
            
            float degreesToRadians = MathF.PI / 180f;
            
            // Yaw = Y-axis rotation (spinning like a top)
            float yawRad = rotationDegrees.Y * degreesToRadians;
            
            // Pitch = X-axis rotation (nodding head up/down)
            float pitchRad = rotationDegrees.X * degreesToRadians;
            
            // Roll = Z-axis rotation (tilting head side to side)
            float rollRad = rotationDegrees.Z * degreesToRadians;
            
            // ================================================================
            // STEP 2: Create quaternion from Euler angles
            // ================================================================
            // CreateFromYawPitchRoll internally applies rotations as:
            //   Roll (Z) first → Pitch (X) second → Yaw (Y) last
            // This matches BrowEdit3's rotation application order.
            
            return System.Numerics.Quaternion.CreateFromYawPitchRoll(yawRad, pitchRad, rollRad);
        }

        // =====================================================================
        // RSW POSITION CONVERSION
        // =====================================================================

        /// <summary>
        /// Converts an RSW object position to world space coordinates.
        /// RSW positions are stored relative to the map center; this converts
        /// to absolute world coordinates matching the terrain grid.
        /// </summary>
        /// <param name="rswPos">
        /// Position from RSW file:
        ///   X = East/West offset from map center
        ///   Y = Height (will be negated)
        ///   Z = North/South offset from map center
        /// </param>
        /// <param name="mapWidth">
        /// Map width in tiles (from GND file).
        /// Used to calculate the center X offset.
        /// </param>
        /// <param name="mapHeight">
        /// Map height in tiles (from GND file).
        /// Used to calculate the center Z offset.
        /// </param>
        /// <param name="tileScale">
        /// World units per tile. Default is 10.0.
        /// </param>
        /// <returns>
        /// Absolute world position as Vector3.
        /// </returns>
        /// <remarks>
        /// RSW Object Coordinate System:
        /// -----------------------------
        /// Objects in RSW files store their position relative to the map CENTER,
        /// not the corner. The formula from BrowEdit3 is:
        /// 
        ///   World X = (mapWidth * tileScale / 2) + rsw.position.X
        ///   World Y = -rsw.position.Y  (negated for RO's inverted height)
        ///   World Z = (mapHeight * tileScale / 2) + rsw.position.Z
        /// 
        /// For a 256x256 map with tileScale=10:
        ///   Center X = 256 * 10 / 2 = 1280
        ///   Center Z = 256 * 10 / 2 = 1280
        /// 
        /// An object at RSW position (0, 50, 0) would be at world (1280, -50, 1280).
        /// </remarks>
        public static Vector3 RswToWorld(Vector3 rswPos, int mapWidth, int mapHeight, float tileScale = 10f)
        {
            // Calculate the world-space center of the map
            // This is half the total map dimension in world units
            float centerX = mapWidth * tileScale * 0.5f;   // e.g., 256 * 10 * 0.5 = 1280
            float centerZ = mapHeight * tileScale * 0.5f;  // e.g., 256 * 10 * 0.5 = 1280
            
            // Convert RSW position to world position:
            // - Add center offset to X and Z (RSW coords are center-relative)
            // - Negate Y (RO's height convention: positive = lower)
            return new Vector3(
                rswPos.X + centerX,   // Shift from center to absolute X
                -rswPos.Y,            // Invert height (RO convention)
                rswPos.Z + centerZ    // Shift from center to absolute Z
            );
        }

        // =====================================================================
        // ADDITIONAL UTILITY METHODS (for future use)
        // =====================================================================

        /// <summary>
        /// Converts a world position back to approximate grid coordinates.
        /// Useful for determining which tile a world point is over.
        /// </summary>
        /// <param name="worldPos">Position in world space</param>
        /// <param name="mapHeight">Map height in tiles</param>
        /// <param name="zoom">Tile scale (default 10.0)</param>
        /// <returns>Tuple of (gridX, gridY) tile indices</returns>
        public static (int gridX, int gridY) WorldToGrid(Point3D worldPos, int mapHeight, float zoom = DefaultZoom)
        {
            // Reverse the GridToWorld calculations
            int gridX = (int)(worldPos.X / zoom);
            int gridY = mapHeight - (int)(worldPos.Z / zoom);
            return (gridX, gridY);
        }

        /// <summary>
        /// Converts a System.Numerics.Quaternion to WPF's Quaternion type.
        /// Useful when interfacing between System.Numerics math and WPF 3D.
        /// </summary>
        /// <param name="q">System.Numerics quaternion</param>
        /// <returns>Equivalent WPF Media3D quaternion</returns>
        public static System.Windows.Media.Media3D.Quaternion ToWpfQuaternion(System.Numerics.Quaternion q)
        {
            // Both quaternion types use (X, Y, Z, W) component order
            return new System.Windows.Media.Media3D.Quaternion(q.X, q.Y, q.Z, q.W);
        }
    }
}
