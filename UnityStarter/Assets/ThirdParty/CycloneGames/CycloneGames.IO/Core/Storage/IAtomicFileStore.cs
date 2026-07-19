using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.IO
{
    /// <summary>
    /// Capability contract for stores that can commit one destination atomically.
    /// </summary>
    public interface IAtomicFileStore
    {
        void WriteBytesAtomically(string path, byte[] content);

        Task WriteBytesAtomicallyAsync(
            string path,
            byte[] content,
            CancellationToken cancellationToken = default);

        Task<long> WriteStreamAtomicallyAsync(
            string path,
            Stream source,
            IProgress<FileTransferProgress> progress = null,
            CancellationToken cancellationToken = default);
    }
}
