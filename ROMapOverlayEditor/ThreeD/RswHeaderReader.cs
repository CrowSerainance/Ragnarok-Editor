using System;
using System.IO;
using System.Text;

namespace ROMapOverlayEditor.ThreeD
{
    public sealed class RswHeaderInfo
    {
        public string Signature { get; set; } = "";
        public ushort Major { get; set; }
        public ushort Minor { get; set; }
        public long ObjectCountOffset { get; set; }
        public int ObjectCount { get; set; }
    }

    public static class RswHeaderReader
    {
        // Conservative upper bound; real maps are far smaller than this
        private const int MaxReasonableObjects = 200000;

        public static (bool Ok, string Message, RswHeaderInfo? Info) TryRead(byte[] rswBytes)
        {
            try
            {
                if (rswBytes == null || rswBytes.Length < 8)
                    return (false, "RSW file too small (< 8 bytes)", null);

                using var ms = new MemoryStream(rswBytes);
                using var br = new BinaryReader(ms);

                // Signature: 4 bytes ASCII
                var sig = Encoding.ASCII.GetString(br.ReadBytes(4));
                if (sig != "GRSW" && sig != "RSW\0" && sig != "RSW ")
                    return (false, $"Invalid RSW signature: '{sig}'", null);

                // Version: often 2 bytes (major, minor) - but RswIO reads it differently
                // Let's read as two bytes first to match common format
                byte b0 = br.ReadByte();
                byte b1 = br.ReadByte();
                
                // Try both interpretations
                ushort major = b0;
                ushort minor = b1;
                ushort combined = (ushort)((b0 << 8) | b1);

                // Many formats include 40-byte INI-like strings next; but implementations vary.
                // We do NOT fully parse here; we only locate objectCount robustly.

                // Strategy:
                // - Remember current offset as baseline
                // - Try read objectCount at several plausible offsets depending on version,
                //   with sanity checks.

                long basePos = ms.Position;

                // Probe function
                int ProbeAt(long pos)
                {
                    if (pos < 0 || pos + 4 > rswBytes.Length) return -1;
                    ms.Position = pos;
                    return br.ReadInt32();
                }

                // RSW 2.1: Water(24)+Light(36)+MapBoundaries(16)=76 after version; count at 82, list at 86.
                if (combined == 0x0201)
                {
                    int c = ProbeAt(82);
                    if (c >= 0 && c <= MaxReasonableObjects)
                    {
                        return (true, "", new RswHeaderInfo
                        {
                            Signature = sig,
                            Major = major,
                            Minor = minor,
                            ObjectCountOffset = 82,
                            ObjectCount = c
                        });
                    }
                }

                // Commonly, objectCount is after several fixed blocks (long-header layout).
                long[] probes =
                {
                    basePos + 0x100, // common-ish if strings/blocks exist
                    basePos + 0x110,
                    basePos + 0x116, // your error offset — we test it but will reject if insane
                    basePos + 0x120,
                    basePos + 0x124,
                    basePos + 0x128
                };

                foreach (var p in probes)
                {
                    int count = ProbeAt(p);
                    if (count >= 0 && count <= MaxReasonableObjects)
                    {
                        return (true, "", new RswHeaderInfo
                        {
                            Signature = sig,
                            Major = major,
                            Minor = minor,
                            ObjectCountOffset = p,
                            ObjectCount = count
                        });
                    }
                }

                // Realignment attempt for some 2.1x variants:
                // If your parser reads count too early/late, allow shifting +/- 4
                for (int delta = -32; delta <= 32; delta += 4)
                {
                    long p = basePos + 0x116 + delta;
                    int count = ProbeAt(p);
                    if (count >= 0 && count <= MaxReasonableObjects)
                    {
                        return (true, "", new RswHeaderInfo
                        {
                            Signature = sig,
                            Major = major,
                            Minor = minor,
                            ObjectCountOffset = p,
                            ObjectCount = count
                        });
                    }
                }

                return (false,
                    $"RSW header read but objectCount could not be located safely.\n" +
                    $"Signature={sig}, Version={major}.{minor} (combined=0x{combined:X4})\n" +
                    $"FileLen={rswBytes.Length} bytes\n" +
                    "This indicates a version/layout mismatch in your RSW parser.\n" +
                    "Next step: align your parser to the exact version structure (2.0x/2.1x).",
                    null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, null);
            }
        }
    }
}
