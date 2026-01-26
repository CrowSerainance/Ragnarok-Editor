// ============================================================================
// BrowEditCoordinates.cs - Coordinate System Utilities
// ============================================================================
// PURPOSE: Coordinate transformations matching BrowEdit3's conventions
// INTEGRATION: Drop into ROMapOverlayEditor/ThreeD/ folder
// NOTES: RO uses a unique coordinate system that differs from standard 3D engines
// ============================================================================

using System;
using System.Numerics;

namespace ROMapOverlayEditor.ThreeD
{
    /// <summary>
    /// Coordinate system conversion utilities for Ragnarok Online maps.
    /// 
    /// RO COORDINATE SYSTEM:
    /// - X: East-West (positive = East)
    /// - Y: Height (positive = UP in world, but stored NEGATIVE in files)
    /// - Z: North-South (positive = South)
    /// - Origin: Northwest corner of map at ground level
    /// - Tile size: 10 units (configurable via GND.TileScale)
    /// 
    /// BROWEDIT CONVENTIONS:
    /// - Heights are negated from file values for display
    /// - Light longitude/latitude use degrees
    /// - Model rotations are in degrees (not radians)
    /// </summary>
    public static class BrowEditCoordinates
    {
        // ====================================================================
        // CONSTANTS
        // ====================================================================
        
        /// <summary>Default tile size in world units</summary>
        public const float TILE_SIZE = 10f;
        
        /// <summary>Degrees to radians conversion factor</summary>
        public const float DEG_TO_RAD = MathF.PI / 180f;
        
        /// <summary>Radians to degrees conversion factor</summary>
        public const float RAD_TO_DEG = 180f / MathF.PI;
        
        // ====================================================================
        // TILE <-> WORLD CONVERSIONS
        // ====================================================================
        
        /// <summary>
        /// Convert tile coordinates to world position.
        /// </summary>
        /// <param name="tileX">Tile X coordinate (0 to width-1)</param>
        /// <param name="tileY">Tile Y coordinate (0 to height-1)</param>
        /// <param name="tileScale">Tile scale (default 10)</param>
        /// <returns>World position at tile center</returns>
        public static Vector3 TileToWorld(int tileX, int tileY, float tileScale = TILE_SIZE)
        {
            return new Vector3(
                (tileX + 0.5f) * tileScale,  // Center of tile
                0,                            // Ground level
                (tileY + 0.5f) * tileScale   // Center of tile
            );
        }
        
        /// <summary>
        /// Convert tile coordinates with height to world position.
        /// </summary>
        public static Vector3 TileToWorld(int tileX, int tileY, float height, float tileScale = TILE_SIZE)
        {
            return new Vector3(
                (tileX + 0.5f) * tileScale,
                -height,  // Negate height (file stores negative)
                (tileY + 0.5f) * tileScale
            );
        }
        
        /// <summary>
        /// Convert world position to tile coordinates.
        /// </summary>
        /// <param name="worldPos">World position</param>
        /// <param name="tileScale">Tile scale</param>
        /// <returns>Tile coordinates (may be fractional)</returns>
        public static (float tileX, float tileY) WorldToTile(Vector3 worldPos, float tileScale = TILE_SIZE)
        {
            return (
                worldPos.X / tileScale,
                worldPos.Z / tileScale
            );
        }
        
        /// <summary>
        /// Convert world position to integer tile coordinates (floored).
        /// </summary>
        public static (int tileX, int tileY) WorldToTileInt(Vector3 worldPos, float tileScale = TILE_SIZE)
        {
            return (
                (int)MathF.Floor(worldPos.X / tileScale),
                (int)MathF.Floor(worldPos.Z / tileScale)
            );
        }
        
        // ====================================================================
        // HEIGHT CONVERSIONS
        // ====================================================================
        
        /// <summary>
        /// Convert GND file height to world Y coordinate.
        /// GND stores heights as negative values (deeper = more negative).
        /// </summary>
        public static float GndHeightToWorld(float gndHeight)
        {
            return -gndHeight;
        }
        
        /// <summary>
        /// Convert world Y coordinate to GND file height.
        /// </summary>
        public static float WorldToGndHeight(float worldY)
        {
            return -worldY;
        }
        
        // ====================================================================
        // RSW OBJECT POSITION CONVERSION
        // ====================================================================
        
        /// <summary>
        /// Convert RSW object position to world position.
        /// RSW positions are already in world space but may need axis adjustment.
        /// </summary>
        /// <param name="rswPos">Position from RSW file</param>
        /// <param name="mapWidth">Map width in tiles</param>
        /// <param name="mapHeight">Map height in tiles</param>
        /// <param name="tileScale">Tile scale</param>
        /// <returns>World position</returns>
        public static Vector3 RswToWorld(
            Vector3 rswPos, 
            int mapWidth, 
            int mapHeight, 
            float tileScale = TILE_SIZE)
        {
            // RSW positions are offset from map center
            float centerX = mapWidth * tileScale * 0.5f;
            float centerZ = mapHeight * tileScale * 0.5f;
            
            return new Vector3(
                rswPos.X + centerX,
                -rswPos.Y,  // Y is negated
                rswPos.Z + centerZ
            );
        }
        
        /// <summary>
        /// Convert world position to RSW object position.
        /// </summary>
        public static Vector3 WorldToRsw(
            Vector3 worldPos,
            int mapWidth,
            int mapHeight,
            float tileScale = TILE_SIZE)
        {
            float centerX = mapWidth * tileScale * 0.5f;
            float centerZ = mapHeight * tileScale * 0.5f;
            
            return new Vector3(
                worldPos.X - centerX,
                -worldPos.Y,
                worldPos.Z - centerZ
            );
        }
        
        // ====================================================================
        // ROTATION CONVERSIONS
        // ====================================================================
        
        /// <summary>
        /// Convert RSW rotation (degrees) to quaternion.
        /// RSW uses Euler angles in degrees with specific axis order.
        /// </summary>
        public static Quaternion RswRotationToQuaternion(Vector3 rotationDegrees)
        {
            // RSW rotation order: Y (yaw) -> X (pitch) -> Z (roll)
            float yaw = rotationDegrees.Y * DEG_TO_RAD;
            float pitch = rotationDegrees.X * DEG_TO_RAD;
            float roll = rotationDegrees.Z * DEG_TO_RAD;
            
            return Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll);
        }
        
        /// <summary>
        /// Convert quaternion to RSW rotation (degrees).
        /// </summary>
        public static Vector3 QuaternionToRswRotation(Quaternion q)
        {
            // Extract Euler angles
            var angles = QuaternionToEuler(q);
            
            return new Vector3(
                angles.X * RAD_TO_DEG,
                angles.Y * RAD_TO_DEG,
                angles.Z * RAD_TO_DEG
            );
        }
        
        /// <summary>
        /// Extract Euler angles (radians) from quaternion.
        /// </summary>
        private static Vector3 QuaternionToEuler(Quaternion q)
        {
            // Roll (X)
            float sinr_cosp = 2 * (q.W * q.X + q.Y * q.Z);
            float cosr_cosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
            float roll = MathF.Atan2(sinr_cosp, cosr_cosp);
            
            // Pitch (Y)
            float sinp = 2 * (q.W * q.Y - q.Z * q.X);
            float pitch;
            if (MathF.Abs(sinp) >= 1)
                pitch = MathF.CopySign(MathF.PI / 2, sinp);
            else
                pitch = MathF.Asin(sinp);
            
            // Yaw (Z)
            float siny_cosp = 2 * (q.W * q.Z + q.X * q.Y);
            float cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
            float yaw = MathF.Atan2(siny_cosp, cosy_cosp);
            
            return new Vector3(roll, pitch, yaw);
        }
        
        // ====================================================================
        // LIGHT DIRECTION
        // ====================================================================
        
        /// <summary>
        /// Calculate light direction vector from longitude/latitude.
        /// Uses BrowEdit3's lighting convention.
        /// </summary>
        /// <param name="longitude">Longitude in degrees (0-360)</param>
        /// <param name="latitude">Latitude in degrees (0-90)</param>
        /// <returns>Normalized light direction vector</returns>
        public static Vector3 LightDirection(float longitude, float latitude)
        {
            float lonRad = longitude * DEG_TO_RAD;
            float latRad = latitude * DEG_TO_RAD;
            
            float cosLat = MathF.Cos(latRad);
            
            return Vector3.Normalize(new Vector3(
                cosLat * MathF.Sin(lonRad),
                MathF.Sin(latRad),
                cosLat * MathF.Cos(lonRad)
            ));
        }
        
        // ====================================================================
        // BOUNDING BOX CALCULATIONS
        // ====================================================================
        
        /// <summary>
        /// Calculate world-space bounding box for a map.
        /// </summary>
        public static (Vector3 min, Vector3 max) MapBounds(
            int width, 
            int height, 
            float minHeight, 
            float maxHeight,
            float tileScale = TILE_SIZE)
        {
            return (
                new Vector3(0, -maxHeight, 0),
                new Vector3(width * tileScale, -minHeight, height * tileScale)
            );
        }
        
        /// <summary>
        /// Calculate map center point.
        /// </summary>
        public static Vector3 MapCenter(int width, int height, float tileScale = TILE_SIZE)
        {
            return new Vector3(
                width * tileScale * 0.5f,
                0,
                height * tileScale * 0.5f
            );
        }
    }
    
    // ========================================================================
    // CAMERA CONTROLLER
    // ========================================================================
    
    /// <summary>
    /// BrowEdit-style orbit camera controller.
    /// Orbits around a target point with pan, zoom, and rotation.
    /// </summary>
    public class BrowEditCameraV2
    {
        // ====================================================================
        // STATE
        // ====================================================================
        
        /// <summary>Point the camera orbits around</summary>
        public Vector3 Target { get; set; }
        
        /// <summary>Distance from target</summary>
        public float Distance { get; set; } = 500f;
        
        /// <summary>Horizontal rotation in degrees (yaw)</summary>
        public float Yaw { get; set; } = 45f;
        
        /// <summary>Vertical rotation in degrees (pitch)</summary>
        public float Pitch { get; set; } = 45f;
        
        /// <summary>Minimum pitch angle</summary>
        public float MinPitch { get; set; } = 5f;
        
        /// <summary>Maximum pitch angle</summary>
        public float MaxPitch { get; set; } = 89f;
        
        /// <summary>Minimum zoom distance</summary>
        public float MinDistance { get; set; } = 10f;
        
        /// <summary>Maximum zoom distance</summary>
        public float MaxDistance { get; set; } = 5000f;
        
        /// <summary>Field of view in degrees</summary>
        public float FieldOfView { get; set; } = 45f;
        
        /// <summary>Near clip plane</summary>
        public float NearPlane { get; set; } = 1f;
        
        /// <summary>Far clip plane</summary>
        public float FarPlane { get; set; } = 10000f;
        
        // ====================================================================
        // COMPUTED PROPERTIES
        // ====================================================================
        
        /// <summary>Current camera position</summary>
        public Vector3 Position
        {
            get
            {
                float yawRad = Yaw * BrowEditCoordinates.DEG_TO_RAD;
                float pitchRad = Pitch * BrowEditCoordinates.DEG_TO_RAD;
                
                float cosPitch = MathF.Cos(pitchRad);
                
                return Target + new Vector3(
                    Distance * cosPitch * MathF.Sin(yawRad),
                    Distance * MathF.Sin(pitchRad),
                    Distance * cosPitch * MathF.Cos(yawRad)
                );
            }
        }
        
        /// <summary>Camera forward direction</summary>
        public Vector3 Forward => Vector3.Normalize(Target - Position);
        
        /// <summary>Camera right direction</summary>
        public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitY));
        
        /// <summary>Camera up direction</summary>
        public Vector3 Up => Vector3.Cross(Right, Forward);
        
        // ====================================================================
        // MATRIX GENERATION
        // ====================================================================
        
        /// <summary>Generate view matrix</summary>
        public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitY);
        
        /// <summary>Generate projection matrix</summary>
        public Matrix4x4 ProjectionMatrix(float aspectRatio)
        {
            return Matrix4x4.CreatePerspectiveFieldOfView(
                FieldOfView * BrowEditCoordinates.DEG_TO_RAD,
                aspectRatio,
                NearPlane,
                FarPlane
            );
        }
        
        /// <summary>Generate combined view-projection matrix</summary>
        public Matrix4x4 ViewProjectionMatrix(float aspectRatio)
        {
            return ViewMatrix * ProjectionMatrix(aspectRatio);
        }
        
        // ====================================================================
        // CONTROLS
        // ====================================================================
        
        /// <summary>Rotate camera by delta angles</summary>
        public void Rotate(float deltaYaw, float deltaPitch)
        {
            Yaw += deltaYaw;
            Pitch = Math.Clamp(Pitch + deltaPitch, MinPitch, MaxPitch);
            
            // Normalize yaw to 0-360
            while (Yaw < 0) Yaw += 360;
            while (Yaw >= 360) Yaw -= 360;
        }
        
        /// <summary>Zoom camera by delta distance</summary>
        public void Zoom(float delta)
        {
            Distance = Math.Clamp(Distance + delta, MinDistance, MaxDistance);
        }
        
        /// <summary>Zoom camera by factor (1.0 = no change)</summary>
        public void ZoomFactor(float factor)
        {
            Distance = Math.Clamp(Distance * factor, MinDistance, MaxDistance);
        }
        
        /// <summary>Pan camera target position</summary>
        public void Pan(float deltaX, float deltaZ)
        {
            // Pan in camera-relative directions
            var right = Right;
            var forward = Vector3.Normalize(new Vector3(Forward.X, 0, Forward.Z));
            
            Target += right * deltaX + forward * deltaZ;
        }
        
        /// <summary>Pan camera target position (world-space)</summary>
        public void PanWorld(Vector3 delta)
        {
            Target += delta;
        }
        
        /// <summary>Focus on a specific point</summary>
        public void FocusOn(Vector3 point, float? newDistance = null)
        {
            Target = point;
            if (newDistance.HasValue)
                Distance = Math.Clamp(newDistance.Value, MinDistance, MaxDistance);
        }
        
        /// <summary>Focus on map center</summary>
        public void FocusOnMap(int mapWidth, int mapHeight, float tileScale = BrowEditCoordinates.TILE_SIZE)
        {
            Target = BrowEditCoordinates.MapCenter(mapWidth, mapHeight, tileScale);
            Distance = Math.Max(mapWidth, mapHeight) * tileScale * 0.8f;
        }
        
        // ====================================================================
        // RAYCASTING
        // ====================================================================
        
        /// <summary>
        /// Get ray from camera through screen point.
        /// </summary>
        /// <param name="screenX">Normalized screen X (-1 to 1)</param>
        /// <param name="screenY">Normalized screen Y (-1 to 1)</param>
        /// <param name="aspectRatio">Viewport aspect ratio</param>
        /// <returns>Ray origin and direction</returns>
        public (Vector3 origin, Vector3 direction) ScreenPointToRay(
            float screenX, 
            float screenY, 
            float aspectRatio)
        {
            // Unproject from screen to world
            var invViewProj = ViewProjectionMatrix(aspectRatio);
            Matrix4x4.Invert(invViewProj, out var inv);
            
            var nearPoint = Vector4.Transform(new Vector4(screenX, screenY, 0, 1), inv);
            var farPoint = Vector4.Transform(new Vector4(screenX, screenY, 1, 1), inv);
            
            nearPoint /= nearPoint.W;
            farPoint /= farPoint.W;
            
            var direction = Vector3.Normalize(
                new Vector3(farPoint.X, farPoint.Y, farPoint.Z) - 
                new Vector3(nearPoint.X, nearPoint.Y, nearPoint.Z)
            );
            
            return (Position, direction);
        }
    }
}
