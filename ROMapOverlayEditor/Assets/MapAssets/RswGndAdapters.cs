// ============================================================================
// RswGndAdapters - Convert V2 types to RswFile (BrowEdit-compatible) for UI/mesh
// ============================================================================

using System;
using System.Collections.Generic;
using ROMapOverlayEditor.Gnd;
using ROMapOverlayEditor.Rsw;

namespace ROMapOverlayEditor.MapAssets
{
    /// <summary>Convert RswFileV2 to RswFile for CreateRswMarkers and existing UI.</summary>
    public static class RswV2Adapter
    {
        public static RswFile ToRswFile(RswFileV2 v2)
        {
            var objects = new List<RswObject>();
            foreach (var o in v2.Objects)
            {
                if (o is RswModelObject m)
                    objects.Add(new RswModel
                    {
                        ObjectType = 1,
                        Name = string.IsNullOrEmpty(m.Name) ? m.ObjectName : m.Name,
                        AnimType = m.AnimationType,
                        AnimSpeed = m.AnimationSpeed,
                        BlockType = m.BlockType,
                        FileName = m.Filename,
                        Position = V(m.Position),
                        Rotation = V(m.Rotation),
                        Scale = V(m.Scale)
                    });
                else if (o is RswLightObject l)
                    objects.Add(new RswLight { ObjectType = 2, Name = l.Name, Position = V(l.Position), Color = V(l.Color), Range = l.Range });
                else if (o is RswSoundObject s)
                    objects.Add(new RswSound
                    {
                        ObjectType = 3,
                        Name = s.Name,
                        FileName = s.WaveFile,
                        Position = V(s.Position),
                        Rotation = V(s.Rotation),
                        Scale = V(s.Scale),
                        Volume = s.Volume,
                        Width = s.Width,
                        Height = s.Height,
                        Range = s.Range
                    });
                else if (o is RswEffectObject e)
                    objects.Add(new RswEffect { ObjectType = 4, Name = e.Name, Position = V(e.Position), EffectId = e.EffectId, Loop = e.EmitSpeed, Param1 = e.Param1, Param2 = e.Unknown1, Param3 = e.Unknown2, Param4 = e.Unknown3 });
            }
            var rsw = new RswFile
            {
                Version = v2.Version,
                BuildNumber = (byte)(v2.BuildNumber ?? 0),
                UnknownAfterBuild = v2.UnknownV205 ?? 0,
                IniFile = v2.IniFile1,
                GndFile = v2.GndFile,
                GatFile = v2.GatFile,
                SourceFile = v2.IniFile2,
                Objects = objects
            };
            if (v2.Water != null)
                rsw.Water = new WaterSettings
                {
                    WaterLevel = v2.Water.Height,
                    WaterType = v2.Water.Type,
                    WaveHeight = v2.Water.Amplitude,
                    WaveSpeed = v2.Water.WaveSpeed,
                    WavePitch = v2.Water.WavePitch,
                    AnimSpeed = v2.Water.TextureAnimSpeed
                };
            if (v2.Lighting != null)
                rsw.Light = new LightSettings
                {
                    Longitude = v2.Lighting.Longitude,
                    Latitude = v2.Lighting.Latitude,
                    Diffuse = V(v2.Lighting.DiffuseColor),
                    Ambient = V(v2.Lighting.AmbientColor),
                    Opacity = v2.Lighting.ShadowOpacity
                };
            return rsw;
        }

        private static Vec3 V(Vec3F v) => new Vec3(v.X, v.Y, v.Z);
    }

    /// <summary>Convert GndFileV2 to GndFile for GndMeshBuilder.BuildTerrain.</summary>
    public static class GndV2Adapter
    {
        public static GndFile ToGndFile(GndFileV2 v2)
        {
            var g = new GndFile
            {
                Version = v2.Version,
                Width = v2.Width,
                Height = v2.Height,
                TileScale = v2.TileScale,
                LightmapWidth = v2.Lightmaps.CellWidth,
                LightmapHeight = v2.Lightmaps.CellHeight,
                GridSizeCell = v2.Lightmaps.GridSizeCell
            };
            foreach (var t in v2.Textures)
                g.Textures.Add(new GndTexture { File = t.Filename, Name = t.Name });
            foreach (var s in v2.Surfaces)
                g.Tiles.Add(new GndTile { U1 = s.U1, U2 = s.U2, U3 = s.U3, U4 = s.U4, V1 = s.V1, V2 = s.V2, V3 = s.V3, V4 = s.V4, TextureIndex = s.TextureIndex, LightmapIndex = s.LightmapIndex, R = s.R, G = s.G, B = s.B, A = s.A });
            g.Cubes = new GndCube[v2.Width, v2.Height];
            for (int y = 0; y < v2.Height; y++)
                for (int x = 0; x < v2.Width; x++)
                {
                    var c = v2.Cubes[x, y];
                    g.Cubes[x, y] = new GndCube { H1 = c.Height00, H2 = c.Height10, H3 = c.Height01, H4 = c.Height11, TileUp = c.TileUp, TileSide = c.TileSide, TileFront = c.TileFront };
                }
            return g;
        }
    }
}
