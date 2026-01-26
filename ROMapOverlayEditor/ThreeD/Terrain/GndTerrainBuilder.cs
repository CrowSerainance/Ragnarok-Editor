using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using ROMapOverlayEditor.Gnd;

namespace ROMapOverlayEditor.ThreeD.Terrain
{
    /// <summary>Builds a textured GND terrain mesh using an atlas and VfsTextureResolver.</summary>
    public static class GndTerrainBuilder
    {
        /// <summary>
        /// Build a textured terrain from GND. Uses existing GndFile and VfsTextureResolver.
        /// X east, Z south, Y up. One GeometryModel3D with atlas material.
        /// </summary>
        public static GeometryModel3D BuildTexturedTerrain(
            GndFile gnd,
            VfsTextureResolver texResolver,
            int atlasMaxSize = 4096)
        {
            if (gnd == null) throw new ArgumentNullException(nameof(gnd));
            if (texResolver == null) throw new ArgumentNullException(nameof(texResolver));
            if (gnd.Textures.Count == 0)
                throw new ArgumentException("GND has no textures.", nameof(gnd));

            var bmpList = new List<BitmapSource>(gnd.Textures.Count);
            for (int i = 0; i < gnd.Textures.Count; i++)
            {
                var img = texResolver.TryLoadTexture(gnd.Textures[i].File);
                bmpList.Add(img ?? MakeMissingTexture());
            }

            var atlas = TextureAtlasBuilder.Build(bmpList, maxAtlasSize: atlasMaxSize, padding: 2);

            var mb = new MeshBuilder(true, true);
            float s = gnd.TileScale;

            for (int y = 0; y < gnd.Height; y++)
            {
                for (int x = 0; x < gnd.Width; x++)
                {
                    var c = gnd.Cubes[x, y];
                    if (c == null) continue;

                    int tileIndex = c.TileUp;
                    if (tileIndex < 0 || tileIndex >= gnd.Tiles.Count) continue;

                    var t = gnd.Tiles[tileIndex];
                    int texIndex = t.TextureIndex;
                    if (texIndex < 0 || texIndex >= gnd.Textures.Count)
                        texIndex = 0;

                    var rect = atlas.TexIndexToRect.TryGetValue(texIndex, out var r) ? r : new Rect(0, 0, 1, 1);

                    var uv1 = ToAtlasUv(t.U1, t.V1, rect);
                    var uv2 = ToAtlasUv(t.U2, t.V2, rect);
                    var uv3 = ToAtlasUv(t.U3, t.V3, rect);
                    var uv4 = ToAtlasUv(t.U4, t.V4, rect);

                    var p1 = new Point3D(x * s, c.H1, y * s);
                    var p2 = new Point3D((x + 1) * s, c.H2, y * s);
                    var p3 = new Point3D(x * s, c.H3, (y + 1) * s);
                    var p4 = new Point3D((x + 1) * s, c.H4, (y + 1) * s);

                    // Quad as two triangles: sw-se-ne, sw-ne-nw
                    mb.AddTriangle(p1, p2, p4, uv1, uv2, uv3);
                    mb.AddTriangle(p1, p4, p3, uv1, uv3, uv4);
                }
            }

            var mesh = mb.ToMesh();
            var brush = new ImageBrush(atlas.AtlasImage)
            {
                ViewportUnits = BrushMappingMode.Absolute,
                TileMode = TileMode.None,
                Stretch = Stretch.Fill
            };
            brush.Freeze();

            var mat = new DiffuseMaterial(brush);
            mat.Freeze();

            return new GeometryModel3D(mesh, mat) { BackMaterial = mat };
        }

        private static Point ToAtlasUv(float u, float v, Rect rect)
        {
            double uu = rect.X + u * rect.Width;
            double vv = rect.Y + v * rect.Height;
            return new Point(uu, vv);
        }

        private static BitmapSource MakeMissingTexture()
        {
            int w = 64, h = 64;
            var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            int stride = w * 4;
            var buf = new byte[h * stride];

            for (int py = 0; py < h; py++)
            for (int px = 0; px < w; px++)
            {
                bool on = ((px / 8) + (py / 8)) % 2 == 0;
                byte r = on ? (byte)255 : (byte)0;
                byte g = (byte)0;
                byte b = on ? (byte)255 : (byte)0;
                int i = (py * stride) + (px * 4);
                buf[i + 0] = b;
                buf[i + 1] = g;
                buf[i + 2] = r;
                buf[i + 3] = 255;
            }

            wb.WritePixels(new Int32Rect(0, 0, w, h), buf, stride, 0);
            wb.Freeze();
            return wb;
        }
    }
}
