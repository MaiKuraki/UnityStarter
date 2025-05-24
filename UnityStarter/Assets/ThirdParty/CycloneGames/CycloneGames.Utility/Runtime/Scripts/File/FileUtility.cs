using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CycloneGames.Logger; // Assuming CLogger is a custom logging utility

namespace CycloneGames.Utility.Runtime
{
    public static class FileUtility
    {
        private const string DEBUG_FLAG = "[FileUtility]";

        // Optimal buffer sizes can vary based on platform and typical file sizes.
        // Larger buffers can reduce disk I/O calls but consume more memory.
#if UNITY_IOS || UNITY_ANDROID
        private const int BufferSize = 81920; // 80KB, potentially better for flash storage on mobile
#else
        private const int BufferSize = 32768; // 32KB, a common default for PC
#endif

        // Files larger than this threshold will be compared by chunks for potential early exit.
        // Smaller files will be compared by their full hash.
        private const long LargeFileThreshold = 10 * 1024 * 1024; // 10MB 

        // ThreadLocal SHA256 instance to avoid repeated creation and ensure thread safety.
        // SHA256.Create() can be relatively expensive.
        private static readonly ThreadLocal<SHA256> ThreadLocalSha256 = new ThreadLocal<SHA256>(SHA256.Create);

        // Get a buffer from the shared ArrayPool to reduce GC pressure.
        private static byte[] GetBuffer() => ArrayPool<byte>.Shared.Rent(BufferSize);

        // Return a buffer to the shared ArrayPool.
        private static void ReturnBuffer(byte[] buffer) => ArrayPool<byte>.Shared.Return(buffer);

        /// <summary>
        /// Asynchronously compares two files to determine if they are identical.
        /// </summary>
        /// <param name="filePath1">Path to the first file.</param>
        /// <param name="filePath2">Path to the second file.</param>
        /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
        /// <returns>True if files are identical, false otherwise.</returns>
        public static async Task<bool> AreFilesEqualAsync(string filePath1, string filePath2,
            CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                // Ensure both files exist before attempting to get FileInfo.
                if (!File.Exists(filePath1) || !File.Exists(filePath2))
                {
                    CLogger.LogDebug($"{DEBUG_FLAG} One or both files do not exist: '{filePath1}', '{filePath2}'");
                    return false;
                }

                FileInfo fileInfo1, fileInfo2;
                try
                {
                    fileInfo1 = new FileInfo(filePath1);
                    fileInfo2 = new FileInfo(filePath2);
                }
                catch (Exception ex)
                {
                    // Catch potential errors like path too long, security issues, etc.
                    CLogger.LogWarning(
                        $"{DEBUG_FLAG} Error getting file info for '{filePath1}' or '{filePath2}': {ex.Message}");
                    return false;
                }

                // If lengths are different, files are not equal.
                if (fileInfo1.Length != fileInfo2.Length)
                {
                    return false;
                }

                // If files are empty and lengths are same (0), they are equal.
                if (fileInfo1.Length == 0)
                {
                    return true;
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Choose comparison strategy based on file size.
                // For large files, chunk comparison can be faster if differences are found early.
                // For smaller files, hashing is often efficient.
                return fileInfo1.Length > LargeFileThreshold
                    ? await AreFilesEqualByChunksAsync(filePath1, filePath2, cancellationToken).ConfigureAwait(false)
                    : await AreFilesEqualByHashAsync(filePath1, filePath2, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                sw.Stop();
                CLogger.LogDebug(
                    $"{DEBUG_FLAG} File comparison for '{filePath1}' and '{filePath2}' took {sw.ElapsedMilliseconds}ms");
            }
        }

        /// <summary>
        /// Compares two files by computing and comparing their SHA256 hashes.
        /// Suitable for smaller files or when a full-file verification is acceptable.
        /// </summary>
        private static async Task<bool> AreFilesEqualByHashAsync(string filePath1, string filePath2,
            CancellationToken cancellationToken)
        {
            try
            {
                var sha256 = ThreadLocalSha256.Value; // Get thread-local SHA256 instance
                if (sha256 == null)
                {
                    // This should not happen with the ThreadLocal(SHA256.Create) pattern
                    // unless the factory method itself failed, which would throw earlier.
                    CLogger.LogError($"{DEBUG_FLAG} SHA256 instance could not be retrieved from ThreadLocal.");
                    return false;
                }

                // Compute hash for the first file.
                byte[] hash1 = await ComputeHashAsync(sha256, filePath1, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                // Compute hash for the second file.
                byte[] hash2 = await ComputeHashAsync(sha256, filePath2, cancellationToken).ConfigureAwait(false);

                // Compare the generated hashes.
                return CompareByteArrays(hash1, hash2);
            }
            catch (OperationCanceledException)
            {
                CLogger.LogDebug($"{DEBUG_FLAG} Hash comparison cancelled for '{filePath1}', '{filePath2}'.");
                throw; // Re-throw to propagate cancellation.
            }
            catch (Exception ex)
            {
                CLogger.LogWarning(
                    $"{DEBUG_FLAG} Error comparing files by hash ('{filePath1}', '{filePath2}'): {ex.Message}");
                return false; // Treat errors in comparison as files not being equal or comparison failed.
            }
        }

        /// <summary>
        /// Compares two files by reading them in chunks and comparing each chunk.
        /// More efficient for large files if differences occur early in the file.
        /// </summary>
        private static async Task<bool> AreFilesEqualByChunksAsync(string filePath1, string filePath2,
            CancellationToken cancellationToken)
        {
            byte[] buffer1 = GetBuffer();
            byte[] buffer2 = GetBuffer();
            try
            {
                // Use FileOptions.SequentialScan for performance and Asynchronous for true async I/O.
                using (var fs1 = new FileStream(filePath1, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
                           FileOptions.SequentialScan | FileOptions.Asynchronous))
                using (var fs2 = new FileStream(filePath2, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
                           FileOptions.SequentialScan | FileOptions.Asynchronous))
                {
                    int bytesRead1;
                    int bytesRead2;
                    while ((bytesRead1 = await fs1.ReadAsync(buffer1, 0, buffer1.Length, cancellationToken)
                               .ConfigureAwait(false)) > 0)
                    {
                        // Read the same amount from the second file.
                        // If file lengths are guaranteed equal by the caller (AreFilesEqualAsync),
                        // a full read of buffer2.Length might seem okay, but it's safer to match bytesRead1.
                        // However, fs2.ReadAsync might not fill the buffer fully even if there's more data.
                        // It's crucial to read into buffer2 up to the same count as read from buffer1 for a valid comparison pass.
                        // A more robust way is to read into buffer2 independently and ensure total bytes read match eventually.
                        // Given file lengths are already checked to be equal, this sequential chunk reading should be fine.

                        int totalBytesReadFromFs2 = 0;
                        while (totalBytesReadFromFs2 < bytesRead1)
                        {
                            bytesRead2 = await fs2.ReadAsync(buffer2, totalBytesReadFromFs2,
                                bytesRead1 - totalBytesReadFromFs2, cancellationToken).ConfigureAwait(false);
                            if (bytesRead2 == 0)
                            {
                                // End of fs2 reached prematurely, means files are different (should not happen if lengths are same and no corruption).
                                return false;
                            }

                            totalBytesReadFromFs2 += bytesRead2;
                        }

                        // Compare the contents of the buffers up to the number of bytes read.
                        if (!CompareByteArrays(buffer1, buffer2, bytesRead1))
                        {
                            return false; // Buffers differ.
                        }

                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    // If loop completes, all chunks matched.
                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                CLogger.LogDebug($"{DEBUG_FLAG} Chunk comparison cancelled for '{filePath1}', '{filePath2}'.");
                throw; // Re-throw to propagate cancellation.
            }
            catch (Exception ex)
            {
                CLogger.LogWarning(
                    $"{DEBUG_FLAG} Error comparing files by chunks ('{filePath1}', '{filePath2}'): {ex.Message}");
                return false;
            }
            finally
            {
                ReturnBuffer(buffer1);
                ReturnBuffer(buffer2);
            }
        }

        /// <summary>
        /// Computes the hash of a file using the provided HashAlgorithm.
        /// </summary>
        private static async Task<byte[]> ComputeHashAsync(HashAlgorithm hashAlgorithm, string filePath,
            CancellationToken cancellationToken)
        {
            var buffer = GetBuffer();
            try
            {
                // Initialize the hash algorithm for a new computation.
                hashAlgorithm.Initialize();

                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
                           FileOptions.SequentialScan | FileOptions.Asynchronous))
                {
                    int bytesRead;
                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                               .ConfigureAwait(false)) > 0)
                    {
                        // Process the block of data.
                        hashAlgorithm.TransformBlock(buffer, 0, bytesRead, null, 0);
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    // Finalize the hash computation.
                    // Pass an empty block or the last partial block (if any was not fully processed by TransformBlock).
                    // Since all read bytes are processed by TransformBlock in the loop, finalize with an empty array.
                    hashAlgorithm.TransformFinalBlock(buffer, 0,
                        0); // Using buffer with 0 count is fine and avoids Array.Empty allocation
                    return hashAlgorithm.Hash; // The .Hash property creates a new byte array.
                }
            }
            finally
            {
                ReturnBuffer(buffer);
            }
        }

        /// <summary>
        /// Asynchronously copies a file from source to destination.
        /// If the destination file exists, it's compared with the source.
        /// Copying is skipped if files are identical. Otherwise, the destination is overwritten.
        /// </summary>
        public static async Task CopyFileWithComparisonAsync(string sourceFilePath, string destinationFilePath,
            CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (!File.Exists(sourceFilePath))
                {
                    CLogger.LogError($"{DEBUG_FLAG} Source file does not exist: {sourceFilePath}");
                    return;
                }

                string directoryPath = Path.GetDirectoryName(destinationFilePath);
                // Ensure directoryPath is not null or empty if destination is in the current directory.
                if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                    CLogger.LogInfo($"{DEBUG_FLAG} Created directory: {directoryPath}");
                }

                if (File.Exists(destinationFilePath))
                {
                    CLogger.LogInfo(
                        $"{DEBUG_FLAG} Destination file exists: {destinationFilePath}. Comparing with source.");
                    if (await AreFilesEqualAsync(sourceFilePath, destinationFilePath, cancellationToken)
                            .ConfigureAwait(false))
                    {
                        CLogger.LogInfo(
                            $"{DEBUG_FLAG} Files '{sourceFilePath}' and '{destinationFilePath}' are identical. Skipping copy.");
                        return;
                    }

                    CLogger.LogInfo(
                        $"{DEBUG_FLAG} Files are different. Deleting destination file '{destinationFilePath}' before copy.");
                    File.Delete(destinationFilePath); // Delete to overwrite.
                }

                cancellationToken.ThrowIfCancellationRequested();

                var buffer = GetBuffer();
                try
                {
                    // Using FileOptions.SequentialScan for potentially faster reads and writes.
                    // Using FileOptions.Asynchronous for true async I/O.
                    using (var sourceStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read,
                               FileShare.Read, BufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous))
                    using (var destinationStream = new FileStream(destinationFilePath, FileMode.CreateNew,
                               FileAccess.Write, FileShare.None, BufferSize,
                               FileOptions.SequentialScan |
                               FileOptions
                                   .Asynchronous)) // FileMode.CreateNew ensures we are creating it after potential delete.
                    {
                        int bytesRead;
                        while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                                   .ConfigureAwait(false)) > 0)
                        {
                            await destinationStream.WriteAsync(buffer, 0, bytesRead, cancellationToken)
                                .ConfigureAwait(false);
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }

                    CLogger.LogInfo(
                        $"{DEBUG_FLAG} File copied successfully from '{sourceFilePath}' to '{destinationFilePath}'.");
                }
                finally
                {
                    ReturnBuffer(buffer);
                }
            }
            catch (OperationCanceledException)
            {
                CLogger.LogWarning(
                    $"{DEBUG_FLAG} File copy operation from '{sourceFilePath}' to '{destinationFilePath}' was cancelled.");
                // Clean up partially written destination file if cancellation occurred during write.
                if (File.Exists(destinationFilePath))
                {
                    try
                    {
                        File.Delete(destinationFilePath);
                        CLogger.LogInfo(
                            $"{DEBUG_FLAG} Deleted partially written destination file: {destinationFilePath} due to cancellation.");
                    }
                    catch (Exception exDelete)
                    {
                        CLogger.LogWarning(
                            $"{DEBUG_FLAG} Could not delete partially written destination file '{destinationFilePath}': {exDelete.Message}");
                    }
                }

                throw; // Re-throw to propagate cancellation.
            }
            catch (Exception ex)
            {
                CLogger.LogError(
                    $"{DEBUG_FLAG} Error during file operation (source: '{sourceFilePath}', dest: '{destinationFilePath}'): {ex.Message} StackTrace: {ex.StackTrace}");
                // Consider if the destination file (if partially created) should be deleted on other errors too.
            }
            finally
            {
                sw.Stop();
                CLogger.LogDebug(
                    $"{DEBUG_FLAG} File copy operation for source '{sourceFilePath}' took {sw.ElapsedMilliseconds}ms");
            }
        }

        /// <summary>
        /// Compares two byte arrays for equality using unsafe pointer operations for performance.
        /// </summary>
        /// <param name="array1">The first byte array.</param>
        /// <param name="array2">The second byte array.</param>
        /// <param name="length">The number of bytes to compare. If -1, compares based on array1.Length and ensures array2.Length matches.</param>
        /// <returns>True if the specified portions of the arrays are equal, false otherwise.</returns>
        private static unsafe bool CompareByteArrays(byte[] array1, byte[] array2, int length = -1)
        {
            if (array1 == null || array2 == null) return array1 == array2; // Both null means equal, one null means not.

            if (length == -1)
            {
                if (array1.Length != array2.Length)
                    return false;
                length = array1.Length;
            }

            // If either array is too short for the specified comparison length.
            if (array1.Length < length || array2.Length < length)
                return false;

            if (length == 0) return true; // Zero length arrays (or zero length comparison) are equal.

            // Pin the arrays in memory to get stable pointers.
            fixed (byte* p1 = array1, p2 = array2)
            {
                byte* x1 = p1;
                byte* x2 = p2;

                // Compare 8 bytes at a time (long)
                int n = length / 8;
                for (int i = 0; i < n; i++)
                {
                    if (*(long*)x1 != *(long*)x2) return false;
                    x1 += 8;
                    x2 += 8;
                }

                // Compare remaining 4 bytes if any (int)
                if ((length & 4) != 0) // Check if the 3rd bit (representing 4) is set
                {
                    if (*(int*)x1 != *(int*)x2) return false;
                    x1 += 4;
                    x2 += 4;
                }

                // Compare remaining 2 bytes if any (short)
                if ((length & 2) != 0) // Check if the 2nd bit (representing 2) is set
                {
                    if (*(short*)x1 != *(short*)x2) return false;
                    x1 += 2;
                    x2 += 2;
                }

                // Compare remaining 1 byte if any (byte)
                if ((length & 1) != 0) // Check if the 1st bit (representing 1) is set
                {
                    if (*x1 != *x2) return false;
                }
            }

            return true;
        }
    }
}