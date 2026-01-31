using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace ROMapOverlayEditor.ThreeD
{
    /// <summary>
    /// BrowEdit3-style textured terrain mesh builder.
    /// Uses: position, texture UV, per-vertex smoothed normals.
    /// WPF limitation: MeshGeometry3D has no per-vertex color or second UV (lightmap);
    /// for those use a custom shader (e.g. SharpDX) with VertexP3T2T2C4N3.
    /// </summary>
    public static class GndTexturedTerrainBuilder
    {
        // WPF performance: build in chunks to avoid one mega-mesh freezing UI.
        public static List<Model3D> Build(GndV2 gnd, TextureAtlas atlas, int chunkSize = 32)
        {
            var models = new List<Model3D>();
            double zBase = gnd.Zoom * gnd.Height;

            // BrowEdit3-style per-vertex smoothed normals (one pass for full map, then index by (gx,gy,vertexIndex))
            Vector3D[]? smoothedNormals = null;
            try
            {
                smoothedNormals = GndNormalCalculator.GetSmoothedNormals(gnd);
            }
            catch
            {
                // If normal calc fails, mesh will use default lighting (no normals set)
            }

            for (int cy = 0; cy < gnd.Height; cy += chunkSize)
            {
                for (int cx = 0; cx < gnd.Width; cx += chunkSize)
                {
                    int w = Math.Min(chunkSize, gnd.Width - cx);
                    int h = Math.Min(chunkSize, gnd.Height - cy);

                    var mesh = new MeshGeometry3D
                    {
                        Positions = new Point3DCollection(),
                        TriangleIndices = new Int32Collection(),
                        TextureCoordinates = new PointCollection(),
                        Normals = new Vector3DCollection()
                    };

                    int baseIndex = 0;

                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            int gx = cx + x;
                            int gy = cy + y;

                            var cube = gnd.CubeAt(gx, gy);
                            if (cube == null) continue;
                            if (cube.TileUp < 0 || cube.TileUp >= gnd.Tiles.Count) continue;

                            var tile = gnd.Tiles[cube.TileUp];
                            int texId = tile.TextureIndex;

                            var rect = atlas.TextureRects01.TryGetValue(texId, out var r) ? r : new Rect(0, 0, 1, 1);

                            // BrowEdit3 position basis: (10*x, -h, 10*height - 10*y + offset)
                            double xx = gnd.Zoom * gx;
                            double zz = zBase - (gnd.Zoom * gy);

                            var p1 = new Point3D(xx, -cube.H1, zz);                 // h1
                            var p2 = new Point3D(xx + gnd.Zoom, -cube.H2, zz);      // h2
                            var p3 = new Point3D(xx, -cube.H3, zz - gnd.Zoom);      // h3
                            var p4 = new Point3D(xx + gnd.Zoom, -cube.H4, zz - gnd.Zoom); // h4

                            // Tile UVs (BrowEdit order U1..U4 V1..V4); remap into atlas rect.
                            var t1 = Remap(tile.V1, rect);
                            var t2 = Remap(tile.V2, rect);
                            var t3 = Remap(tile.V3, rect);
                            var t4 = Remap(tile.V4, rect);

                            // Per-vertex normals (our order: p1=h1, p2=h2, p4=h4, p3=h3 -> indices 0,1,2,3)
                            int normalBase = (gx + gy * gnd.Width) * 4;
                            Vector3D n1 = default, n2 = default, n3 = default, n4 = default;
                            if (smoothedNormals != null && normalBase + 4 <= smoothedNormals.Length)
                            {
                                n1 = smoothedNormals[normalBase + 0];
                                n2 = smoothedNormals[normalBase + 1];
                                n4 = smoothedNormals[normalBase + 2];
                                n3 = smoothedNormals[normalBase + 3];
                            }

                            // Add 4 verts (vertex order: p1, p2, p4, p3)
                            mesh.Positions.Add(p1);
                            mesh.Positions.Add(p2);
                            mesh.Positions.Add(p4);
                            mesh.Positions.Add(p3);

                            mesh.TextureCoordinates.Add(t1);
                            mesh.TextureCoordinates.Add(t2);
                            mesh.TextureCoordinates.Add(t4);
                            mesh.TextureCoordinates.Add(t3);

                            mesh.Normals.Add(n1);
                            mesh.Normals.Add(n2);
                            mesh.Normals.Add(n4);
                            mesh.Normals.Add(n3);

                            // Two triangles (p1,p2,p4) + (p1,p4,p3) — CCW
                            mesh.TriangleIndices.Add(baseIndex + 0);
                            mesh.TriangleIndices.Add(baseIndex + 1);
                            mesh.TriangleIndices.Add(baseIndex + 2);

                            mesh.TriangleIndices.Add(baseIndex + 0);
                            mesh.TriangleIndices.Add(baseIndex + 2);
                            mesh.TriangleIndices.Add(baseIndex + 3);

                            baseIndex += 4;

                            // ------------------------------------------------------------------------
                            // SIDE WALL (Right: x+1)
                            // Connects current (TR/BR) to next tile (TL/BL)
                            // ------------------------------------------------------------------------
                            if (cube.TileRight >= 0 && gx < gnd.Width - 1)
                            {
                                 var nextCube = gnd.CubeAt(gx + 1, gy);
                                 if (nextCube != null) // BrowEdit simply trusts next tile exists if Width check passes
                                 {
                                     // Geometry:
                                     // Quad between:
                                     //   v1: TR of current (p2) = (x+zoom, -h2, z)
                                     //   v2: BR of current (p4) = (x+zoom, -h4, z-zoom)
                                     //   v3: TL of next    = (x+zoom, -next.h1, z)
                                     //   v4: BL of next    = (x+zoom, -next.h3, z-zoom)

                                     var v_tr_curr = p2;
                                     var v_br_curr = p4;
                                     var v_tl_next = new Point3D(xx + gnd.Zoom, -nextCube.H1, zz);
                                     var v_bl_next = new Point3D(xx + gnd.Zoom, -nextCube.H3, zz - gnd.Zoom);

                                     // UVs from TileSide
                                     // TileSide texture:
                                     var tileSide = gnd.Tiles[cube.TileRight];
                                     var rectSide = atlas.TextureRects01.TryGetValue(tileSide.TextureIndex, out var rs) ? rs : new Rect(0, 0, 1, 1);

                                     // BrowEdit UV mapping for walls roughly matches standard quad?
                                     // Let's assume U1..U4 map to TL, TR, BL, BR of the wall quad.
                                     // Wall Quad upright:
                                     // Top-Left: v_tr_curr? No, depends on height.
                                     // Let's stick to standard order: 1=TL, 2=TR, 3=BL, 4=BR relative to face?
                                     
                                     var uv1 = Remap(tileSide.V1, rectSide);
                                     var uv2 = Remap(tileSide.V2, rectSide);
                                     var uv3 = Remap(tileSide.V3, rectSide);
                                     var uv4 = Remap(tileSide.V4, rectSide);
                                     
                                     // Normal: (1, 0, 0)
                                     var nSide = new Vector3D(1, 0, 0);

                                     // Add Vertices
                                     // Order: v_tr_curr, v_br_curr, v_bl_next, v_tl_next
                                     // Wait, connectivity: 
                                     // TR(curr) -> BR(curr) -> BL(next) -> TL(next)
                                     // 1->2->4->3 winding
                                     
                                     mesh.Positions.Add(v_tr_curr); // 0 (TL of wall?)
                                     mesh.Positions.Add(v_br_curr); // 1 (BL of wall?)
                                     mesh.Positions.Add(v_bl_next); // 2 (BR of wall?)
                                     mesh.Positions.Add(v_tl_next); // 3 (TR of wall?)

                                     // Indices need 2 tris. 
                                     // 0, 1, 2
                                     // 0, 2, 3
                                     mesh.TriangleIndices.Add(baseIndex + 0);
                                     mesh.TriangleIndices.Add(baseIndex + 1);
                                     mesh.TriangleIndices.Add(baseIndex + 2);

                                     mesh.TriangleIndices.Add(baseIndex + 0);
                                     mesh.TriangleIndices.Add(baseIndex + 2);
                                     mesh.TriangleIndices.Add(baseIndex + 3);

                                     // UVs
                                     mesh.TextureCoordinates.Add(uv2); // TR curr -> Top?
                                     mesh.TextureCoordinates.Add(uv4); // BR curr -> Bottom?
                                     mesh.TextureCoordinates.Add(uv3); // BL next
                                     mesh.TextureCoordinates.Add(uv1); // TL next
                                     
                                     // Normals
                                     mesh.Normals.Add(nSide);
                                     mesh.Normals.Add(nSide);
                                     mesh.Normals.Add(nSide);
                                     mesh.Normals.Add(nSide);
                                     
                                     baseIndex += 4;
                                 }
                            }

                            // ------------------------------------------------------------------------
                            // FRONT WALL (Bottom: y+1)
                            // Connects current (BL/BR) to next row (TL/TR)
                            // ------------------------------------------------------------------------
                            if (cube.TileFront >= 0 && gy < gnd.Height - 1)
                            {
                                var nextCube = gnd.CubeAt(gx, gy + 1);
                                if (nextCube != null) 
                                {
                                    // Geometry:
                                    // Quad between:
                                    //   v1: BL of current (p3) = (x, -h3, z-zoom)
                                    //   v2: BR of current (p4) = (x+zoom, -h4, z-zoom)
                                    //   v3: TL of next = (x, -next.h1, z-zoom)
                                    //   v4: TR of next = (x+zoom, -next.h2, z-zoom)
                                    
                                    var v_bl_curr = p3;
                                    var v_br_curr = p4;
                                    var v_tl_next = new Point3D(xx, -nextCube.H1, zz - gnd.Zoom);
                                    var v_tr_next = new Point3D(xx + gnd.Zoom, -nextCube.H2, zz - gnd.Zoom);
                                    
                                    var tileFront = gnd.Tiles[cube.TileFront];
                                    var rectFront = atlas.TextureRects01.TryGetValue(tileFront.TextureIndex, out var rf) ? rf : new Rect(0, 0, 1, 1);
                                    
                                    var uv1 = Remap(tileFront.V1, rectFront);
                                    var uv2 = Remap(tileFront.V2, rectFront);
                                    var uv3 = Remap(tileFront.V3, rectFront);
                                    var uv4 = Remap(tileFront.V4, rectFront);
                                    
                                    var nFront = new Vector3D(0, 0, -1);
                                    
                                    // Verts
                                    mesh.Positions.Add(v_bl_curr); // 0
                                    mesh.Positions.Add(v_br_curr); // 1
                                    mesh.Positions.Add(v_tr_next); // 2
                                    mesh.Positions.Add(v_tl_next); // 3
                                    
                                    // Indices
                                    mesh.TriangleIndices.Add(baseIndex + 0);
                                    mesh.TriangleIndices.Add(baseIndex + 1);
                                    mesh.TriangleIndices.Add(baseIndex + 2);
                                    
                                    mesh.TriangleIndices.Add(baseIndex + 0);
                                    mesh.TriangleIndices.Add(baseIndex + 2);
                                    mesh.TriangleIndices.Add(baseIndex + 3);
                                    
                                    // UVs - mapping logic?
                                    // Standard 1,2,4,3 layout?
                                    mesh.TextureCoordinates.Add(uv1);
                                    mesh.TextureCoordinates.Add(uv2);
                                    mesh.TextureCoordinates.Add(uv4);
                                    mesh.TextureCoordinates.Add(uv3);
                                    
                                    // Normals
                                    mesh.Normals.Add(nFront);
                                    mesh.Normals.Add(nFront);
                                    mesh.Normals.Add(nFront);
                                    mesh.Normals.Add(nFront);
                                    
                                    baseIndex += 4;
                                }
                            }
                        }
                    }

                    if (mesh.Positions.Count == 0)
                        continue;

                    // WPF: if no normals were set, clear so runtime generates face normals
                    if (mesh.Normals.Count == 0)
                        mesh.Normals = null;

                    mesh.Freeze();

                    var brush = new ImageBrush(atlas.AtlasBitmap)
                    {
                        ViewportUnits = BrushMappingMode.Absolute,
                        TileMode = TileMode.None,
                        Stretch = Stretch.Fill
                    };
                    brush.Freeze();

                    var material = new DiffuseMaterial(brush);
                    material.Freeze();

                    var gm = new GeometryModel3D(mesh, material)
                    {
                        BackMaterial = material
                    };
                    gm.Freeze();

                    models.Add(gm);
                }
            }

            return models;
        }

        private static Point Remap(Vector uv, Rect rect01)
        {
            // WPF texture coords origin is top-left; our decoder already handled origin flag.
            double u = rect01.X + (uv.X * rect01.Width);
            double v = rect01.Y + (uv.Y * rect01.Height);
            return new Point(u, v);
        }
    }
}
