using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using ROMapOverlayEditor.Gnd;

namespace ROMapOverlayEditor.ThreeD
{
    /// <summary>
    /// BrowEdit3-accurate terrain mesh builder.
    /// Builds Top faces, Side walls (Right), and Front walls with correct UV mapping.
    /// 
    /// COORDINATE SYSTEM (BrowEdit3):
    ///   World X = gridX * tileScale
    ///   World Y = -height (negated)
    ///   World Z = (mapHeight - gridY) * tileScale
    /// 
    /// HEIGHT MAPPING (GND file):
    ///   h1 = Bottom-Left (BL)  → Height01
    ///   h2 = Bottom-Right (BR) → Height11
    ///   h3 = Top-Left (TL)     → Height00
    ///   h4 = Top-Right (TR)    → Height10
    /// 
    /// UV MAPPING (BrowEdit3 GndRenderer.cpp):
    ///   v1 (TL): position(x, -h3, H-y),       UV = tile.v3
    ///   v2 (TR): position(x+10, -h4, H-y),    UV = tile.v4  
    ///   v3 (BL): position(x, -h1, H-y+10),    UV = tile.v1
    ///   v4 (BR): position(x+10, -h2, H-y+10), UV = tile.v2
    ///   Triangles: (v4,v2,v1), (v4,v1,v3)
    /// </summary>
    public static class UnifiedTerrainBuilder
    {
        /// <summary>
        /// Builds terrain mesh partitioned by texture ID.
        /// </summary>
        /// <param name="gnd">Parsed GND file data</param>
        /// <param name="getMaterial">Function to create material for a texture index</param>
        /// <returns>List of GeometryModel3D, one per texture</returns>
        public static List<GeometryModel3D> Build(GndFileV2 gnd, Func<int, Material> getMaterial)
        {
            // Dictionary to accumulate vertices per texture
            var perTextureMeshes = new Dictionary<int, MeshGeometry3D>();
            
            int mapW = gnd.Width;
            int mapH = gnd.Height;
            float zoom = gnd.TileScale; // Usually 10.0

            // Iterate all cubes in the terrain grid
            for (int y = 0; y < mapH; y++)
            {
                for (int x = 0; x < mapW; x++)
                {
                    var cube = gnd.Cubes[x, y];

                    // ================================================================
                    // 1. TOP FACE (Main terrain surface)
                    // ================================================================
                    if (cube.TileUp >= 0 && cube.TileUp < gnd.Surfaces.Count)
                    {
                        var surf = gnd.Surfaces[cube.TileUp];
                        if (surf.TextureIndex >= 0)
                        {
                            // BrowEdit3 position formula:
                            // v1(TL): (10*x, -h3, 10*H - 10*y)
                            // v2(TR): (10*x+10, -h4, 10*H - 10*y)
                            // v3(BL): (10*x, -h1, 10*H - 10*y + 10)
                            // v4(BR): (10*x+10, -h2, 10*H - 10*y + 10)
                            
                            // Height mapping: h3=Height00(TL), h4=Height10(TR), h1=Height01(BL), h2=Height11(BR)
                            var p1_TL = new Point3D(zoom * x,       -cube.Height00, zoom * mapH - zoom * y);
                            var p2_TR = new Point3D(zoom * x + zoom, -cube.Height10, zoom * mapH - zoom * y);
                            var p3_BL = new Point3D(zoom * x,       -cube.Height01, zoom * mapH - zoom * y + zoom);
                            var p4_BR = new Point3D(zoom * x + zoom, -cube.Height11, zoom * mapH - zoom * y + zoom);

                            // UV mapping from BrowEdit3:
                            // v1(TL) → tile.v3 (U3,V3)
                            // v2(TR) → tile.v4 (U4,V4)
                            // v3(BL) → tile.v1 (U1,V1)
                            // v4(BR) → tile.v2 (U2,V2)
                            var uv1_TL = new Point(surf.U3, surf.V3);
                            var uv2_TR = new Point(surf.U4, surf.V4);
                            var uv3_BL = new Point(surf.U1, surf.V1);
                            var uv4_BR = new Point(surf.U2, surf.V2);

                            AddQuad(perTextureMeshes, surf.TextureIndex,
                                p1_TL, p2_TR, p3_BL, p4_BR,
                                uv1_TL, uv2_TR, uv3_BL, uv4_BR);
                        }
                    }

                    // ================================================================
                    // 2. SIDE WALL (Right edge, connecting to x+1)
                    // ================================================================
                    if (cube.TileSide >= 0 && x < mapW - 1 && cube.TileSide < gnd.Surfaces.Count)
                    {
                        var surf = gnd.Surfaces[cube.TileSide];
                        if (surf.TextureIndex >= 0)
                        {
                            var nextCube = gnd.Cubes[x + 1, y];
                            
                            // BrowEdit3 side wall vertices (GndRenderer.cpp ~390):
                            // v1(up-front):   (x+10, -h2, H-y+10)  → tile.v2
                            // v2(up-back):    (x+10, -h4, H-y)     → tile.v1
                            // v3(down-front): (x+10, -next.h1, H-y+10) → tile.v4
                            // v4(down-back):  (x+10, -next.h3, H-y)    → tile.v3
                            
                            var p1 = new Point3D(zoom * x + zoom, -cube.Height11, zoom * mapH - zoom * y + zoom);
                            var p2 = new Point3D(zoom * x + zoom, -cube.Height10, zoom * mapH - zoom * y);
                            var p3 = new Point3D(zoom * x + zoom, -nextCube.Height01, zoom * mapH - zoom * y + zoom);
                            var p4 = new Point3D(zoom * x + zoom, -nextCube.Height00, zoom * mapH - zoom * y);

                            var uv1 = new Point(surf.U2, surf.V2);
                            var uv2 = new Point(surf.U1, surf.V1);
                            var uv3 = new Point(surf.U4, surf.V4);
                            var uv4 = new Point(surf.U3, surf.V3);

                            AddWallQuad(perTextureMeshes, surf.TextureIndex,
                                p1, p2, p3, p4, uv1, uv2, uv3, uv4);
                        }
                    }

                    // ================================================================
                    // 3. FRONT WALL (Bottom edge, connecting to y+1)
                    // ================================================================
                    if (cube.TileFront >= 0 && y < mapH - 1 && cube.TileFront < gnd.Surfaces.Count)
                    {
                        var surf = gnd.Surfaces[cube.TileFront];
                        if (surf.TextureIndex >= 0)
                        {
                            var nextCube = gnd.Cubes[x, y + 1];
                            
                            // BrowEdit3 front wall vertices (GndRenderer.cpp ~420):
                            // v1: (x, -h3, H-y)        → tile.v1
                            // v2: (x+10, -h4, H-y)     → tile.v2
                            // v3: (x, -next.h1, H-y)   → tile.v3
                            // v4: (x+10, -next.h2, H-y) → tile.v4
                            
                            var p1 = new Point3D(zoom * x, -cube.Height00, zoom * mapH - zoom * y);
                            var p2 = new Point3D(zoom * x + zoom, -cube.Height10, zoom * mapH - zoom * y);
                            var p3 = new Point3D(zoom * x, -nextCube.Height01, zoom * mapH - zoom * y);
                            var p4 = new Point3D(zoom * x + zoom, -nextCube.Height11, zoom * mapH - zoom * y);

                            var uv1 = new Point(surf.U1, surf.V1);
                            var uv2 = new Point(surf.U2, surf.V2);
                            var uv3 = new Point(surf.U3, surf.V3);
                            var uv4 = new Point(surf.U4, surf.V4);

                            AddFrontWallQuad(perTextureMeshes, surf.TextureIndex,
                                p1, p2, p3, p4, uv1, uv2, uv3, uv4);
                        }
                    }
                }
            }

            // Convert mesh dictionary to list of GeometryModel3D
            var results = new List<GeometryModel3D>();
            foreach (var kvp in perTextureMeshes)
            {
                var mesh = kvp.Value;
                mesh.Freeze(); // Optimize for rendering
                
                var mat = getMaterial(kvp.Key);
                var model = new GeometryModel3D(mesh, mat)
                {
                    BackMaterial = mat // Render both sides
                };
                model.Freeze();
                results.Add(model);
            }
            
            return results;
        }

        /// <summary>
        /// Adds a top face quad with BrowEdit3 triangle winding: (v4,v2,v1), (v4,v1,v3)
        /// </summary>
        private static void AddQuad(
            Dictionary<int, MeshGeometry3D> meshes, int texId,
            Point3D p1_TL, Point3D p2_TR, Point3D p3_BL, Point3D p4_BR,
            Point uv1_TL, Point uv2_TR, Point uv3_BL, Point uv4_BR)
        {
            if (!meshes.TryGetValue(texId, out var mesh))
            {
                mesh = new MeshGeometry3D();
                meshes[texId] = mesh;
            }

            int baseIdx = mesh.Positions.Count;

            // Add vertices in order: v1(TL), v2(TR), v3(BL), v4(BR)
            mesh.Positions.Add(p1_TL);  // index 0
            mesh.Positions.Add(p2_TR);  // index 1
            mesh.Positions.Add(p3_BL);  // index 2
            mesh.Positions.Add(p4_BR);  // index 3

            mesh.TextureCoordinates.Add(uv1_TL);
            mesh.TextureCoordinates.Add(uv2_TR);
            mesh.TextureCoordinates.Add(uv3_BL);
            mesh.TextureCoordinates.Add(uv4_BR);

            // BrowEdit3 triangle winding: (v4,v2,v1), (v4,v1,v3)
            // v4=index3, v2=index1, v1=index0, v3=index2
            
            // Triangle 1: v4, v2, v1 → indices 3, 1, 0
            mesh.TriangleIndices.Add(baseIdx + 3);
            mesh.TriangleIndices.Add(baseIdx + 1);
            mesh.TriangleIndices.Add(baseIdx + 0);

            // Triangle 2: v4, v1, v3 → indices 3, 0, 2
            mesh.TriangleIndices.Add(baseIdx + 3);
            mesh.TriangleIndices.Add(baseIdx + 0);
            mesh.TriangleIndices.Add(baseIdx + 2);
        }

        /// <summary>
        /// Adds a side wall quad with BrowEdit3 winding: (v1,v3,v4), (v4,v2,v1)
        /// </summary>
        private static void AddWallQuad(
            Dictionary<int, MeshGeometry3D> meshes, int texId,
            Point3D p1, Point3D p2, Point3D p3, Point3D p4,
            Point uv1, Point uv2, Point uv3, Point uv4)
        {
            if (!meshes.TryGetValue(texId, out var mesh))
            {
                mesh = new MeshGeometry3D();
                meshes[texId] = mesh;
            }

            int baseIdx = mesh.Positions.Count;

            mesh.Positions.Add(p1);  // 0
            mesh.Positions.Add(p2);  // 1
            mesh.Positions.Add(p3);  // 2
            mesh.Positions.Add(p4);  // 3

            mesh.TextureCoordinates.Add(uv1);
            mesh.TextureCoordinates.Add(uv2);
            mesh.TextureCoordinates.Add(uv3);
            mesh.TextureCoordinates.Add(uv4);

            // BrowEdit3: (v1,v3,v4), (v4,v2,v1)
            mesh.TriangleIndices.Add(baseIdx + 0);
            mesh.TriangleIndices.Add(baseIdx + 2);
            mesh.TriangleIndices.Add(baseIdx + 3);

            mesh.TriangleIndices.Add(baseIdx + 3);
            mesh.TriangleIndices.Add(baseIdx + 1);
            mesh.TriangleIndices.Add(baseIdx + 0);
        }

        /// <summary>
        /// Adds a front wall quad with BrowEdit3 winding: (v1,v2,v3), (v3,v2,v4)
        /// </summary>
        private static void AddFrontWallQuad(
            Dictionary<int, MeshGeometry3D> meshes, int texId,
            Point3D p1, Point3D p2, Point3D p3, Point3D p4,
            Point uv1, Point uv2, Point uv3, Point uv4)
        {
            if (!meshes.TryGetValue(texId, out var mesh))
            {
                mesh = new MeshGeometry3D();
                meshes[texId] = mesh;
            }

            int baseIdx = mesh.Positions.Count;

            mesh.Positions.Add(p1);
            mesh.Positions.Add(p2);
            mesh.Positions.Add(p3);
            mesh.Positions.Add(p4);

            mesh.TextureCoordinates.Add(uv1);
            mesh.TextureCoordinates.Add(uv2);
            mesh.TextureCoordinates.Add(uv3);
            mesh.TextureCoordinates.Add(uv4);

            // BrowEdit3: (v1,v2,v3), (v3,v2,v4)
            mesh.TriangleIndices.Add(baseIdx + 0);
            mesh.TriangleIndices.Add(baseIdx + 1);
            mesh.TriangleIndices.Add(baseIdx + 2);

            mesh.TriangleIndices.Add(baseIdx + 2);
            mesh.TriangleIndices.Add(baseIdx + 1);
            mesh.TriangleIndices.Add(baseIdx + 3);
        }
    }
}