using ROMapOverlayEditor.Sources;
using ROMapOverlayEditor.ThreeD;
using ROMapOverlayEditor.Vfs;

namespace ROMapOverlayEditor.Rsw
{
    /// <summary>
    /// Loads an RSW map from the VFS (e.g. GRF) for 3D viewing in the editor.
    /// Resolves RSW/GND/GAT paths and delegates to <see cref="ThreeDMapLoader"/>.
    /// </summary>
    public static class Rsw3DLoader
    {
        /// <summary>
        /// Resolve RSW (and GND/GAT) from the VFS and load the map for 3D view.
        /// </summary>
        /// <param name="vfs">Composite VFS (GRF, folders, etc.)</param>
        /// <param name="rswPathOrBaseName">Full path (e.g. "data/prontera.rsw") or base name (e.g. "prontera")</param>
        /// <returns>Success with <see cref="ThreeDMap"/> or failure message.</returns>
        public static ThreeDMapLoadResult LoadForView(CompositeVfs vfs, string rswPathOrBaseName)
        {
            var (rswPath, _, _) = VfsPathResolver.ResolveMapTriplet(vfs, rswPathOrBaseName);
            if (rswPath == null)
            {
                var baseName = System.IO.Path.GetFileNameWithoutExtension((rswPathOrBaseName ?? "").Trim());
                return ThreeDMapLoadResult.Fail(
                    $"RSW not found for '{baseName}'.\n\n" +
                    "Ensure the GRF (or mounted sources) contains:\n" +
                    $"  - {baseName}.rsw\n" +
                    $"  - {baseName}.gnd (required for 3D)\n" +
                    $"  - {baseName}.gat (recommended)\n\n" +
                    "Open the correct GRF and try again.");
            }

            return ThreeDMapLoader.Load(vfs, rswPath);
        }
    }
}
