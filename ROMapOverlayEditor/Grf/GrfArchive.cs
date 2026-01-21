using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace ROMapOverlayEditor.Grf
{
    public sealed class GrfArchive : IDisposable
    {
        public string Path { get; }
        public string VersionHex { get; }
        public uint FileTableOffset { get; }
        public List<GrfEntry> Entries { get; } = new();

        private readonly FileStream _fs;
        private readonly object _lock = new();

        private GrfArchive(string path, FileStream fs, string versionHex, uint fileTableOffset, List<GrfEntry> entries)
        {
            Path = path;
            _fs = fs;
            VersionHex = versionHex;
            FileTableOffset = fileTableOffset;
            Entries = entries;
        }

        public void Dispose()
        {
            _fs?.Dispose();
        }

        public static GrfValidationResult TryValidate(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return GrfValidationResult.Fail("No file selected.");
            if (!File.Exists(path)) return GrfValidationResult.Fail("File does not exist.");

            var fi = new FileInfo(path);
            if (fi.Length < 64) return GrfValidationResult.Fail("File too small to be a valid GRF.");

            try
            {
                using var fs = File.OpenRead(path);
                using var br = new BinaryReader(fs);

                // Signature: 15 bytes ASCII "Master of Magic"
                var sigBytes = br.ReadBytes(15);
                var sig = Encoding.ASCII.GetString(sigBytes);
                if (!sig.Equals("Master of Magic", StringComparison.Ordinal))
                    return GrfValidationResult.Fail("Invalid signature (expected 'Master of Magic').");

                // Header layout (common GRF):
                // 0x00: 15  Signature
                // 0x0F: 15  Key (unused/padding)
                // 0x1E: 4   FileTableOffset (relative to header end)
                // 0x22: 4   Reserved / Seed
                // 0x26: 4   FileCount (or reserved depending on variant)
                // 0x2A: 4   Version
                fs.Position = 15 + 15;

                uint fileTableOffset = br.ReadUInt32();
                uint reserved = br.ReadUInt32();
                uint fileCount = br.ReadUInt32();
                uint version = br.ReadUInt32();

                long fileSize = fi.Length;

                if (!IsValidOffset(fileTableOffset, fileSize))
                {
                    return GrfValidationResult.Fail(
                        "Failed to open GRF: invalid file table offset.\n\n" +
                        $"File: {System.IO.Path.GetFileName(path)}\n" +
                        $"FileSize: {fileSize} bytes\n" +
                        $"FileTableOffset(rel): {fileTableOffset}\n" +
                        $"FileTableOffset(abs): {46 + (long)fileTableOffset}\n" +
                        $"Version: 0x{version:X}\n\n" +
                        "This usually means:\n" +
                        "- The GRF is corrupt, OR\n" +
                        "- The header layout differs (rare), OR\n" +
                        "- You selected a non-GRF file.");
                }

                return GrfValidationResult.Success();
            }
            catch (Exception ex)
            {
                return GrfValidationResult.Fail(ex.Message);
            }
        }

        private static bool IsValidOffset(uint offsetRelToHeaderEnd, long fileSize)
        {
            long abs = 46L + offsetRelToHeaderEnd; // 46 = header length used by this reader
            return abs >= 46 && abs < fileSize;
        }

        public static GrfArchive Open(string path)
        {
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                var br = new BinaryReader(fs);

                fs.Position = 0;
                var sig = Encoding.ASCII.GetString(br.ReadBytes(15));
                if (!sig.Equals("Master of Magic", StringComparison.Ordinal))
                    throw new InvalidDataException("Invalid GRF signature (expected 'Master of Magic').");

                fs.Position = 15 + 15;

                uint fileTableOffset = br.ReadUInt32();
                uint reserved = br.ReadUInt32();
                uint fileCount = br.ReadUInt32();
                uint ver = br.ReadUInt32();

                long len = fs.Length;

                if (!IsValidOffset(fileTableOffset, len))
                {
                    throw new InvalidDataException(
                        $"Invalid file table offset.\n" +
                        $"FileSize: {len}\n" +
                        $"FileTableOffset(rel): {fileTableOffset}\n" +
                        $"FileTableOffset(abs): {46 + (long)fileTableOffset}\n" +
                        $"Version: 0x{ver:X}");
                }

                // Parse table
                fs.Position = 46L + fileTableOffset;

                int compressedSize = br.ReadInt32();
                int decompressedSize = br.ReadInt32();

                if (compressedSize <= 0 || decompressedSize <= 0)
                    throw new InvalidDataException($"Invalid file table sizes. comp={compressedSize}, decomp={decompressedSize}");

                byte[] compressed = br.ReadBytes(compressedSize);
                if (compressed.Length != compressedSize)
                    throw new EndOfStreamException("Truncated GRF file table data.");

                byte[] tableData = DecompressZlib(compressed, decompressedSize);

                var entries = ParseTable(tableData, decompressedSize);

                return new GrfArchive(path, fs, $"0x{ver:X}", fileTableOffset, entries);
            }
            catch
            {
                fs.Dispose();
                throw;
            }
        }

        private static byte[] DecompressZlib(byte[] data, int expectedSize)
        {
            using var ms = new MemoryStream(data);
            // standard zlib header usually 0x78 0x9C or similar; DeflateStream might need skipping header?
            // Dotnet DeflateStream expects RAW deflate. ZLibStream (if avail) handles Zlib.
            // If we are on modern .NET, ZLibStream is available?
            // Attempt standard DeflateStream skipping 2 bytes if needed?
            // Actually, System.IO.Compression.ZLibStream (NET 6+) handles it.
            // Assuming this project is NET 8 (from previous context).
            
            using var zs = new ZLibStream(ms, CompressionMode.Decompress);
            using var outMs = new MemoryStream(expectedSize);
            zs.CopyTo(outMs);
            return outMs.ToArray();
        }

        private static List<GrfEntry> ParseTable(byte[] table, int size)
        {
            var list = new List<GrfEntry>();
            int i = 0;
            // Rows: string null, then 17 bytes info
            while (i < size)
            {
                int start = i;
                while (i < size && table[i] != 0) i++;
                if (i >= size) break;
                
                string fname = Encoding.GetEncoding(949).GetString(table, start, i - start); // EUC-KR usually for RO
                // If EUC-KR fails we can fallback to ASCII/Latin1, but 949 covers standard + korean
                if (string.IsNullOrEmpty(fname)) fname = Encoding.ASCII.GetString(table, start, i - start);

                i++; // skip null

                if (i + 17 > size) break;

                // 17 bytes struct:
                // param1 (4), param2 (4), decompLen (4), flags (1), offset (4)
                uint compSize = BitConverter.ToUInt32(table, i);
                uint alignSize = BitConverter.ToUInt32(table, i + 4);
                uint decompSize = BitConverter.ToUInt32(table, i + 8);
                byte flags = table[i + 12];
                uint offset = BitConverter.ToUInt32(table, i + 13);
                i += 17;

                list.Add(new GrfEntry
                {
                    Path = fname,
                    CompressedSize = compSize,
                    AlignedSize = alignSize,
                    UncompressedSize = decompSize,
                    Flags = flags,
                    Offset = offset
                });
            }
            return list;
        }

        public byte[] Extract(string internalPath)
        {
            var e = Entries.FirstOrDefault(x => x.Path.Equals(internalPath, StringComparison.OrdinalIgnoreCase));
            if (e == null) throw new FileNotFoundException("Entry not found: " + internalPath);

            lock(_lock)
            {
                long absPos = 46 + e.Offset;
                _fs.Position = absPos;

                // Read aligned size from disk (safe padded block)
                int len = (int)e.AlignedSize;
                
                // Safety clamp
                if (len < 0) len = (int)e.CompressedSize;
                if (len <= 0) return Array.Empty<byte>();

                byte[] raw = new byte[len];
                int read = _fs.Read(raw, 0, len);
                if (read < len) 
                {
                    // Truncated? Resize to actual read if needed
                    Array.Resize(ref raw, read);
                }
                
                // Compression check: Flag 8 OR different sizes
                bool isCompressed = (e.Flags & 8) != 0 || (e.CompressedSize != e.UncompressedSize);

                if (isCompressed)
                {
                    try { return DecompressZlib(raw, (int)e.UncompressedSize); }
                    catch { return raw; } // fallback to returning raw if decompression fails
                }
                return raw;
            }
        }
        
        public byte[] ExtractByAligned(GrfEntry e, uint alignedSize)
        {
             return new byte[0];
        }
    }
}
