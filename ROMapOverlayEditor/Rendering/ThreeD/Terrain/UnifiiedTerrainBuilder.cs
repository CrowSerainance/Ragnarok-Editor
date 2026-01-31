using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using ROMapOverlayEditor.Gnd;

namespace ROMapOverlayEditor.ThreeD
{
    /// <summary>
    /// Consolidated Terrain Builder using BrowEdit3 mesh generation logic.
    /// Handles Top faces, Side walls (Right), and Front walls.
    /// </summary>
    public static class UnifiedTerrainBuilder
    {
        /// <summary>
        /// Builds a collection of GeometryModel3D objects partitioned by Texture ID.
        /// </summary>
        public static List<GeometryModel3D> Build(GndFileV2 gnd, Func<int, Material> getMaterial)
        {
            var perTextureMeshes = new Dictionary<int, MeshGeometry3D>();
            int mapW = gnd.Width;
            int mapH = gnd.Height;
            float zoom = gnd.TileScale;

            for (int y = 0; y < mapH; y++)
            {
                for (int x = 0; x < mapW; x++)
                {
                    var cube = gnd.Cubes[x, y];

                    // 1. TOP FACE (Terrain)
                    if (cube.TileUp != -1 && cube.TileUp < gnd.Surfaces.Count)
                    {
                        var surf = gnd.Surfaces[cube.TileUp];
                        AddFace(perTextureMeshes, surf.TextureIndex,
                            BrowEditCoordinates.GridToWorld(x, cube.Height00, y, mapH, zoom),     // TL (h1)
                            BrowEditCoordinates.GridToWorld(x + 1, cube.Height10, y, mapH, zoom), // TR (h2)
                            BrowEditCoordinates.GridToWorld(x, cube.Height01, y + 1, mapH, zoom), // BL (h3)
                            BrowEditCoordinates.GridToWorld(x + 1, cube.Height11, y + 1, mapH, zoom), // BR (h4)
                            surf, false);
                    }

                    // 2. SIDE WALL (Right) - Connects current TR/BR to next tile TL/BL
                    if (cube.TileSide != -1 && x < mapW - 1 && cube.TileSide < gnd.Surfaces.Count)
                    {
                        var surf = gnd.Surfaces[cube.TileSide];
                        var next = gnd.Cubes[x + 1, y];
                        AddFace(perTextureMeshes, surf.TextureIndex,
                            BrowEditCoordinates.GridToWorld(x + 1, cube.Height10, y, mapH, zoom),     // Top Back
                            BrowEditCoordinates.GridToWorld(x + 1, next.Height00, y, mapH, zoom),     // Top Front
                            BrowEditCoordinates.GridToWorld(x + 1, cube.Height11, y + 1, mapH, zoom), // Bottom Back
                            BrowEditCoordinates.GridToWorld(x + 1, next.Height01, y + 1, mapH, zoom), // Bottom Front
                            surf, true);
                    }

                    // 3. FRONT WALL (Front) - Connects current BL/BR to next row TL/TR
                    if (cube.TileFront != -1 && y < mapH - 1 && cube.TileFront < gnd.Surfaces.Count)
                    {
                        var surf = gnd.Surfaces[cube.TileFront];
                        var next = gnd.Cubes[x, y + 1];
                        AddFace(perTextureMeshes, surf.TextureIndex,
                            BrowEditCoordinates.GridToWorld(x, cube.Height01, y + 1, mapH, zoom),     // Top Left
                            BrowEditCoordinates.GridToWorld(x + 1, cube.Height11, y + 1, mapH, zoom), // Top Right
                            BrowEditCoordinates.GridToWorld(x, next.Height00, y + 1, mapH, zoom),     // Bottom Left
                            BrowEditCoordinates.GridToWorld(x + 1, next.Height10, y + 1, mapH, zoom), // Bottom Right
                            surf, true);
                    }
                }
            }

            var results = new List<GeometryModel3D>();
            foreach (var kvp in perTextureMeshes)
            {
                var mat = getMaterial(kvp.Key);
                var model = new GeometryModel3D(kvp.Value, mat) { BackMaterial = mat };
                results.Add(model);
            }
            return results;
        }

        private static void AddFace(Dictionary<int, MeshGeometry3D> meshes, int texId, 
            Point3D p1, Point3D p2, Point3D p3, Point3D p4, GndSurfaceTile s, bool isWall)
        {
            if (!meshes.TryGetValue(texId, out var mesh))
            {
                mesh = new MeshGeometry3D();
                meshes[texId] = mesh;
            }

            int baseIdx = mesh.Positions.Count;
            mesh.Positions.Add(p1); mesh.Positions.Add(p2);
            mesh.Positions.Add(p3); mesh.Positions.Add(p4);

            // UV Mapping (Matches BrowEdit GndRenderer.cpp vertex order)
            mesh.TextureCoordinates.Add(new Point(s.U1, s.V1));
            mesh.TextureCoordinates.Add(new Point(s.U2, s.V2));
            mesh.TextureCoordinates.Add(new Point(s.U3, s.V3));
            mesh.TextureCoordinates.Add(new Point(s.U4, s.V4));

            // Triangle 1
            mesh.TriangleIndices.Add(baseIdx + 0);
            mesh.TriangleIndices.Add(baseIdx + 1);
            mesh.TriangleIndices.Add(baseIdx + 2);
            // Triangle 2
            mesh.TriangleIndices.Add(baseIdx + 1);
            mesh.TriangleIndices.Add(baseIdx + 3);
            mesh.TriangleIndices.Add(baseIdx + 2);
        }
    }
}