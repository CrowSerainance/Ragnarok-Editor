using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ROMapOverlayEditor.Rsw
{
    // BrowEdit3 format notes: version = byte minor | byte major; buildNumber if >= 0x0202;
    // unknown int if >= 0x0205; water block removed if >= 0x0206.
    public static class RswIO
    {
        public static bool LooksLikeRsw(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 8) return false;
            return bytes[0] == (byte)'G' && bytes[1] == (byte)'R' && bytes[2] == (byte)'S' && bytes[3] == (byte)'W';
        }

        public static RswFile Read(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 16)
                throw new InvalidDataException("RSW buffer too small.");
            using var ms = new MemoryStream(bytes, writable: false);
            return Read(ms);
        }

        public static RswFile Read(Stream stream)
        {
            using var br = new BinaryReader(stream, Encoding.GetEncoding(1252), leaveOpen: true);

            var sig = Encoding.ASCII.GetString(br.ReadBytes(4));
            if (sig != "GRSW")
                throw new InvalidDataException($"Not an RSW file (sig={sig})");

            // IMPORTANT: minor first, then major
            byte minor = br.ReadByte();
            byte major = br.ReadByte();
            ushort version = (ushort)((major << 8) | minor);

            byte buildNumber = 0;
            if (version >= 0x0202)
                buildNumber = br.ReadByte();

            int unknownAfterBuild = 0;
            if (version >= 0x0205)
                unknownAfterBuild = br.ReadInt32();

            // v1.02: GAT string only from v1.04; water only from v1.03; light only from v1.05
            string ini = ReadFixedString(br, 40);
            string gnd = ReadFixedString(br, 40);
            string gat;
            string src;
            if (version >= 0x0104)
            {
                gat = ReadFixedString(br, 40);
                src = ReadFixedString(br, 40);
            }
            else
            {
                gat = gnd;
                src = ReadFixedString(br, 40);
            }

            WaterSettings? water = null;
            if (version >= 0x0103 && version < 0x0206)
            {
                float waterLevel = br.ReadSingle();
                int waterType = 0;
                float waveHeight = 0, waveSpeed = 0, wavePitch = 0;
                int animSpeed = 100;
                if (version >= 0x0108)
                {
                    waterType = br.ReadInt32();
                    waveHeight = br.ReadSingle();
                    waveSpeed = br.ReadSingle();
                    wavePitch = br.ReadSingle();
                }
                if (version >= 0x0109)
                    animSpeed = br.ReadInt32();
                water = new WaterSettings
                {
                    WaterLevel = waterLevel,
                    WaterType = waterType,
                    WaveHeight = waveHeight,
                    WaveSpeed = waveSpeed,
                    WavePitch = wavePitch,
                    AnimSpeed = animSpeed
                };
            }

            var light = new LightSettings
            {
                Longitude = 45,
                Latitude = 45,
                Diffuse = new Vec3(1.0f, 1.0f, 1.0f),
                Ambient = new Vec3(0.3f, 0.3f, 0.3f),
                Opacity = 1.0f
            };
            if (version >= 0x0105)
            {
                light = new LightSettings
                {
                    Longitude = br.ReadInt32(),
                    Latitude = br.ReadInt32(),
                    Diffuse = new Vec3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                    Ambient = new Vec3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                    Opacity = br.ReadSingle()
                };
            }

            long objCountPos = stream.Position;
            int objectCount = br.ReadInt32();

            if (!IsReasonableObjectCount(objectCount))
            {
                stream.Position = objCountPos;
                objectCount = FindObjectCountByScanning(br, maxScanBytes: 256, out long foundAt);
                if (objectCount < 0)
                    throw new InvalidDataException($"RSW: Could not locate a valid objectCount near offset 0x{objCountPos:X} (ver=0x{version:X4})");
                stream.Position = foundAt + 4;
            }

            var rsw = new RswFile
            {
                Signature = sig,
                Version = version,
                BuildNumber = buildNumber,
                UnknownAfterBuild = unknownAfterBuild,
                IniFile = ini,
                GndFile = gnd,
                GatFile = gat,
                SourceFile = src,
                Water = water,
                Light = light,
                Objects = new List<RswObject>()
            };

            for (int i = 0; i < objectCount; i++)
            {
                long posHere = stream.Position;
                int type = br.ReadInt32();
                if (type < 1 || type > 4)
                    throw new InvalidDataException(
                        $"Unknown RSW object type {type} at index {i} (Offset 0x{posHere:X}, Version 0x{version:X4}).");
                switch (type)
                {
                    case 1:
                        rsw.Objects.Add(ReadModel(br, version));
                        break;
                    case 2:
                        rsw.Objects.Add(ReadLight(br, version));
                        break;
                    case 3:
                        rsw.Objects.Add(ReadSound(br));
                        break;
                    case 4:
                        rsw.Objects.Add(ReadEffect(br));
                        break;
                    default:
                        rsw.Objects.Add(ReadUnknownObject(br, type));
                        break;
                }
            }

            return rsw;
        }

        private static bool IsReasonableObjectCount(int n) => n >= 0 && n <= 500000;

        private static int FindObjectCountByScanning(BinaryReader br, int maxScanBytes, out long foundAt)
        {
            var s = br.BaseStream;
            long start = s.Position;
            foundAt = -1;
            for (int offset = 0; offset <= maxScanBytes; offset += 4)
            {
                s.Position = start + offset;
                int candidate = br.ReadInt32();
                if (!IsReasonableObjectCount(candidate)) continue;
                if (candidate == 0)
                {
                    foundAt = start + offset;
                    return candidate;
                }
                long afterCount = s.Position;
                int type = br.ReadInt32();
                s.Position = afterCount;
                if (type >= 0 && type <= 16)
                {
                    foundAt = start + offset;
                    return candidate;
                }
            }
            s.Position = start;
            return -1;
        }

        private static string ReadFixedString(BinaryReader br, int len)
        {
            var b = br.ReadBytes(len);
            int z = Array.IndexOf(b, (byte)0);
            if (z < 0) z = b.Length;
            return Encoding.GetEncoding(1252).GetString(b, 0, z).Trim();
        }

        private static string ReadCStr(BinaryReader br)
        {
            var bytes = new List<byte>(64);
            while (true)
            {
                byte c = br.ReadByte();
                if (c == 0) break;
                bytes.Add(c);
            }
            return Encoding.GetEncoding(1252).GetString(bytes.ToArray());
        }

        private static RswModel ReadModel(BinaryReader br, ushort version)
        {
            string name = "";
            int animType = 0;
            float animSpeed = 0;
            int blockType = 0;
            string file;
            if (version >= 0x0103)
            {
                name = ReadCStr(br);
                animType = br.ReadInt32();
                animSpeed = br.ReadSingle();
                blockType = br.ReadInt32();
                file = ReadCStr(br);
            }
            else
            {
                file = ReadFixedString(br, 80);
            }
            var pos = new Vec3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            var rot = new Vec3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            var scale = new Vec3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            return new RswModel
            {
                ObjectType = 1,
                Name = name,
                AnimType = animType,
                AnimSpeed = animSpeed,
                BlockType = blockType,
                FileName = file,
                Position = pos,
                Rotation = rot,
                Scale = scale
            };
        }

        private static RswLight ReadLight(BinaryReader br, ushort version)
        {
            string name = (version >= 0x0103) ? ReadCStr(br) : "";
            var pos = new Vec3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            var color = new Vec3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            float range = br.ReadSingle();
            return new RswLight { ObjectType = 2, Name = name, Position = pos, Color = color, Range = range };
        }

        private static RswSound ReadSound(BinaryReader br)
        {
            string name = ReadCStr(br);
            string file = ReadCStr(br);
            var pos = new Vec3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            float vol = br.ReadSingle();
            int width = br.ReadInt32();
            int height = br.ReadInt32();
            float range = br.ReadSingle();
            return new RswSound
            {
                ObjectType = 3,
                Name = name,
                FileName = file,
                Position = pos,
                Volume = vol,
                Width = width,
                Height = height,
                Range = range
            };
        }

        private static RswEffect ReadEffect(BinaryReader br)
        {
            string name = ReadCStr(br);
            var pos = new Vec3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            int id = br.ReadInt32();
            float delay = br.ReadSingle();
            float param = br.ReadSingle();
            return new RswEffect
            {
                ObjectType = 4,
                Name = name,
                Position = pos,
                EffectId = id,
                Delay = delay,
                Param = param
            };
        }

        private static RswObject ReadUnknownObject(BinaryReader br, int type)
        {
            string name = ReadCStr(br);
            var pos = new Vec3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            var rot = new Vec3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            var scale = new Vec3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            return new RswUnknown
            {
                ObjectType = type,
                Name = name,
                Position = pos,
                Rotation = rot,
                Scale = scale
            };
        }
    }
}
