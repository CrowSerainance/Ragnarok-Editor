namespace ROMapOverlayEditor.ThreeD
{
    public sealed class LoadedRoMap
    {
        public string MapName { get; set; } = "";

        // Dimensions (from GND)
        public int Width { get; set; }
        public int Height { get; set; }

        public byte[] GndBytes { get; set; } = System.Array.Empty<byte>();
        public byte[] GatBytes { get; set; } = System.Array.Empty<byte>();
        public byte[] RswBytes { get; set; } = System.Array.Empty<byte>();
    }
}
