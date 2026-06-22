using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.IO.Runtime
{
    /// <summary>
    /// Encoding-aware file API designed for both explicit construction and DI composition.
    /// Backed by a pluggable <see cref="IFileStorageBackend"/>.
    /// Text reads honor BOM and fall back to strict UTF-8. Text writes default to UTF-8 without BOM.
    /// Atomic writes prefer a temporary-file-then-replace strategy; see <see cref="FileUtility"/> for durability limits.
    /// </summary>
    public interface IFileService
    {
        bool FileExists(string path);

        void DeleteFile(string path);

        byte[] ReadAllBytes(string path);

        Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default);

        string ReadAllText(string path);

        string ReadAllText(string path, Encoding encoding);

        Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);

        Task<string> ReadAllTextAsync(string path, Encoding encoding, CancellationToken cancellationToken = default);

        void WriteAllBytes(string path, byte[] bytes);

        Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default);

        void WriteAllText(string path, string content);

        void WriteAllText(string path, string content, Encoding encoding);

        Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default);

        Task WriteAllTextAsync(string path, string content, Encoding encoding, CancellationToken cancellationToken = default);

        void WriteAllBytesAtomic(string path, byte[] bytes);

        Task WriteAllBytesAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken = default);

        void WriteAllTextAtomic(string path, string content);

        void WriteAllTextAtomic(string path, string content, Encoding encoding);

        Task WriteAllTextAtomicAsync(string path, string content, CancellationToken cancellationToken = default);

        Task WriteAllTextAtomicAsync(string path, string content, Encoding encoding, CancellationToken cancellationToken = default);
    }
}
