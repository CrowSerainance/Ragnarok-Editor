using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ROMapOverlayEditor.Rsw
{
    public static class RswIO
    {
        public static bool LooksLikeRsw(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 8) return false;
            // "GRSW"
            return bytes[0] == (byte)'G' && bytes[1] == (byte)'R' && bytes[2] == (byte)'S' && bytes[3] == (byte)'W';
        }

        public static RswFile Read(byte[] bytes)
        {
            // Validate header first using diagnostic reader
            var (headerOk, headerMsg, headerInfo) = ROMapOverlayEditor.ThreeD.RswHeaderReader.TryRead(bytes);
            if (!headerOk)
            {
                throw new InvalidDataException($"RSW header validation failed: {headerMsg}");
            }

            using var ms = new MemoryStream(bytes, writable: false);
            using var br = new BinaryReader(ms);

            var header = br.ReadBytes(4);
            if (header.Length != 4 || header[0] != 'G' || header[1] != 'R' || header[2] != 'S' || header[3] != 'W')
                throw new InvalidDataException("Not an RSW (missing GRSW).");

            byte b0 = br.ReadByte();
            byte b1 = br.ReadByte();
            ushort version = (ushort)((b0 << 8) | b1); // 0xMMmm

            // Sanity check version
            if (version > 0x0300) 
                throw new InvalidDataException($"RSW version 0x{version:X4} is too new or invalid.");

            int? buildNumber = null;
            if (version >= 0x0205)
                buildNumber = br.ReadInt32();

            if (version >= 0x0202)
                br.ReadByte();

            string ini1 = ReadRoStr(br, 40);
            string gnd = ReadRoStr(br, 40);

            string gat;
            if (version >= 0x0104)
                gat = ReadRoStr(br, 40);
            else
                gat = gnd; // legacy mapping

            string ini2 = ReadRoStr(br, 40);

            // Detailed Water/Light/Ground block skipping
            // NOTE: Older versions (<2.0) vary wildly. 
            // 2.1 is common.

            if (version >= 0x0103) br.ReadSingle(); // water height

            if (version >= 0x0108) // water properties
            {
                br.ReadInt32(); // type
                br.ReadSingle(); // amp
                br.ReadSingle(); // phase
                br.ReadSingle(); // curve
            }

            if (version >= 0x0109) br.ReadInt32(); // anim speed

            if (version >= 0x0108) ReadRoStr(br, 32); // water texture

            if (version >= 0x0105) // Light info
            {
                br.ReadInt32(); // long
                br.ReadInt32(); // lat
                ReadVec3(br);   // diffuse
                ReadVec3(br);   // ambient
                if (version >= 0x0107) // unknown float (shadow opacity?) - ONLY 1.7+
                    br.ReadSingle();
            }

            if (version >= 0x0106) // Ground/BBox
            {
                br.ReadInt32();
                br.ReadInt32();
                br.ReadInt32();
                br.ReadInt32();
            }

            // Object count is at the current position after the full header parse.
            // Use this exact offset — do NOT use RswHeaderReader's probed offset, which can
            // land on the wrong place for some version layouts (e.g. "Unknown type 1701244").
            long objectCountOffsetGuess = ms.Position;

            // Use object list locator to find the correct list start (count at Guess, list at Guess+4 or resync)
            var (locOk, locMsg, locInfo) = ROMapOverlayEditor.ThreeD.RswObjectListLocator.TryLocate(bytes, objectCountOffsetGuess);
            if (!locOk)
            {
                throw new InvalidDataException(
                    $"Failed to locate RSW object list:\n{locMsg}\n\n" +
                    $"Parser reached offset 0x{ms.Position:X} before object count.\n" +
                    $"This indicates a version/layout mismatch. The RSW structure may differ from expected.");
            }

            int objectCount = locInfo!.ObjectCount;
            long objectListStart = locInfo.ListStartOffset;

            // Move to the correct object list start position
            ms.Position = objectListStart;

            var objects = new List<RswObject>(Math.Max(0, objectCount));

            // Read objects starting at the correctly located offset
            for (int i = 0; i < objectCount; i++)
            {
                // Safety: check EOF
                if (ms.Position >= ms.Length - 4) break;

                long startPos = ms.Position;
                int type = br.ReadInt32();

                // Validate type before attempting to read
                if (type < 1 || type > 4)
                {
                    throw new InvalidDataException(
                        $"Unknown RSW object type {type} at index {i} (Offset 0x{startPos:X}, Version 0x{version:X4}).\n" +
                        $"Expected types: 1=Model, 2=Light, 3=Sound, 4=Effect.\n" +
                        $"Object list location note: {locInfo.Note}");
                }

                try
                {
                    switch (type)
                    {
                        case 1:
                            objects.Add(ReadModel(br, version, buildNumber));
                            break;
                        case 2:
                            objects.Add(ReadLight(br, version));
                            break;
                        case 3:
                            objects.Add(ReadSound(br));
                            break;
                        case 4:
                            objects.Add(ReadEffect(br));
                            break;
                    }
                }
                catch (Exception ex)
                {
                     throw new InvalidDataException($"Failed to read object {i} (Type {type}): {ex.Message}", ex);
                }
            }

            return new RswFile
            {
                Version = version,
                BuildNumber = buildNumber,
                IniFile1 = ini1,
                GndFile = gnd,
                GatFile = gat,
                IniFile2 = ini2,
                ObjectCount = objectCount,
                Objects = objects
            };
        }

        private static RswModel ReadModel(BinaryReader br, ushort version, int? buildNumber)
        {
            string name = (version >= 0x0103) ? ReadRoStr(br, 40) : "";

            int animType = (version >= 0x0103) ? br.ReadInt32() : 0;
            float animSpeed = (version >= 0x0103) ? br.ReadSingle() : 0;
            int blockType = (version >= 0x0103) ? br.ReadInt32() : 0;

            byte? u206 = null;
            if (version >= 0x0206 && (buildNumber ?? 0) > 161)
                u206 = br.ReadByte();

            string filename = ReadRoStr(br, 40);
            string objName = ReadRoStr(br, 80);

            var pos = ReadVec3(br);
            var rot = ReadVec3(br);
            var scale = ReadVec3(br);

            return new RswModel
            {
                Type = 1,
                Name = name,
                AnimationType = animType,
                AnimationSpeed = animSpeed,
                BlockType = blockType,
                Unknown206 = u206,
                Filename = filename,
                ObjectName = objName,
                Position = pos,
                Rotation = rot,
                Scale = scale
            };
        }

        private static RswLight ReadLight(BinaryReader br, ushort version)
        {
            string name = (version >= 0x0103) ? ReadRoStr(br, 40) : "";
            var pos = ReadVec3(br);
            var unk10 = br.ReadBytes(10);
            var color = ReadVec3(br);
            float range = br.ReadSingle();

            return new RswLight
            {
                Type = 2,
                Name = name,
                Position = pos,
                Unknown10 = unk10.Length == 10 ? unk10 : new byte[10],
                Color = color,
                Range = range
            };
        }

        private static RswSound ReadSound(BinaryReader br)
        {
            string name = ReadRoStr(br, 80);
            string filename = ReadRoStr(br, 40);
            float u1 = br.ReadSingle();
            float u2 = br.ReadSingle();
            var rot = ReadVec3(br);
            var scale = ReadVec3(br);
            var unk8 = br.ReadBytes(8);
            var pos = ReadVec3(br);
            float vol = br.ReadSingle();
            float w = br.ReadSingle();
            float h = br.ReadSingle();
            float range = br.ReadSingle();

            return new RswSound
            {
                Type = 3,
                Name = name,
                Filename = filename,
                Unknown1 = u1,
                Unknown2 = u2,
                Rotation = rot,
                Scale = scale,
                Unknown8 = unk8.Length == 8 ? unk8 : new byte[8],
                Position = pos,
                Volume = vol,
                Width = w,
                Height = h,
                Range = range
            };
        }

        private static RswEffect ReadEffect(BinaryReader br)
        {
            string name = ReadRoStr(br, 80);
            var pos = ReadVec3(br);
            int id = br.ReadInt32();
            float loop = br.ReadSingle();
            float u1 = br.ReadSingle();
            float u2 = br.ReadSingle();
            float u3 = br.ReadSingle();
            float u4 = br.ReadSingle();

            return new RswEffect
            {
                Type = 4,
                Name = name,
                Position = pos,
                Id = id,
                Loop = loop,
                Unknown1 = u1,
                Unknown2 = u2,
                Unknown3 = u3,
                Unknown4 = u4
            };
        }

        private static Vec3 ReadVec3(BinaryReader br)
        {
            float x = br.ReadSingle();
            float y = br.ReadSingle();
            float z = br.ReadSingle();
            return new Vec3(x, y, z);
        }

        private static string ReadRoStr(BinaryReader br, int fixedBytes)
        {
            var data = br.ReadBytes(fixedBytes);
            if (data.Length != fixedBytes)
                throw new EndOfStreamException($"Unexpected EOF in RoStr({fixedBytes}).");

            int len = Array.IndexOf<byte>(data, 0);
            if (len < 0) len = data.Length;

            return Encoding.ASCII.GetString(data, 0, len).Trim();
        }
    }
}
