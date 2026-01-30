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

        /// <summary>Read only GND/GAT paths from RSW header (BrowEdit-style; use when resolving map files).</summary>
        public static (string? Gnd, string? Gat) ReadGndGatPaths(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 96) return (null, null);
            try
            {
                using var ms = new MemoryStream(bytes, writable: false);
                using var br = new BinaryReader(ms, Encoding.GetEncoding(1252), leaveOpen: true);
                if (Encoding.ASCII.GetString(br.ReadBytes(4)) != "GRSW") return (null, null);
                byte major = br.ReadByte();
                byte minor = br.ReadByte();
                ushort version = (ushort)((major << 8) | minor);
                if (version >= 0x0205) br.ReadInt32();
                if (version >= 0x0202) br.ReadByte();
                string ini = ReadFixedString(br, 40);
                string gnd = ReadFixedString(br, 40);
                string gat = version >= 0x0104 ? ReadFixedString(br, 40) : gnd;
                if (string.IsNullOrWhiteSpace(gnd)) return (null, null);
                return (gnd.Trim(), string.IsNullOrWhiteSpace(gat) ? gnd.Trim() : gat.Trim());
            }
            catch
            {
                return (null, null);
            }
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

            byte major = br.ReadByte();
            byte minor = br.ReadByte();
            ushort version = (ushort)((major << 8) | minor);

            byte buildNumber = 0;
            int unknownV205 = 0;

            if (version >= 0x0205)
                unknownV205 = br.ReadInt32();

            if (version >= 0x0202)
                buildNumber = br.ReadByte();

            string ini = ReadFixedString(br, 40);
            string gnd = ReadFixedString(br, 40);
            string gat = "";
            string src = "";

            if (version >= 0x0104)
            {
                gat = ReadFixedString(br, 40);
                src = ReadFixedString(br, 40); // BrowEdit reads iniFile AGAIN here (40 bytes), effectively skipping or reading source
            }
            else
            {
                gat = gnd;
                src = ReadFixedString(br, 40);
            }

            WaterSettings? water = null;
            if (version < 0x0206)
            {
                // Pre-2.6 water logic
                float waterLevel = 0;
                if (version >= 0x0103) waterLevel = br.ReadSingle();
                
                int type = 0;
                float height = 0, speed = 0, pitch = 0;
                int animSpeed = 100;

                if (version >= 0x0108)
                {
                    type = br.ReadInt32();
                    height = br.ReadSingle();
                    speed = br.ReadSingle();
                    pitch = br.ReadSingle();
                }
                if (version >= 0x0109)
                {
                    animSpeed = br.ReadInt32();
                }
                water = new WaterSettings { WaterLevel = waterLevel, WaterType = type, WaveHeight = height, WaveSpeed = speed, WavePitch = pitch, AnimSpeed = animSpeed };
            }

            var light = new LightSettings { Longitude = 45, Latitude = 45, Diffuse = new Vec3(1,1,1), Ambient = new Vec3(0.3f, 0.3f, 0.3f), Opacity = 1 };
            if (version >= 0x0105)
            {
                int lon = br.ReadInt32();
                int lat = br.ReadInt32();
                var dif = new Vec3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                var amb = new Vec3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                float op = 1.0f;
                
                if (version >= 0x0107) op = br.ReadSingle();

                light = new LightSettings { Longitude = lon, Latitude = lat, Diffuse = dif, Ambient = amb, Opacity = op };
            }

            // v1.6 added 4 unknown integers (ground bounds / quadtree info)
            if (version >= 0x0106)
            {
                br.ReadInt32(); br.ReadInt32(); br.ReadInt32(); br.ReadInt32();
            }
            
            // v2.7 unknowns
            if (version >= 0x0207)
            {
                 int c = br.ReadInt32();
                 if (c > 0) br.ReadBytes(c * 4);
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
                UnknownAfterBuild = unknownV205,
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
                int type = br.ReadInt32();
                if (type == 1) rsw.Objects.Add(ReadModel(br, version));
                else if (type == 2) rsw.Objects.Add(ReadLight(br, version));
                else if (type == 3) rsw.Objects.Add(ReadSound(br, version));
                else if (type == 4) rsw.Objects.Add(ReadEffect(br, version));
                else 
                {
                    // Scan logic or invalid entry
                    rsw.Objects.Add(ReadUnknownObject(br, type));
                    // Usually safer to stop if we hit unknown/corrupt data
                    // break; 
                }
            }

            // Read quadtree if present
            try 
            {
                 // Just consume rest or read floats? BrowEdit reads floats.
                 // We don't use them yet, so leaving as EOF check
            } 
            catch {}

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

        private static RswObject ReadUnknownObject(BinaryReader br, int type)
        {
            return new RswObject { ObjectType = type, Name = $"Unknown_{type}" };
        }

        private static RswModel ReadModel(BinaryReader br, ushort version)
        {
            string name = "";
            int animType = 0;
            float animSpeed = 0;
            int blockType = 0;
            
            if (version >= 0x0103)
            {
                name = ReadFixedString(br, 40);
                animType = br.ReadInt32();
                animSpeed = br.ReadSingle();
                blockType = br.ReadInt32();
            }
            
            if (version >= 0x0206) br.ReadByte(); 
            if (version >= 0x0207) br.ReadInt32();

            string fileName = ReadFixedString(br, 80);
            string objectName = ReadFixedString(br, 80);
            
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
                FileName = fileName,
                Position = pos,
                Rotation = rot,
                Scale = scale
            };
        }

        private static RswLight ReadLight(BinaryReader br, ushort version)
        {
            string name = ReadFixedString(br, 80);
            var pos = new Vec3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            var color = new Vec3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            float range = br.ReadSingle();
            
            return new RswLight { ObjectType = 2, Name = name, Position = pos, Color = color, Range = range };
        }

        private static RswSound ReadSound(BinaryReader br, ushort version)
        {
            string name = ReadFixedString(br, 80);
            string file = ReadFixedString(br, 80);
            var pos = new Vec3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            float vol = br.ReadSingle();
            int w = br.ReadInt32();
            int h = br.ReadInt32();
            float range = br.ReadSingle();
            
            if (version >= 0x0200) br.ReadSingle(); // cycle

            return new RswSound
            {
                ObjectType = 3,
                Name = name,
                FileName = file,
                Position = pos,
                Volume = vol,
                Width = w,
                Height = h,
                Range = range
            };
        }

        private static RswEffect ReadEffect(BinaryReader br, ushort version)
        {
            string name = ReadFixedString(br, 80);
            var pos = new Vec3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            int id = br.ReadInt32();
            float loop = br.ReadSingle();
            float p1 = br.ReadSingle();
            float p2 = br.ReadSingle();
            float p3 = br.ReadSingle();
            float p4 = br.ReadSingle(); 

            return new RswEffect
            {
                ObjectType = 4,
                Name = name,
                Position = pos,
                EffectId = id,
                Loop = loop,
                Param1 = p1
            };
        }
    }
}
