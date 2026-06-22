using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.IO.Runtime
{
    /// <summary>
    /// Default <see cref="IFileService"/> implementation.
    ///
    /// Composition:
    ///   - DI: register <see cref="IFileStorageBackend"/> (platform-specific) and <see cref="IFileService"/> as <see cref="FileService"/>.
    ///   - Non-DI: use <see cref="Default"/>, construct with <c>new FileService()</c>, or keep calling the static <see cref="FileUtility"/>.
    ///
    /// Encoding and decoding are delegated to <see cref="FileUtility"/> so policy stays in one place.
    /// The storage backend only handles raw bytes, keeping platform backends minimal and portable.
    /// </summary>
    public sealed class FileService : IFileService
    {
        public static readonly FileService Default = new FileService();

        private readonly IFileStorageBackend _backend;

        public FileService() : this(null)
        {
        }

        public FileService(IFileStorageBackend backend)
        {
            _backend = backend ?? SystemIOFileStorageBackend.Default;
        }

        public IFileStorageBackend Backend => _backend;

        public bool FileExists(string path)
        {
            return _backend.FileExists(path);
        }

        public void DeleteFile(string path)
        {
            _backend.DeleteFile(path);
        }

        public byte[] ReadAllBytes(string path)
        {
            return _backend.ReadAllBytes(path);
        }

        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        {
            return _backend.ReadAllBytesAsync(path, cancellationToken);
        }

        public string ReadAllText(string path)
        {
            return FileUtility.DecodeText(_backend.ReadAllBytes(path));
        }

        public string ReadAllText(string path, Encoding encoding)
        {
            if (encoding == null)
            {
                throw new ArgumentNullException(nameof(encoding));
            }

            return FileUtility.DecodeText(_backend.ReadAllBytes(path), encoding, true);
        }

        public async Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        {
            byte[] bytes = await _backend.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            return FileUtility.DecodeText(bytes);
        }

        public async Task<string> ReadAllTextAsync(string path, Encoding encoding, CancellationToken cancellationToken = default)
        {
            if (encoding == null)
            {
                throw new ArgumentNullException(nameof(encoding));
            }

            byte[] bytes = await _backend.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            return FileUtility.DecodeText(bytes, encoding, true);
        }

        public void WriteAllBytes(string path, byte[] bytes)
        {
            _backend.WriteAllBytes(path, bytes);
        }

        public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
        {
            return _backend.WriteAllBytesAsync(path, bytes, cancellationToken);
        }

        public void WriteAllText(string path, string content)
        {
            WriteAllText(path, content, FileUtility.Utf8NoBom);
        }

        public void WriteAllText(string path, string content, Encoding encoding)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            if (encoding == null)
            {
                throw new ArgumentNullException(nameof(encoding));
            }

            _backend.WriteAllBytes(path, encoding.GetBytes(content));
        }

        public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
        {
            return WriteAllTextAsync(path, content, FileUtility.Utf8NoBom, cancellationToken);
        }

        public Task WriteAllTextAsync(string path, string content, Encoding encoding, CancellationToken cancellationToken = default)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            if (encoding == null)
            {
                throw new ArgumentNullException(nameof(encoding));
            }

            return _backend.WriteAllBytesAsync(path, encoding.GetBytes(content), cancellationToken);
        }

        public void WriteAllBytesAtomic(string path, byte[] bytes)
        {
            _backend.WriteAllBytesAtomic(path, bytes);
        }

        public Task WriteAllBytesAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
        {
            return _backend.WriteAllBytesAtomicAsync(path, bytes, cancellationToken);
        }

        public void WriteAllTextAtomic(string path, string content)
        {
            WriteAllTextAtomic(path, content, FileUtility.Utf8NoBom);
        }

        public void WriteAllTextAtomic(string path, string content, Encoding encoding)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            if (encoding == null)
            {
                throw new ArgumentNullException(nameof(encoding));
            }

            _backend.WriteAllBytesAtomic(path, encoding.GetBytes(content));
        }

        public Task WriteAllTextAtomicAsync(string path, string content, CancellationToken cancellationToken = default)
        {
            return WriteAllTextAtomicAsync(path, content, FileUtility.Utf8NoBom, cancellationToken);
        }

        public Task WriteAllTextAtomicAsync(string path, string content, Encoding encoding, CancellationToken cancellationToken = default)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            if (encoding == null)
            {
                throw new ArgumentNullException(nameof(encoding));
            }

            return _backend.WriteAllBytesAtomicAsync(path, encoding.GetBytes(content), cancellationToken);
        }
    }
}
