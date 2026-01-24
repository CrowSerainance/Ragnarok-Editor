using System;
using System.IO;

namespace ROMapOverlayEditor.ThreeD
{
    public sealed class RswObjectListLocation
    {
        public int ObjectCount { get; set; }
        public long CountOffset { get; set; }
        public long ListStartOffset { get; set; }
        public string Note { get; set; } = "";
    }

    public static class RswObjectListLocator
    {
        // Known object types for common RSW versions
        // 1 = MODEL, 2 = LIGHT, 3 = SOUND, 4 = EFFECT (typical RO)
        private static bool IsKnownType(int t) => t >= 1 && t <= 4;

        public static (bool Ok, string Message, RswObjectListLocation? Loc) TryLocate(byte[] rswBytes, long objectCountOffsetGuess)
        {
            try
            {
                using var ms = new MemoryStream(rswBytes);
                using var br = new BinaryReader(ms);

                if (objectCountOffsetGuess < 0 || objectCountOffsetGuess + 4 > rswBytes.Length)
                    return (false, $"ObjectCountOffset out of bounds: 0x{objectCountOffsetGuess:X}", null);

                // Read count at the guessed offset
                ms.Position = objectCountOffsetGuess;
                int count = br.ReadInt32();

                if (count < 0 || count > 200000)
                {
                    return (false,
                        $"Object count is not sane at 0x{objectCountOffsetGuess:X}: {count}\n" +
                        "This indicates the count offset guess is wrong for this RSW layout.",
                        null);
                }

                long listStart = objectCountOffsetGuess + 4;

                // Validate first object type
                if (listStart + 4 > rswBytes.Length)
                    return (false, "Object list start is out of bounds.", null);

                ms.Position = listStart;
                int firstType = br.ReadInt32();

                if (IsKnownType(firstType))
                {
                    return (true, "", new RswObjectListLocation
                    {
                        ObjectCount = count,
                        CountOffset = objectCountOffsetGuess,
                        ListStartOffset = listStart,
                        Note = "ListStart = CountOffset + 4"
                    });
                }

                // Resync scan: some versions have extra header fields between count and list
                // Scan forward up to +0x200, aligned on 4 bytes, for a plausible first object type
                long scanStart = listStart;
                long scanEnd = Math.Min(rswBytes.Length - 4, listStart + 0x200);

                for (long p = scanStart; p <= scanEnd; p += 4)
                {
                    ms.Position = p;
                    int t = br.ReadInt32();
                    if (IsKnownType(t))
                    {
                        return (true, "", new RswObjectListLocation
                        {
                            ObjectCount = count,
                            CountOffset = objectCountOffsetGuess,
                            ListStartOffset = p,
                            Note = $"Resynced: first type={t} at 0x{p:X} (scan +0x{p - listStart:X})"
                        });
                    }
                }

                return (false,
                    $"Could not locate object list start.\n" +
                    $"CountOffset guess: 0x{objectCountOffsetGuess:X}, count={count}\n" +
                    $"First int at CountOffset+4 was {firstType} (unknown).\n" +
                    "No plausible object type (1..4) was found within +0x200 bytes.\n" +
                    "Next step: align RSW header parsing for this version to get the correct count offset.",
                    null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, null);
            }
        }
    }
}
