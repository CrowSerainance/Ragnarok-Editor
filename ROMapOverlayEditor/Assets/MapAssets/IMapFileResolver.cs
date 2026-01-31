// ============================================================================
// IMapFileResolver - Async file resolution for MapLoaderV2 (from rsw_viewer)
// ============================================================================

using System.Threading;
using System.Threading.Tasks;

namespace ROMapOverlayEditor.MapAssets
{
    /// <summary>Interface for resolving map files from various sources (GRF, folder, etc.).</summary>
    public interface IMapFileResolver
    {
        Task<byte[]?> ReadFileAsync(string path, CancellationToken ct = default);
        Task<bool> FileExistsAsync(string path, CancellationToken ct = default);
    }
}
