using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.IO.Runtime
{
    /// <summary>
    /// Pluggable byte-oriented storage primitive used by <see cref="IFileService"/>.
    /// Implement this to target platforms whose persistence does not map to System.IO
    /// (for example WebGL IndexedDB or console save-data APIs), or to provide a test double.
    /// Encoding, hashing, and path policy live above this layer so backends stay minimal.
    /// </summary>
    public interface IFileStorageBackend
    {
        bool FileExists(string path);

        long GetFileSize(string path);

        void DeleteFile(string path);

        byte[] ReadAllBytes(string path);

        Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default);

        void WriteAllBytes(string path, byte[] bytes);

        Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default);

        void WriteAllBytesAtomic(string path, byte[] bytes);

        Task WriteAllBytesAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken = default);
    }
}
