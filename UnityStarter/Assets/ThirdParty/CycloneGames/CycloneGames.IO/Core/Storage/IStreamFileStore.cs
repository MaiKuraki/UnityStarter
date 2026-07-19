using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.IO
{
    /// <summary>
    /// Optional streaming capability. The caller owns every returned stream.
    /// </summary>
    public interface IStreamFileStore
    {
        Stream OpenRead(string path);

        /// <summary>
        /// Creates or fully truncates a file and returns a caller-owned writable stream.
        /// </summary>
        Stream CreateWrite(string path);

        /// <summary>
        /// Opens a caller-owned append-only stream that permits concurrent readers but rejects other writers.
        /// </summary>
        Stream OpenAppend(string path);

        Task<long> WriteStreamAsync(
            string path,
            Stream source,
            CancellationToken cancellationToken = default);

        Task<long> ReadStreamAsync(
            string path,
            Stream destination,
            CancellationToken cancellationToken = default);
    }
}
