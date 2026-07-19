using System;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.IO
{
    public static class FileHasher
    {
        public static int WriteHash(
            string path,
            FileHashAlgorithm algorithm,
            Span<byte> destination,
            SystemFileStoreOptions options = null)
        {
            ValidatePath(path);
            using (var source = SystemFileStreams.OpenRead(path, false))
            {
                return ContentHasher.WriteHash(source, algorithm, destination, options);
            }
        }

        public static async Task<int> WriteHashAsync(
            string path,
            FileHashAlgorithm algorithm,
            Memory<byte> destination,
            IProgress<FileTransferProgress> progress = null,
            CancellationToken cancellationToken = default,
            SystemFileStoreOptions options = null)
        {
            ValidatePath(path);
            cancellationToken.ThrowIfCancellationRequested();
            using (var source = SystemFileStreams.OpenRead(path, true))
            {
                return await ContentHasher.WriteHashAsync(
                    source,
                    algorithm,
                    destination,
                    progress,
                    cancellationToken,
                    options).ConfigureAwait(false);
            }
        }

        public static string ComputeHex(
            string path,
            FileHashAlgorithm algorithm,
            SystemFileStoreOptions options = null)
        {
            int hashSize = ContentHasher.GetHashSize(algorithm);
            Span<byte> hash = stackalloc byte[hashSize];
            WriteHash(path, algorithm, hash, options);
            return ContentHasher.ToHex(hash);
        }

        public static async Task<string> ComputeHexAsync(
            string path,
            FileHashAlgorithm algorithm,
            IProgress<FileTransferProgress> progress = null,
            CancellationToken cancellationToken = default,
            SystemFileStoreOptions options = null)
        {
            int hashSize = ContentHasher.GetHashSize(algorithm);
            var hash = new byte[hashSize];
            await WriteHashAsync(
                path,
                algorithm,
                hash.AsMemory(),
                progress,
                cancellationToken,
                options).ConfigureAwait(false);
            return ContentHasher.ToHex(hash);
        }

        private static void ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path cannot be null, empty, or whitespace.", nameof(path));
            }
        }
    }
}
