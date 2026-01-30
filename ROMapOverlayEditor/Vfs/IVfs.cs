namespace ROMapOverlayEditor.Vfs
{
    public interface IVfs
    {
        bool Exists(string virtualPath);
        byte[] ReadAllBytes(string virtualPath);
        bool TryReadAllBytes(string virtualPath, out byte[]? bytes, out string? error);
    }
}
