using System.IO;
using System.IO.Compression;
using System.Text;

namespace ROMapOverlayEditor;

/// <summary>Reads GRF 0x200 archives. Supports reading and extracting files; no encryption.</summary>
public sealed class GrfReader : IDisposable
{
    private const string Signature = "Master of Magic";
    private const int HeaderSize = 46;
    private const int MaxSaneSize = 400 * 1024 * 1024; // 400 MB
    private readonly FileStream _stream;
    private List<GrfEntry> _entries = new();

    public IReadOnlyList<GrfEntry> Entries => _entries;
    public uint Version { get; private set; }

    public GrfReader(string grfFilePath)
    {
        _stream = new FileStream(grfFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    /// <summary>Load and parse the GRF header and file table. Call before Extract or GetMapBmpPaths. Only 0x200 is supported.</summary>
    public void Load()
    {
        _stream.Position = 0;
        var buf = new byte[HeaderSize];
        if (_stream.Read(buf, 0, HeaderSize) != HeaderSize)
            throw new InvalidDataException("GRF: truncated header.");

        var sig = Encoding.ASCII.GetString(buf, 0, 15);
        if (sig != Signature)
            throw new InvalidDataException($"GRF: invalid signature '{sig.TrimEnd('\0')}'.");

        // Header: Magic 16, Key 14 → offset at 30; Seed 34, FilesCount 38, Version 42
        int fileTableOffset = BitConverter.ToInt32(buf, 30);
        Version = (uint)BitConverter.ToInt32(buf, 42);

        if (Version >= 0x100 && Version <= 0x103)
            throw new InvalidDataException("GRF 1.x (0x100–0x103) is not supported. Use a 0x200 GRF.");

        if (Version != 0x200)
            throw new InvalidDataException($"GRF version 0x{Version:X} is not supported. Only 0x200 is supported.");

        // File table starts at absolute offset (fileTableOffset + 46)
        long tableAbsolute = 46 + fileTableOffset;
        if (tableAbsolute < 46 || tableAbsolute > _stream.Length - 8)
            throw new InvalidDataException("GRF: invalid file table offset.");

        _stream.Position = tableAbsolute;
        if (_stream.Read(buf, 0, 8) != 8)
            throw new InvalidDataException("GRF: truncated file table header.");

        int compressedSize = BitConverter.ToInt32(buf, 0);
        int decompressedSize = BitConverter.ToInt32(buf, 4);

        if (compressedSize < 0 || compressedSize > MaxSaneSize)
            throw new InvalidDataException($"GRF: invalid file table compressed size ({compressedSize}).");
        if (decompressedSize < 0 || decompressedSize > MaxSaneSize)
            throw new InvalidDataException($"GRF: invalid file table uncompressed size ({decompressedSize}).");

        byte[] compressed = new byte[compressedSize];
        if (_stream.Read(compressed, 0, compressedSize) != compressedSize)
            throw new InvalidDataException("GRF: truncated file table.");

        byte[] table = DecompressZlib(compressed);
        if (table.Length < decompressedSize)
            throw new InvalidDataException("GRF: file table decompression yielded less than expected.");

        _entries = ParseFileTable(table, decompressedSize);
    }

    private static byte[] DecompressZlib(byte[] compressed)
    {
        using var ms = new MemoryStream(compressed);
        using var z = new ZLibStream(ms, CompressionMode.Decompress, leaveOpen: false);
        using var outMs = new MemoryStream();
        z.CopyTo(outMs);
        return outMs.ToArray();
    }

    private static List<GrfEntry> ParseFileTable(byte[] table, int decompressedSize)
    {
        var list = new List<GrfEntry>();
        int i = 0;
        while (i < decompressedSize)
        {
            int nameStart = i;
            while (i < decompressedSize && table[i] != 0) i++;
            if (i >= decompressedSize) break;
            string path = Encoding.ASCII.GetString(table, nameStart, i - nameStart);
            i++; // consume null

            if (i + 17 > decompressedSize) break;

            int compressedSize = BitConverter.ToInt32(table, i);
            int byteAlignedSize = BitConverter.ToInt32(table, i + 4);
            int entryDecompressedSize = BitConverter.ToInt32(table, i + 8);
            byte flags = table[i + 12];
            int offset = BitConverter.ToInt32(table, i + 13);
            i += 17;

            list.Add(new GrfEntry
            {
                Path = path,
                CompressedSize = compressedSize,
                ByteAlignedSize = byteAlignedSize,
                DecompressedSize = entryDecompressedSize,
                Flags = flags,
                Offset = offset
            });
        }
        return list;
    }

    /// <summary>Extract a file by its internal path (case-insensitive). Returns decompressed bytes.</summary>
    public byte[] Extract(string internalPath)
    {
        var e = _entries.FirstOrDefault(x => string.Equals(x.Path, internalPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"GRF: entry not found: {internalPath}");

        if (e.ByteAlignedSize < 0 || e.ByteAlignedSize > MaxSaneSize)
            throw new InvalidDataException($"GRF: invalid size for {internalPath}.");

        // File data offset is relative to post-header: absolute = 46 + offset
        long dataAbsolute = 46 + e.Offset;
        if (dataAbsolute < 46 || dataAbsolute > _stream.Length - e.ByteAlignedSize)
            throw new InvalidDataException($"GRF: invalid data offset for {internalPath}.");

        _stream.Position = dataAbsolute;
        byte[] raw = new byte[e.ByteAlignedSize];
        if (_stream.Read(raw, 0, raw.Length) != raw.Length)
            throw new InvalidDataException($"GRF: truncated data for {internalPath}.");

        bool isCompressed = (e.Flags & 8) != 0 || (e.CompressedSize != e.DecompressedSize);
        if (isCompressed)
            return DecompressZlib(raw);
        return raw;
    }

    /// <summary>Paths of .bmp files under \map\. If none, all .bmp in the archive.</summary>
    public IReadOnlyList<string> GetMapBmpPaths()
    {
        var mapBmps = _entries
            .Where(e => (e.Flags & 1) != 0) // file
            .Where(e => e.Path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) && (e.Path.Contains("\\map\\", StringComparison.OrdinalIgnoreCase) || e.Path.Contains("/map/", StringComparison.OrdinalIgnoreCase)))
            .Select(e => e.Path)
            .ToList();
        if (mapBmps.Count != 0) return mapBmps;
        return _entries
            .Where(e => (e.Flags & 1) != 0 && e.Path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Path)
            .ToList();
    }

    public void Dispose() => _stream.Dispose();
}

public sealed class GrfEntry
{
    public string Path { get; set; } = "";
    public int CompressedSize { get; set; }
    public int ByteAlignedSize { get; set; }
    public int DecompressedSize { get; set; }
    public byte Flags { get; set; }
    public int Offset { get; set; }
}
