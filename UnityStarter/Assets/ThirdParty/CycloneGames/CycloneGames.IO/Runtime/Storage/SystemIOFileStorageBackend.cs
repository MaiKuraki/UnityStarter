using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.IO.Runtime
{
    /// <summary>
    /// Default <see cref="IFileStorageBackend"/> backed by System.IO through <see cref="FileUtility"/>.
    /// Suitable for Windows, macOS, Linux, and writable iOS/Android locations.
    /// On WebGL and console platforms, inject a platform-specific backend instead.
    /// </summary>
    public sealed class SystemIOFileStorageBackend : IFileStorageBackend, IStreamingFileStorageBackend
    {
        public static readonly SystemIOFileStorageBackend Default = new SystemIOFileStorageBackend();

        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public long GetFileSize(string path)
        {
            return new FileInfo(path).Length;
        }

        public void DeleteFile(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public byte[] ReadAllBytes(string path)
        {
            return FileUtility.ReadAllBytes(path);
        }

        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        {
            return FileUtility.ReadAllBytesAsync(path, cancellationToken);
        }

        public void WriteAllBytes(string path, byte[] bytes)
        {
            FileUtility.WriteAllBytes(path, bytes);
        }

        public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
        {
            return FileUtility.WriteAllBytesAsync(path, bytes, cancellationToken);
        }

        public void WriteAllBytesAtomic(string path, byte[] bytes)
        {
            FileUtility.WriteAllBytesAtomic(path, bytes);
        }

        public Task WriteAllBytesAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
        {
            return FileUtility.WriteAllBytesAtomicAsync(path, bytes, cancellationToken);
        }

        public Stream OpenRead(string path)
        {
            return FileUtility.OpenRead(path);
        }

        public Stream OpenWrite(string path)
        {
            return FileUtility.OpenWrite(path);
        }
    }
}
