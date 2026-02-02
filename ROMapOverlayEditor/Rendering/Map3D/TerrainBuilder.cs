using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using ROMapOverlayEditor.Gnd; // New Namespace

namespace ROMapOverlayEditor.Map3D
{
    public sealed class TerrainBuildResult
    {
        public readonly List<ModelVisual3D> TerrainPieces = new();
        public Rect3D Bounds;
    }

    public static class TerrainBuilder
    {
        // Builds one MeshGeometry3D per textureId. Only TOP faces (terrain) are rendered.
        // COORDINATE SYSTEM: BrowEdit3 style (zoom×x, -h, zoom×y)
        public static TerrainBuildResult BuildTexturedTerrain(
            GndFileV2 gnd,
            Func<string, byte[]?> tryLoadTextureBytes,
            float yScale = 1.0f)
        {
            var res = new TerrainBuildResult();
            var perTex = new Dictionary<int, MeshGeometry3D>();

            int w = gnd.Width;
            int h = gnd.Height;
            float zoom = gnd.TileScale; // BrowEdit3 uses this as zoom/scale factor

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // GndFileV2 stores cubes in [x,y] 2D array
                    var cube = gnd.Cubes[x, y];
                    
                    int surfIndex = cube.TileUp; // Top surface index
                    if (surfIndex < 0 || surfIndex >= gnd.Surfaces.Count)
                        continue;

                    var s = gnd.Surfaces[surfIndex];
                    int texId = s.TextureIndex;

                    if (!perTex.TryGetValue(texId, out var mesh))
                    {
                        mesh = new MeshGeometry3D();
                        perTex[texId] = mesh;
                    }

                    // BrowEdit3 coordinate system: (zoom×x, -h, zoom×y)
                    // H1=BL, H2=BR, H3=TL, H4=TR
                    // GndReaderV2 assigns: H1->Height01 (BL), H2->Height11 (BR), H3->Height00 (TL), H4->Height10 (TR)
                    
                    // Coordinates:
                    // TL (x, y)     -> H3 (Height00)
                    // TR (x+1, y)   -> H4 (Height10)
                    // BL (x, y+1)   -> H1 (Height01)
                    // BR (x+1, y+1) -> H2 (Height11)
                    
                    // Logic from old builder:
                    // p1 (x, y) -> -H1 ?  Wait, old builder said:
                    // p1(x, y) -> -cube.H1 (BL?)
                    // p2(x+1, y) -> -cube.H2 (BR?)
                    // This seems to imply (x,y) corresponds to bottom-left in old logic?
                    // Let's stick to BrowEdit3 logic via GndFileV2 comments.
                    
                    // Using the new mapping:
                    // Vertex order for simple quad: TL, TR, BR, BL or similar?
                    // Old code:
                    // p1 (x, y)     H1
                    // p2 (x+1, y)   H2
                    // p3 (x+1, y+1) H3
                    // p4 (x, y+1)   H4
                    
                    // Wait, old code loop was: 
                    // p1 = (zoom*x, -H1, zoom*y)
                    // p2 = (zoom*(x+1), -H2, zoom*y)
                    // p3 = (zoom*(x+1), -H3, zoom*(y+1))
                    // p4 = (zoom*x, -H4, zoom*(y+1))
                    
                    // This creates a quad:
                    // (x,y)--H1---(x+1,y)--H2
                    //  |             |
                    // (x,y+1)--H4--(x+1,y+1)--H3
                    
                    // In GndFileV2:
                    // Height00 (TL)
                    // Height10 (TR)
                    // Height01 (BL)
                    // Height11 (BR)
                    
                    // So we probably want:
                    // (x, y) -> TL -> Height00
                    // (x+1, y) -> TR -> Height10
                    // (x+1, y+1) -> BR -> Height11
                    // (x, y+1) -> BL -> Height01
                    
                    // Let's use standard grid coordinates where (x,y) is top-left in 2D array, but in 3D:
                    // Z increases downwards? 
                    // Old code: zoom*y matches index y.
                     
                    var p1 = new Point3D(zoom * x,       -cube.Height00 * yScale, zoom * y);       // TL
                    var p2 = new Point3D(zoom * (x + 1), -cube.Height10 * yScale, zoom * y);       // TR
                    var p3 = new Point3D(zoom * (x + 1), -cube.Height11 * yScale, zoom * (y + 1)); // BR
                    var p4 = new Point3D(zoom * x,       -cube.Height01 * yScale, zoom * (y + 1)); // BL

                    int baseIndex = mesh.Positions.Count;
                    mesh.Positions.Add(p1);
                    mesh.Positions.Add(p2);
                    mesh.Positions.Add(p3);
                    mesh.Positions.Add(p4);

                    mesh.TextureCoordinates.Add(new Point(s.U1, s.V1));
                    mesh.TextureCoordinates.Add(new Point(s.U2, s.V2));
                    // Note: U3/V3 and U4/V4 mapping depends on vertex order.
                    // Assuming 1->2->3->4 is TL->TR->BR->BL order in UVs too?
                    // Standard GndSurfaceTile has 4 UV pairs.
                    mesh.TextureCoordinates.Add(new Point(s.U3, s.V3)); // This effectively swaps if needed, checking standard behavior
                    // Usually it's TL, TR, BL, BR or similar.
                    // Let's assume U1=TL, U2=TR, U3=BL, U4=BR ?? 
                    // GndSurfaceTile comments don't specify, but GndReader reads them in order.
                    
                    // Let's stick to the order from existing code if possible, or standard quad order.
                    // Old code:
                    // Tex coords:
                    // Add(U1, V1) -> p1
                    // Add(U2, V2) -> p2
                    // Add(U3, V3) -> p3
                    // Add(U4, V4) -> p4
                    
                    // So we blindly map U3/V3 -> p3 (BR) and U4/V4 -> p4 (BL)?
                    mesh.TextureCoordinates.Add(new Point(s.U3, s.V3)); 
                    mesh.TextureCoordinates.Add(new Point(s.U4, s.V4));

                    mesh.TriangleIndices.Add(baseIndex + 0);
                    mesh.TriangleIndices.Add(baseIndex + 2);
                    mesh.TriangleIndices.Add(baseIndex + 1);

                    mesh.TriangleIndices.Add(baseIndex + 0);
                    mesh.TriangleIndices.Add(baseIndex + 3);
                    mesh.TriangleIndices.Add(baseIndex + 2);
                }
            }

            foreach (var kv in perTex)
            {
                int texId = kv.Key;
                var mesh = kv.Value;
                var mat = CreateMaterialForTexture(texId, gnd, tryLoadTextureBytes);
                var geom = new GeometryModel3D
                {
                    Geometry = mesh,
                    Material = mat,
                    BackMaterial = mat
                };
                res.TerrainPieces.Add(new ModelVisual3D { Content = geom });
            }

            res.Bounds = new Rect3D(0, 0, 0, gnd.Width * zoom, 50, gnd.Height * zoom);
            return res;
        }

        public static Material CreateMaterialForTexture(int texId, GndFileV2 gnd, Func<string, byte[]?> tryLoadTextureBytes)
        {
            if (texId < 0 || texId >= gnd.Textures.Count)
                return MaterialHelper.CreateMaterial(Brushes.Magenta);

            // GndTextureV2 has Filename
            string name = gnd.Textures[texId].Filename;
            if (string.IsNullOrWhiteSpace(name))
                return MaterialHelper.CreateMaterial(Brushes.Magenta);

            // BrowEdit3: "data/texture/" + texture->file (Gnd.cpp line 69)
            string[] candidates =
            {
                $"data/texture/{name}",
                $"data/texture/{name}.tga",
                $"data/texture/{name}.bmp",
                $"data/texture/{name}.png",
                $"data/texture/{name}.jpg",
                name
            };

            foreach (var c in candidates)
            {
                var bytes = tryLoadTextureBytes(c);
                if (bytes == null) continue;

                var bmp = TextureLoader.LoadTexture(bytes, c);
                if (bmp == null) continue;

                var brush = new ImageBrush(bmp) { Stretch = Stretch.Fill };
                brush.Freeze();
                return new DiffuseMaterial(brush);
            }

            return MaterialHelper.CreateMaterial(Brushes.Magenta);
        }
    }
}
