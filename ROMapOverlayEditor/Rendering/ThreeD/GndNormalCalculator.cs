using System;
using System.Windows.Media.Media3D;

namespace ROMapOverlayEditor.ThreeD
{
    /// <summary>
    /// BrowEdit3-style per-vertex normal calculation for GND terrain.
    /// Matches Gnd.cpp calcNormal() and calcNormals(): face normals plus smoothing
    /// when adjacent cube heights match.
    /// </summary>
    public static class GndNormalCalculator
    {
        /// <summary>
        /// Quad layout (BrowEdit): 3----4; 1----2. Heights: v1=h1(0,0), v2=h2(10,0), v3=h3(0,10), v4=h4(10,10).
        /// Returns normals for vertex order used in GndTexturedTerrainBuilder: 0=h1, 1=h2, 2=h4, 3=h3.
        /// </summary>
        public static Vector3D[] GetSmoothedNormals(GndV2 gnd)
        {
            int w = gnd.Width;
            int h = gnd.Height;
            double zoom = gnd.Zoom;

            // Per-cube: 4 vertex normals (our order: h1, h2, h4, h3); normalsForCalc used when blending
            var cubeNormals = new Vector3D[w * h * 4];
            var normalsForCalcStore = new Vector3D[w * h * 4];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var cube = gnd.CubeAt(x, y);
                    if (cube == null) continue;

                    // BrowEdit: v1=(0,h1,0), v2=(10,h2,0), v3=(0,h3,10), v4=(10,h4,10) in tile space
                    double h1 = cube.H1, h2 = cube.H2, h3 = cube.H3, h4 = cube.H4;
                    var v1 = new Vector3D(0, -h1, 0);
                    var v2 = new Vector3D(zoom, -h2, 0);
                    var v3 = new Vector3D(0, -h3, zoom);
                    var v4 = new Vector3D(zoom, -h4, zoom);

                    // normal1 = cross(v2-v1, v3-v1); normal2 = cross(v3-v4, v2-v4)
                    var edge21 = v2 - v1;
                    var edge31 = v3 - v1;
                    var normal1 = Vector3D.CrossProduct(edge21, edge31);
                    if (normal1.LengthSquared > 1e-10)
                        normal1.Normalize();

                    var edge34 = v3 - v4;
                    var edge24 = v2 - v4;
                    var normal2 = Vector3D.CrossProduct(edge34, edge24);
                    if (normal2.LengthSquared > 1e-10)
                        normal2.Normalize();

                    var normal = normal1 + normal2;
                    if (normal.LengthSquared > 1e-10)
                        normal.Normalize();

                    // BrowEdit normalsDefault: [0]=normal1, [1]=normal, [2]=normal, [3]=normal1
                    // normalsForCalc: [0]=normal1, [1]=normal, [2]=normal, [3]=normal2 (used when blending)
                    var normalsDefault = new[] { normal1, normal, normal, normal1 };
                    var normalsForCalc = new[] { normal1, normal, normal, normal2 };

                    // Our vertex order: 0=h1, 1=h2, 2=h4, 3=h3 -> BrowEdit indices 0,1,3,2
                    int baseIdx = (x + y * w) * 4;
                    for (int i = 0; i < 4; i++)
                        cubeNormals[baseIdx + i] = normalsDefault[i];
                    for (int i = 0; i < 4; i++)
                        normalsForCalcStore[baseIdx + i] = normalsForCalc[i];
                }
            }

            // Smooth: blend with adjacent cube normalsForCalc when heights match (BrowEdit calcNormals)
            var smoothed = new Vector3D[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var cube = gnd.CubeAt(x, y);
                    if (cube == null) continue;

                    int baseIdx = (x + y * w) * 4;
                    double[] heights = { cube.H1, cube.H2, cube.H4, cube.H3 }; // our vertex order

                    for (int i = 0; i < 4; i++)
                    {
                        var n = cubeNormals[baseIdx + i];
                        double heightAtVertex = heights[i];

                        // Check 3 adjacent cubes for matching height (BrowEdit's ii=1,2,3)
                        for (int ii = 1; ii < 4; ii++)
                        {
                            int xx = (ii % 2) * ((i % 2 == 0) ? -1 : 1);
                            int yy = (ii / 2) * (i < 2 ? -1 : 1);
                            int nx = x + xx;
                            int ny = y + yy;
                            if (!gnd.InMap(nx, ny)) continue;

                            var adjCube = gnd.CubeAt(nx, ny);
                            if (adjCube == null) continue;

                            // Adjacent vertex index that touches this one
                            int ci = (i + ii * (1 - 2 * (i & 1))) & 3;
                            double adjHeight = ci == 0 ? adjCube.H1 : ci == 1 ? adjCube.H2 : ci == 2 ? adjCube.H4 : adjCube.H3;
                            if (Math.Abs(adjHeight - heightAtVertex) > 1e-5)
                                continue;

                            int adjBase = (nx + ny * w) * 4;
                            n += normalsForCalcStore[adjBase + ci];
                        }

                        if (n.LengthSquared > 1e-10)
                            n.Normalize();
                        smoothed[baseIdx + i] = n;
                    }
                }
            }

            return smoothed;
        }
    }
}
