using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace ROMapOverlayEditor.Grf
{
    /// <summary>
    /// GRF archive reader with support for large files (>2GB).
    /// Handles GRF version 0x200 (standard RO format).
    /// </summary>
    public sealed class GrfArchive : IDisposable
    {
        // ====================================================================
        // CONSTANTS
        // ====================================================================
        
        /// <summary>GRF header size in bytes</summary>
        private const int HEADER_SIZE = 46;
        
        /// <summary>Expected signature string</summary>
        private const string SIGNATURE = "Master of Magic";
        
        /// <summary>Entry info block size (after filename)</summary>
        private const int ENTRY_INFO_SIZE = 17;

        // ====================================================================
        // PROPERTIES
        // ====================================================================
        
        /// <summary>Path to the GRF file</summary>
        public string Path { get; }
        
        /// <summary>Path to the GRF file (alias for compatibility)</summary>
        public string FilePath => Path;
        
        /// <summary>GRF version number</summary>
        public uint Version { get; }
        
        /// <summary>GRF version as hex string</summary>
        public string VersionHex { get; }
        
        /// <summary>File table offset (relative to header end)</summary>
        public uint FileTableOffset { get; }
        
        /// <summary>List of all entries in the archive</summary>
        public List<GrfEntry> Entries { get; } = new();
        
        /// <summary>List of all entries (for compatibility with existing code)</summary>
        public IReadOnlyList<GrfEntry> EntriesList => Entries;
        
        /// <summary>Dictionary lookup for fast access (for compatibility)</summary>
        public IReadOnlyDictionary<string, GrfEntry> EntriesDict => _entryLookup;
        
        /// <summary>Total file size</summary>
        public long FileSize { get; }

        // ====================================================================
        // PRIVATE FIELDS
        // ====================================================================
        
        private readonly FileStream _fs;
        private readonly object _lock = new();
        private readonly Dictionary<string, GrfEntry> _entryLookup;
        private static Encoding _koreanEncoding;

        // ====================================================================
        // STATIC CONSTRUCTOR
        // ====================================================================
        
        static GrfArchive()
        {
            // Register encoding provider for Korean codepage
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            try
            {
                _koreanEncoding = Encoding.GetEncoding(949);
            }
            catch
            {
                _koreanEncoding = Encoding.UTF8;
            }
        }

        // ====================================================================
        // CONSTRUCTOR (Private - use Open() factory method)
        // ====================================================================
        
        private GrfArchive(
            string path, 
            FileStream fs, 
            uint version, 
            string versionHex, 
            uint fileTableOffset,
            long fileSize,
            List<GrfEntry> entries)
        {
            Path = path;
            _fs = fs;
            Version = version;
            VersionHex = versionHex;
            FileTableOffset = fileTableOffset;
            FileSize = fileSize;
            Entries = entries;
            
            // Build lookup dictionary for fast access
            _entryLookup = new Dictionary<string, GrfEntry>(
                entries.Count, 
                StringComparer.OrdinalIgnoreCase);
            
            foreach (var entry in entries)
            {
                // Normalize path separators
                var normalizedPath = entry.Path.Replace('/', '\\').ToLowerInvariant();
                _entryLookup.TryAdd(normalizedPath, entry);
                
                // Also add with original path
                if (!_entryLookup.ContainsKey(entry.Path))
                    _entryLookup.TryAdd(entry.Path, entry);
            }
        }

        // ====================================================================
        // DISPOSE
        // ====================================================================
        
        public void Dispose()
        {
            _fs?.Dispose();
        }

        // ====================================================================
        // VALIDATION
        // ====================================================================
        
        /// <summary>
        /// Validate a GRF file without fully opening it.
        /// </summary>
        public static GrfValidationResult TryValidate(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) 
                return GrfValidationResult.Fail("No file selected.");
            
            if (!File.Exists(path)) 
                return GrfValidationResult.Fail("File does not exist.");

            var fi = new FileInfo(path);
            if (fi.Length < HEADER_SIZE) 
                return GrfValidationResult.Fail("File too small to be a valid GRF.");

            try
            {
                using var fs = File.OpenRead(path);
                using var br = new BinaryReader(fs);

                // Read and validate signature
                var sigBytes = br.ReadBytes(15);
                var sig = Encoding.ASCII.GetString(sigBytes);
                if (!sig.Equals(SIGNATURE, StringComparison.Ordinal))
                    return GrfValidationResult.Fail($"Invalid signature (expected '{SIGNATURE}').");

                // Skip key bytes
                fs.Position = 30; // 15 (sig) + 15 (key)

                // Read header values
                uint fileTableOffset = br.ReadUInt32();
                uint reserved = br.ReadUInt32();
                uint fileCount = br.ReadUInt32();
                uint version = br.ReadUInt32();

                long fileSize = fi.Length;

                // Validate file table offset
                // IMPORTANT: Use long arithmetic to prevent overflow!
                long absoluteOffset = HEADER_SIZE + (long)fileTableOffset;
                
                if (absoluteOffset < HEADER_SIZE || absoluteOffset >= fileSize)
                {
                    return GrfValidationResult.Fail(
                        $"Invalid file table offset.\n\n" +
                        $"File: {System.IO.Path.GetFileName(path)}\n" +
                        $"FileSize: {fileSize:N0} bytes\n" +
                        $"FileTableOffset(rel): {fileTableOffset:N0}\n" +
                        $"FileTableOffset(abs): {absoluteOffset:N0}\n" +
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
                return GrfValidationResult.Fail($"Validation error: {ex.Message}");
            }
        }

        // ====================================================================
        // OPEN FACTORY METHOD
        // ====================================================================
        
        /// <summary>
        /// Open a GRF archive for reading.
        /// </summary>
        /// <param name="path">Path to the GRF file</param>
        /// <returns>Opened GRF archive</returns>
        public static GrfArchive Open(string path)
        {
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                var br = new BinaryReader(fs);
                long fileSize = fs.Length;

                // ============================================================
                // READ HEADER
                // ============================================================
                
                fs.Position = 0;
                var sig = Encoding.ASCII.GetString(br.ReadBytes(15));
                if (!sig.Equals(SIGNATURE, StringComparison.Ordinal))
                    throw new InvalidDataException($"Invalid GRF signature (expected '{SIGNATURE}').");

                // Skip key bytes, position at offset fields
                fs.Position = 30;

                uint fileTableOffset = br.ReadUInt32();
                uint reserved = br.ReadUInt32();
                uint fileCount = br.ReadUInt32();
                uint version = br.ReadUInt32();

                // ============================================================
                // VALIDATE FILE TABLE OFFSET
                // ============================================================
                
                // CRITICAL: Use long arithmetic to prevent overflow on large files!
                long tableAbsoluteOffset = HEADER_SIZE + (long)fileTableOffset;
                
                if (tableAbsoluteOffset < HEADER_SIZE || tableAbsoluteOffset >= fileSize)
                {
                    throw new InvalidDataException(
                        $"Invalid file table offset.\n" +
                        $"FileSize: {fileSize:N0}\n" +
                        $"FileTableOffset(rel): {fileTableOffset:N0}\n" +
                        $"FileTableOffset(abs): {tableAbsoluteOffset:N0}\n" +
                        $"Version: 0x{version:X}");
                }

                // ============================================================
                // READ FILE TABLE
                // ============================================================
                
                fs.Position = tableAbsoluteOffset;

                // Table header: compressed size (4) + decompressed size (4)
                int compressedSize = br.ReadInt32();
                int decompressedSize = br.ReadInt32();

                if (compressedSize <= 0 || decompressedSize <= 0)
                {
                    throw new InvalidDataException(
                        $"Invalid file table sizes. compressed={compressedSize}, decompressed={decompressedSize}");
                }

                // Check we have enough bytes
                long remainingBytes = fileSize - fs.Position;
                if (remainingBytes < compressedSize)
                {
                    throw new InvalidDataException(
                        $"File table extends past end of file. " +
                        $"Need {compressedSize} bytes, have {remainingBytes}");
                }

                byte[] compressedTable = br.ReadBytes(compressedSize);
                if (compressedTable.Length != compressedSize)
                {
                    throw new EndOfStreamException(
                        $"Truncated GRF file table. Expected {compressedSize}, got {compressedTable.Length}");
                }

                // ============================================================
                // DECOMPRESS FILE TABLE
                // ============================================================
                
                byte[] tableData = DecompressZlib(compressedTable, decompressedSize);

                // ============================================================
                // PARSE ENTRIES
                // ============================================================
                
                var entries = ParseFileTable(tableData, version);

                // ============================================================
                // BUILD ARCHIVE OBJECT
                // ============================================================
                
                return new GrfArchive(
                    path, 
                    fs, 
                    version, 
                    $"0x{version:X}", 
                    fileTableOffset,
                    fileSize,
                    entries);
            }
            catch
            {
                fs.Dispose();
                throw;
            }
        }

        // ====================================================================
        // FILE TABLE PARSING
        // ====================================================================
        
        private static List<GrfEntry> ParseFileTable(byte[] table, uint version)
        {
            var entries = new List<GrfEntry>();
            int position = 0;
            int tableSize = table.Length;

            while (position < tableSize)
            {
                // --------------------------------------------------------
                // READ FILENAME (null-terminated string)
                // --------------------------------------------------------
                
                int nameStart = position;
                while (position < tableSize && table[position] != 0)
                    position++;

                if (position >= tableSize)
                    break;

                string filename;
                int nameLength = position - nameStart;
                
                if (nameLength > 0)
                {
                    try
                    {
                        filename = _koreanEncoding.GetString(table, nameStart, nameLength);
                    }
                    catch
                    {
                        filename = Encoding.ASCII.GetString(table, nameStart, nameLength);
                    }
                }
                else
                {
                    filename = string.Empty;
                }

                position++; // Skip null terminator

                // --------------------------------------------------------
                // READ ENTRY INFO (17 bytes)
                // --------------------------------------------------------
                
                if (position + ENTRY_INFO_SIZE > tableSize)
                    break;

                // Structure:
                // - compressedSize (4 bytes) - includes some header overhead
                // - alignedSize (4 bytes) - aligned/padded size
                // - uncompressedSize (4 bytes) - actual data size
                // - flags (1 byte) - compression flags
                // - offset (4 bytes) - offset from header end
                
                uint compressedSize = BitConverter.ToUInt32(table, position);
                uint alignedSize = BitConverter.ToUInt32(table, position + 4);
                uint uncompressedSize = BitConverter.ToUInt32(table, position + 8);
                byte flags = table[position + 12];
                uint offset = BitConverter.ToUInt32(table, position + 13);
                
                position += ENTRY_INFO_SIZE;

                // Skip empty or directory entries
                if (string.IsNullOrEmpty(filename) || uncompressedSize == 0)
                    continue;

                // --------------------------------------------------------
                // CALCULATE ACTUAL COMPRESSED SIZE
                // --------------------------------------------------------
                
                // GRF 0x200 stores: compressedSize = actualCompressed + alignedSize + 715
                // We need to extract the actual compressed data size
                uint actualCompressedSize;
                
                if (compressedSize > alignedSize + 715)
                {
                    actualCompressedSize = compressedSize - alignedSize - 715;
                }
                else
                {
                    // Fallback for unusual entries
                    actualCompressedSize = compressedSize;
                }

                entries.Add(new GrfEntry
                {
                    Path = filename,
                    CompressedSize = actualCompressedSize,
                    AlignedSize = alignedSize,
                    UncompressedSize = uncompressedSize,
                    Flags = flags,
                    Offset = offset
                });
            }

            return entries;
        }

        // ====================================================================
        // DECOMPRESSION
        // ====================================================================
        
        private static byte[] DecompressZlib(byte[] data, int expectedSize)
        {
            try
            {
                using var inputStream = new MemoryStream(data);
                using var zlibStream = new ZLibStream(inputStream, CompressionMode.Decompress);
                using var outputStream = new MemoryStream(expectedSize);
                
                zlibStream.CopyTo(outputStream);
                return outputStream.ToArray();
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Zlib decompression failed: {ex.Message}", ex);
            }
        }

        // ====================================================================
        // FILE EXTRACTION
        // ====================================================================
        
        /// <summary>
        /// Check if an entry exists in the archive.
        /// </summary>
        public bool Contains(string internalPath)
        {
            var normalized = internalPath.Replace('/', '\\').ToLowerInvariant();
            return _entryLookup.ContainsKey(normalized) || _entryLookup.ContainsKey(internalPath);
        }
        
        /// <summary>
        /// Get an entry by path (returns null if not found).
        /// </summary>
        public GrfEntry? GetEntry(string internalPath)
        {
            var normalized = internalPath.Replace('/', '\\').ToLowerInvariant();
            
            if (_entryLookup.TryGetValue(normalized, out var entry))
                return entry;
            
            if (_entryLookup.TryGetValue(internalPath, out entry))
                return entry;
            
            return null;
        }
        
        /// <summary>
        /// Extract a file from the archive.
        /// </summary>
        /// <param name="internalPath">Path within the GRF</param>
        /// <returns>Decompressed file data</returns>
        public byte[] Extract(string internalPath)
        {
            var entry = GetEntry(internalPath);
            
            if (entry == null)
            {
                throw new FileNotFoundException(
                    $"Entry not found in GRF: {internalPath}");
            }

            return ExtractEntry(entry);
        }
        
        /// <summary>
        /// Try to extract a file, returning null if not found.
        /// </summary>
        public byte[]? TryExtract(string internalPath)
        {
            var entry = GetEntry(internalPath);
            if (entry == null)
                return null;

            try
            {
                return ExtractEntry(entry);
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// Extract an entry by reference.
        /// </summary>
        public byte[] ExtractEntry(GrfEntry entry)
        {
            lock (_lock)
            {
                // --------------------------------------------------------
                // CALCULATE ABSOLUTE OFFSET
                // CRITICAL: Use long arithmetic to prevent overflow!
                // --------------------------------------------------------
                
                long absoluteOffset = HEADER_SIZE + (long)entry.Offset;
                
                // Validate offset
                if (absoluteOffset < HEADER_SIZE || absoluteOffset >= FileSize)
                {
                    throw new InvalidDataException(
                        $"Invalid entry offset for '{entry.Path}'. " +
                        $"Offset={absoluteOffset}, FileSize={FileSize}");
                }

                // --------------------------------------------------------
                // READ COMPRESSED DATA
                // --------------------------------------------------------
                
                _fs.Position = absoluteOffset;

                // Determine read size
                // Use aligned size if available, otherwise compressed size
                long readSize = entry.AlignedSize > 0 
                    ? entry.AlignedSize 
                    : entry.CompressedSize;

                // Safety: ensure we read at least compressed size
                if (readSize < entry.CompressedSize)
                    readSize = entry.CompressedSize;

                // Check bounds
                if (absoluteOffset + readSize > FileSize)
                {
                    // Clamp to available size
                    readSize = FileSize - absoluteOffset;
                }

                if (readSize <= 0)
                {
                    return Array.Empty<byte>();
                }

                // Read the data
                byte[] rawData = new byte[readSize];
                int bytesRead = _fs.Read(rawData, 0, (int)readSize);
                
                if (bytesRead < entry.CompressedSize)
                {
                    throw new EndOfStreamException(
                        $"Unexpected EOF reading '{entry.Path}'. " +
                        $"Got {bytesRead}, expected {entry.CompressedSize}");
                }

                // --------------------------------------------------------
                // DECOMPRESS IF NEEDED
                // --------------------------------------------------------
                
                // Check if data is compressed
                // Flag 0x01 = encrypted (old), 0x02 = encrypted (new), 0x08 = file type
                bool isCompressed = entry.CompressedSize < entry.UncompressedSize;

                if (isCompressed && entry.CompressedSize > 0)
                {
                    // Extract only the compressed portion
                    byte[] compressedData;
                    if (rawData.Length > entry.CompressedSize)
                    {
                        compressedData = new byte[entry.CompressedSize];
                        Array.Copy(rawData, compressedData, entry.CompressedSize);
                    }
                    else
                    {
                        compressedData = rawData;
                    }

                    return DecompressZlib(compressedData, (int)entry.UncompressedSize);
                }

                // --------------------------------------------------------
                // NOT COMPRESSED - RETURN RAW DATA
                // --------------------------------------------------------
                
                if (rawData.Length > entry.UncompressedSize && entry.UncompressedSize > 0)
                {
                    // Trim to actual size
                    byte[] trimmed = new byte[entry.UncompressedSize];
                    Array.Copy(rawData, trimmed, entry.UncompressedSize);
                    return trimmed;
                }

                return rawData;
            }
        }

        // ====================================================================
        // SEARCH UTILITIES
        // ====================================================================
        
        /// <summary>
        /// Find all entries matching a pattern.
        /// </summary>
        /// <param name="pattern">Search pattern (e.g., "data\\*.rsw")</param>
        /// <returns>Matching entries</returns>
        public IEnumerable<GrfEntry> FindEntries(string pattern)
        {
            var normalizedPattern = pattern.Replace('/', '\\').ToLowerInvariant();
            
            // Simple wildcard matching
            if (normalizedPattern.Contains('*'))
            {
                var prefix = normalizedPattern.Split('*')[0];
                var suffix = normalizedPattern.Split('*').LastOrDefault() ?? "";
                
                return Entries.Where(e =>
                {
                    var path = e.Path.ToLowerInvariant();
                    return path.StartsWith(prefix) && path.EndsWith(suffix);
                });
            }
            
            // Exact match or contains
            return Entries.Where(e => 
                e.Path.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }
        
        /// <summary>
        /// Get all entries in a directory.
        /// </summary>
        public IEnumerable<GrfEntry> GetEntriesInDirectory(string directory)
        {
            var normalizedDir = directory.Replace('/', '\\').ToLowerInvariant().TrimEnd('\\') + "\\";
            
            return Entries.Where(e => 
                e.Path.Replace('/', '\\').ToLowerInvariant().StartsWith(normalizedDir));
        }
    }
}
