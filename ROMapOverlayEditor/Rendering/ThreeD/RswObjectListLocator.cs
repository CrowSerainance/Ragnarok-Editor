// File: ROMapOverlayEditor/ROMapOverlayEditor/ThreeD/RswObjectListLocator.cs
using System;
using System.IO;
using System.Text;

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
        // Typical RO RSW object types:
        // 1 = Model, 2 = Light, 3 = Sound, 4 = Effect
        private static bool IsKnownType(int t) => t >= 1 && t <= 4;

        // Heuristic limits (safe for real maps)
        private const int MaxReasonableObjects = 200000; // BrowEdit can show thousands; keep wide but bounded
        private const int MinHeaderScan = 16;
        private const int MaxHeaderScan = 4096;

        public static (bool Ok, string Message, RswObjectListLocation? Loc) TryLocate(byte[] rswBytes, long objectCountOffsetGuess)
        {
            // Keep old signature used elsewhere, but upgrade behavior:
            // - Try the guess first, then scan if guess fails.
            var guess = TryLocateAt(rswBytes, objectCountOffsetGuess, note: $"Guess@0x{objectCountOffsetGuess:X}");
            if (guess.Ok) return guess;

            var scan = TryLocateByScan(rswBytes);
            if (scan.Ok) return scan;

            return (false, $"RSW object list locate failed. Guess: {guess.Message}. Scan: {scan.Message}.", null);
        }

        public static (bool Ok, string Message, RswObjectListLocation? Loc) TryLocateByScan(byte[] rswBytes)
        {
            try
            {
                if (rswBytes == null || rswBytes.Length < 16)
                    return (false, "RSW too short", null);

                // Basic magic check
                if (!(rswBytes[0] == (byte)'G' && rswBytes[1] == (byte)'R' && rswBytes[2] == (byte)'S' && rswBytes[3] == (byte)'W'))
                    return (false, "Not an RSW (missing GRSW)", null);

                long scanEnd = Math.Min(rswBytes.Length - 8, MaxHeaderScan);
                long bestScore = long.MinValue;
                RswObjectListLocation? best = null;

                // Scan every 4 bytes (counts are int32 aligned in practice)
                for (long off = MinHeaderScan; off <= scanEnd; off += 4)
                {
                    var candidate = TryLocateAt(rswBytes, off, note: $"Scan@0x{off:X}");
                    if (!candidate.Ok || candidate.Loc == null) continue;

                    // Score: prefer earlier offsets and more plausible lists
                    // (lower offset = more likely header->count)
                    long score = 0;
                    score -= off; // earlier is better
                    score += Math.Min(candidate.Loc.ObjectCount, 10000) * 10;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = candidate.Loc;
                    }
                }

                if (best == null)
                    return (false, "Scan found no plausible object list.", null);

                best.Note = $"{best.Note} | BestScore={bestScore}";
                return (true, $"Located object list via scan at 0x{best.CountOffset:X}", best);
            }
            catch (Exception ex)
            {
                return (false, $"Scan exception: {ex.Message}", null);
            }
        }

        private static (bool Ok, string Message, RswObjectListLocation? Loc) TryLocateAt(byte[] rswBytes, long countOffset, string note)
        {
            try
            {
                if (countOffset < 0 || countOffset + 4 > rswBytes.Length)
                    return (false, $"CountOffset out of bounds: 0x{countOffset:X}", null);

                using var ms = new MemoryStream(rswBytes, writable: false);
                using var br = new BinaryReader(ms);

                ms.Position = countOffset;
                int count = br.ReadInt32();

                if (count < 0 || count > MaxReasonableObjects)
                    return (false, $"{note}: count {count} out of range", null);

                long listStart = countOffset + 4;
                if (listStart < 0 || listStart >= rswBytes.Length)
                    return (false, $"{note}: listStart out of bounds", null);

                // Validate by probing first N objects.
                // If count == 0, still accept if next bytes look sane (rare maps can have 0 objects).
                int probeObjects = Math.Min(Math.Max(count, 1), 8);
                ms.Position = listStart;

                int valid = 0;
                for (int i = 0; i < probeObjects; i++)
                {
                    if (ms.Position + 4 > ms.Length) break;
                    long objPos = ms.Position;
                    int type = br.ReadInt32();

                    if (!IsKnownType(type))
                    {
                        // If count == 0 we don't require types, but for non-zero we do.
                        if (count == 0) break;
                        return (false, $"{note}: first types invalid (type={type} @0x{objPos:X})", null);
                    }

                    // Minimal skip heuristic:
                    // We can't fully parse here (that’s RswIO’s job), but we can check that the next bytes include a valid RO string length block.
                    // Many objects begin with Name (40 bytes) then some floats.
                    // We'll just ensure we can read a 40-byte name safely.
                    if (ms.Position + 40 > ms.Length) return (false, $"{note}: truncated after type", null);
                    var nameBytes = br.ReadBytes(40);
                    if (nameBytes.Length != 40) return (false, $"{note}: name truncated", null);

                    valid++;
                    // Rewind: we should not advance too far; this is only a plausibility check.
                    // To keep scan fast, stop after first object.
                    break;
                }

                // Accept if:
                // - count == 0 (objectless map), or
                // - first object looks plausible
                if (count == 0 || valid > 0)
                {
                    return (true, "OK", new RswObjectListLocation
                    {
                        ObjectCount = count,
                        CountOffset = countOffset,
                        ListStartOffset = listStart,
                        Note = note
                    });
                }

                return (false, $"{note}: no valid probe objects", null);
            }
            catch (Exception ex)
            {
                return (false, $"{note}: exception {ex.Message}", null);
            }
        }
    }
}
