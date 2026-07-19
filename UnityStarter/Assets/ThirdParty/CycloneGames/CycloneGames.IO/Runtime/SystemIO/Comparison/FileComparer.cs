using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.IO
{
    public static class FileComparer
    {
        public static async Task<bool> AreEqualAsync(
            string firstPath,
            string secondPath,
            IProgress<FileTransferProgress> progress = null,
            CancellationToken cancellationToken = default,
            SystemFileStoreOptions options = null)
        {
            ValidateExistingFile(firstPath, nameof(firstPath));
            ValidateExistingFile(secondPath, nameof(secondPath));
            cancellationToken.ThrowIfCancellationRequested();

            string normalizedFirstPath = Path.GetFullPath(firstPath);
            string normalizedSecondPath = Path.GetFullPath(secondPath);
            if (string.Equals(normalizedFirstPath, normalizedSecondPath, PathComparison))
            {
                progress?.Report(new FileTransferProgress(0L, 0L));
                return true;
            }

            long firstLength = new FileInfo(normalizedFirstPath).Length;
            long secondLength = new FileInfo(normalizedSecondPath).Length;
            if (firstLength != secondLength)
            {
                return false;
            }

            using (var first = SystemFileStreams.OpenRead(normalizedFirstPath, true))
            using (var second = SystemFileStreams.OpenRead(normalizedSecondPath, true))
            {
                return await BinaryContentComparer.AreEqualAsync(
                    first,
                    second,
                    firstLength,
                    progress,
                    cancellationToken,
                    options).ConfigureAwait(false);
            }
        }

        private static StringComparison PathComparison =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        private static void ValidateExistingFile(string path, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path cannot be null, empty, or whitespace.", parameterName);
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("File does not exist.", path);
            }
        }
    }
}
