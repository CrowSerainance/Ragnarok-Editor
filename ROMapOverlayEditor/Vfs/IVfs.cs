namespace ROMapOverlayEditor.Vfs
{
    public interface IVfs
    {
        bool Exists(string virtualPath);
        byte[] ReadAllBytes(string virtualPath);
    }
}
