using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.IO
{
    /// <summary>
    /// Byte-oriented storage contract for platform adapters and dependency injection.
    /// Whole-file reads always require an explicit allocation limit.
    /// </summary>
    public interface IFileStore
    {
        bool Exists(string path);

        long GetLength(string path);

        void Delete(string path);

        byte[] ReadBytes(string path, int maxByteCount);

        Task<byte[]> ReadBytesAsync(
            string path,
            int maxByteCount,
            CancellationToken cancellationToken = default);

        void WriteBytes(string path, byte[] content);

        Task WriteBytesAsync(
            string path,
            byte[] content,
            CancellationToken cancellationToken = default);
    }
}
