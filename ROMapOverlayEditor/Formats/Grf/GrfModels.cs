namespace ROMapOverlayEditor.Grf
{
    public readonly struct GrfValidationResult
    {
        public bool Ok { get; }
        public string UserMessage { get; }

        private GrfValidationResult(bool ok, string msg)
        {
            Ok = ok;
            UserMessage = msg;
        }

        public static GrfValidationResult Success() => new(true, "");
        public static GrfValidationResult Fail(string msg) => new(false, msg);
    }

    public sealed class GrfEntry
    {
        public string Path { get; set; } = "";
        public uint CompressedSize { get; set; }
        public uint AlignedSize { get; set; }
        public uint UncompressedSize { get; set; }
        public uint Offset { get; set; }
        public byte Flags { get; set; }
        
        /// <summary>Check if this is a file (vs directory)</summary>
        public bool IsFile => (Flags & 0x01) != 0 || UncompressedSize > 0;
        
        public override string ToString() 
            => $"{Path} ({UncompressedSize:N0} bytes @ offset {Offset})";
    }
}
