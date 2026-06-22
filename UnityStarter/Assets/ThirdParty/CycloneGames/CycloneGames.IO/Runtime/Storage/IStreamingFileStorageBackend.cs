using System.IO;

namespace CycloneGames.IO.Runtime
{
    /// <summary>
    /// Optional streaming capability for storage backends that can expose raw streams.
    /// Not every backend supports streaming (some WebGL or console save-data backends are bulk-only),
    /// so detect support with a type check before use:
    /// <c>(backend as IStreamingFileStorageBackend)?.OpenRead(path)</c>.
    /// The caller owns returned streams and must dispose them.
    /// </summary>
    public interface IStreamingFileStorageBackend
    {
        Stream OpenRead(string path);

        Stream OpenWrite(string path);
    }
}
